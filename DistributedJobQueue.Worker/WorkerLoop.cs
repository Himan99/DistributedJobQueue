using DistributedJobQueue.Core.Domain;
using DistributedJobQueue.Core.Persistence;
using DistributedJobQueueBrokerService.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributedJobQueue.Worker;

public class WorkerLoop : IHostedService
{
    private readonly IJobStore _store;
    private readonly HandlerRegistry _registry;
    private readonly HeartbeatService _heartbeatService;
    private readonly ILogger<WorkerLoop> _logger;
    private readonly string _workerId;
    private readonly List<string> _allowedTypes;
    private readonly int _poolSize;
    private readonly int _leaseSeconds;
    private readonly int _pollIntervalMs;
    private readonly int _shutdownTimeoutSeconds;
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly Dictionary<Guid, CancellationTokenSource> _inFlightJobs = new();
    private readonly object _inFlightLock = new();
    private volatile bool _stopping;

    public WorkerLoop(
        IJobStore store,
        HandlerRegistry registry,
        HeartbeatService heartbeatService,
        ILogger<WorkerLoop> logger,
        string workerId,
        string[] allowedTypes,
        int poolSize,
        int leaseSeconds,
        int pollIntervalMs,
        int shutdownTimeoutSeconds)
    {
        _store = store;
        _registry = registry;
        _heartbeatService = heartbeatService;
        _logger = logger;
        _workerId = workerId;
        _allowedTypes = allowedTypes.ToList();
        _poolSize = poolSize;
        _leaseSeconds = leaseSeconds;
        _pollIntervalMs = pollIntervalMs;
        _shutdownTimeoutSeconds = shutdownTimeoutSeconds;
        _concurrencyLimiter = new SemaphoreSlim(poolSize);

        // Record worker pool size metric
        JobQueueMetrics.WorkerPoolSize.WithLabels(_workerId).Set(poolSize);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker {WorkerId} started. Allowed types: [{AllowedTypes}] Pool size: {PoolSize}",
            _workerId, string.Join(",", _allowedTypes), _poolSize);

        while (!cancellationToken.IsCancellationRequested && !_stopping)
        {
            var job = await _store.Lease(_workerId, _allowedTypes, TimeSpan.FromSeconds(_leaseSeconds));
            if (job == null)
            {
                await Task.Delay(_pollIntervalMs, cancellationToken);
                continue;
            }

            await _concurrencyLimiter.WaitAsync(cancellationToken);

            if (_stopping)
            {
                _concurrencyLimiter.Release();
                break;
            }

            _ = Task.Run(async () =>
            {
                var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                lock (_inFlightLock)
                {
                    _inFlightJobs[job.JobId] = heartbeatCts;
                }

                try
                {
                    await ProcessJob(job, heartbeatCts.Token);
                }
                finally
                {
                    lock (_inFlightLock)
                    {
                        _inFlightJobs.Remove(job.JobId);
                    }
                    _concurrencyLimiter.Release();
                }
            }, cancellationToken);
        }

        _logger.LogInformation("Worker {WorkerId} lease loop stopped.", _workerId);
    }

    private async Task ProcessJob(LeasedJob job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker {WorkerId} processing job {JobId} of type {Type} (attempt {Attempt})",
            _workerId, job.JobId, job.Type, job.Attempt);

        // Track in-flight job
        JobQueueMetrics.WorkerInFlight.WithLabels(_workerId).Inc();

        if (!_registry.TryGet(job.Type, out var handler))
        {
            await _store.Nack(job.JobId, _workerId, $"No handler registered for type '{job.Type}'");
            JobQueueMetrics.WorkerInFlight.WithLabels(_workerId).Dec();
            return;
        }

        var heartbeatTask = _heartbeatService.StartAsync(job.JobId, cancellationToken);

        try
        {
            var result = await handler!.Invoke(job.Payload);
            await _store.Ack(job.JobId, _workerId, result ?? string.Empty);
            _logger.LogInformation("Worker {WorkerId} acked job {JobId}", _workerId, job.JobId);
        }
        catch (Exception ex)
        {
            await _store.Nack(job.JobId, _workerId, ex.Message);
            _logger.LogInformation("Worker {WorkerId} nacked job {JobId}: {Error}", _workerId, job.JobId, ex.Message);
        }
        finally
        {
            await heartbeatTask;
            JobQueueMetrics.WorkerInFlight.WithLabels(_workerId).Dec();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker {WorkerId} stopping...", _workerId);
        _stopping = true;

        // Wait for in-flight jobs to complete with timeout
        var timeout = TimeSpan.FromSeconds(_shutdownTimeoutSeconds);
        var startTime = DateTime.UtcNow;
        var drained = 0;
        var abandoned = 0;

        while (true)
        {
            CancellationTokenSource[] heartbeatCtsArray;
            lock (_inFlightLock)
            {
                if (_inFlightJobs.Count == 0) break;
                heartbeatCtsArray = _inFlightJobs.Values.ToArray();
            }

            var elapsed = DateTime.UtcNow - startTime;
            var remaining = timeout - elapsed;

            if (remaining <= TimeSpan.Zero)
            {
                _logger.LogWarning("Shutdown timeout ({Timeout}s) reached. Cancelling {Count} in-flight job(s)",
                    _shutdownTimeoutSeconds, heartbeatCtsArray.Length);

                foreach (var cts in heartbeatCtsArray)
                {
                    await cts.CancelAsync();
                }
                abandoned = heartbeatCtsArray.Length;
                break;
            }

            // Wait for any job to complete or timeout
            try
            {
                await Task.Delay(remaining, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Timeout or cancellation - check again
            }
        }

        // Wait for all heartbeat tasks to fully cancel
        CancellationTokenSource[] finalCts;
        lock (_inFlightLock)
        {
            finalCts = _inFlightJobs.Values.ToArray();
        }

        if (finalCts.Length > 0)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
        }

        drained = _inFlightJobs.Count == 0 ? _poolSize : 0; // rough estimate
        _logger.LogInformation("Worker {WorkerId} stopped. Drained: {Drained}, Abandoned: {Abandoned}",
            _workerId, drained, abandoned);
    }
}