using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DistributedJobQueueBrokerService.Metrics;

namespace DistributedJobQueue.Worker;

public class WorkerLoop : IHostedService, IWorkerAdmin
{
    private readonly IJobQueueApiClient _apiClient;
    private readonly HandlerRegistry _registry;
    private readonly HeartbeatService _heartbeatService;
    private readonly ILogger<WorkerLoop> _logger;
    private readonly WorkerOptions _options;
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly Dictionary<Guid, CancellationTokenSource> _inFlightJobs = new();
    private readonly object _inFlightLock = new();
    private volatile bool _stopping;
    private volatile bool _shutdownRequested;

    public WorkerLoop(
        IJobQueueApiClient apiClient,
        HandlerRegistry registry,
        HeartbeatService heartbeatService,
        ILogger<WorkerLoop> logger,
        IOptions<WorkerOptions> options)
    {
        _apiClient = apiClient;
        _registry = registry;
        _heartbeatService = heartbeatService;
        _logger = logger;
        _options = options.Value;
        _concurrencyLimiter = new SemaphoreSlim(_options.PoolSize);

        JobQueueMetrics.WorkerPoolSize.WithLabels(_options.WorkerId).Set(_options.PoolSize);
    }

    public string WorkerId => _options.WorkerId;
    public IReadOnlyList<string> AllowedTypes => _options.AllowedTypes;
    public int PoolSize => _options.PoolSize;
    public bool IsStopping => _stopping || _shutdownRequested;

    public int InFlightCount
    {
        get
        {
            lock (_inFlightLock)
            {
                return _inFlightJobs.Count;
            }
        }
    }

    public DateTime? LastHeartbeat { get; private set; }

    // Admin operations
    public Task RequestShutdownAsync()
    {
        _shutdownRequested = true;
        return Task.CompletedTask;
    }

    public async Task AbortJobAsync(Guid jobId, string reason)
    {
        lock (_inFlightLock)
        {
            if (_inFlightJobs.TryGetValue(jobId, out var cts))
            {
                cts.Cancel();
            }
        }
        await _apiClient.AbortAsync(jobId, new AbortRequestDto(reason));
    }

    public async Task SimulateCrashAsync(Guid jobId)
    {
        lock (_inFlightLock)
        {
            if (_inFlightJobs.TryGetValue(jobId, out var cts))
            {
                cts.Cancel();
            }
        }
        await _apiClient.SimulateCrashAsync(jobId);
    }

    public async Task StopAllHeartbeatsAsync()
    {
        CancellationTokenSource[] heartbeats;
        lock (_inFlightLock)
        {
            heartbeats = _inFlightJobs.Values.ToArray();
        }
        foreach (var cts in heartbeats)
        {
            await cts.CancelAsync();
        }
    }

    public WorkerInfo GetWorkerInfo()
    {
        return new WorkerInfo
        {
            WorkerId = _options.WorkerId,
            Pool = DeterminePool(_options.AllowedTypes),
            AllowedTypes = _options.AllowedTypes.ToArray(),
            PoolSize = _options.PoolSize,
            InFlightCount = InFlightCount,
            LastHeartbeat = LastHeartbeat,
            Status = IsStopping ? "stopping" : "healthy"
        };
    }

    private static string DeterminePool(List<string> allowedTypes)
    {
        if (allowedTypes.Any(t => t == "email" || t == "sms"))
            return "fast";
        if (allowedTypes.Any(t => t == "report" || t == "ach"))
            return "batch";
        return "unknown";
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker {WorkerId} started. Allowed types: [{AllowedTypes}] Pool size: {PoolSize}",
            _options.WorkerId, string.Join(",", _options.AllowedTypes), _options.PoolSize);

        var leaseRequest = new LeaseRequestDto(
            _options.WorkerId,
            _options.AllowedTypes,
            _options.LeaseSeconds);

        while (!cancellationToken.IsCancellationRequested && !_stopping)
        {
            LeasedJobDto? job;
            try
            {
                job = await _apiClient.LeaseAsync(leaseRequest, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leasing job, retrying in {PollIntervalMs}ms", _options.PollIntervalMs);
                await Task.Delay(_options.PollIntervalMs, cancellationToken);
                continue;
            }

            if (job == null)
            {
                await Task.Delay(_options.PollIntervalMs, cancellationToken);
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

        _logger.LogInformation("Worker {WorkerId} lease loop stopped.", _options.WorkerId);
    }

    private async Task ProcessJob(LeasedJobDto job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker {WorkerId} processing job {JobId} of type {Type} (attempt {Attempt})",
            _options.WorkerId, job.JobId, job.Type, job.Attempt);

        JobQueueMetrics.WorkerInFlight.WithLabels(_options.WorkerId).Inc();

        if (!_registry.TryGet(job.Type, out var handler))
        {
            await _apiClient.NackAsync(new NackRequestDto(job.JobId, _options.WorkerId, $"No handler registered for type '{job.Type}'"), cancellationToken);
            JobQueueMetrics.WorkerInFlight.WithLabels(_options.WorkerId).Dec();
            return;
        }

        var heartbeatTask = _heartbeatService.StartAsync(job.JobId, cancellationToken);

        try
        {
            var result = await handler!.Invoke(job.Payload);
            await _apiClient.AckAsync(new AckRequestDto(job.JobId, _options.WorkerId, result ?? string.Empty), cancellationToken);
            _logger.LogInformation("Worker {WorkerId} acked job {JobId}", _options.WorkerId, job.JobId);
        }
        catch (Exception ex)
        {
            await _apiClient.NackAsync(new NackRequestDto(job.JobId, _options.WorkerId, ex.Message), cancellationToken);
            _logger.LogInformation("Worker {WorkerId} nacked job {JobId}: {Error}", _options.WorkerId, job.JobId, ex.Message);
        }
        finally
        {
            await heartbeatTask;
            JobQueueMetrics.WorkerInFlight.WithLabels(_options.WorkerId).Dec();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker {WorkerId} stopping...", _options.WorkerId);
        _stopping = true;

        var timeout = TimeSpan.FromSeconds(_options.ShutdownTimeoutSeconds);
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
                    _options.ShutdownTimeoutSeconds, heartbeatCtsArray.Length);

                foreach (var cts in heartbeatCtsArray)
                {
                    await cts.CancelAsync();
                }
                abandoned = heartbeatCtsArray.Length;
                break;
            }

            try
            {
                await Task.Delay(remaining, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

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
            }
        }

        drained = _inFlightJobs.Count == 0 ? _options.PoolSize : 0;
        _logger.LogInformation("Worker {WorkerId} stopped. Drained: {Drained}, Abandoned: {Abandoned}",
            _options.WorkerId, drained, abandoned);
    }
}

public sealed class WorkerOptions
{
    public string WorkerId { get; set; } = string.Empty;
    public List<string> AllowedTypes { get; set; } = new();
    public int PoolSize { get; set; } = 10;
    public int LeaseSeconds { get; set; } = 30;
    public int PollIntervalMs { get; set; } = 2000;
    public int ShutdownTimeoutSeconds { get; set; } = 60;
}

public interface IWorkerAdmin
{
    string WorkerId { get; }
    IReadOnlyList<string> AllowedTypes { get; }
    int PoolSize { get; }
    bool IsStopping { get; }
    int InFlightCount { get; }
    DateTime? LastHeartbeat { get; }
    Task RequestShutdownAsync();
    Task AbortJobAsync(Guid jobId, string reason);
    Task SimulateCrashAsync(Guid jobId);
    Task StopAllHeartbeatsAsync();
    WorkerInfo GetWorkerInfo();
}

public record WorkerInfo
{
    public string WorkerId { get; init; } = default!;
    public string Pool { get; init; } = default!;
    public string[] AllowedTypes { get; init; } = default!;
    public int PoolSize { get; init; }
    public int InFlightCount { get; init; }
    public DateTime? LastHeartbeat { get; init; }
    public string Status { get; init; } = default!;
}