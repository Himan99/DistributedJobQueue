using DistributedJobQueue.Core.Domain;
using DistributedJobQueue.Core.Persistence;
using Microsoft.Extensions.Hosting;

namespace DistributedJobQueue.Worker;

public class WorkerLoop : IHostedService
{
    private readonly IJobStore _store;
    private readonly HandlerRegistry _registry;
    private readonly string _workerId;
    private readonly List<string> _allowedTypes;
    private readonly int _poolSize;
    private readonly int _leaseSeconds;
    private readonly int _pollIntervalMs;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public WorkerLoop(
        IJobStore store,
        HandlerRegistry registry,
        string workerId,
        string[] allowedTypes,
        int poolSize,
        int leaseSeconds,
        int pollIntervalMs)
    {
        _store = store;
        _registry = registry;
        _workerId = workerId;
        _allowedTypes = allowedTypes.ToList();
        _poolSize = poolSize;
        _leaseSeconds = leaseSeconds;
        _pollIntervalMs = pollIntervalMs;
        _concurrencyLimiter = new SemaphoreSlim(poolSize);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Worker {_workerId} started. Allowed types: [{string.Join(",", _allowedTypes)}] Pool size: {_poolSize}");

        while (!cancellationToken.IsCancellationRequested)
        {
            var job = await _store.Lease(_workerId, _allowedTypes, TimeSpan.FromSeconds(_leaseSeconds));
            if (job == null)
            {
                await Task.Delay(_pollIntervalMs, cancellationToken);
                continue;
            }

            await _concurrencyLimiter.WaitAsync(cancellationToken);
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessJob(job, cancellationToken);
                }
                finally
                {
                    _concurrencyLimiter.Release();
                }
            }, cancellationToken);
        }

        Console.WriteLine($"Worker {_workerId} stopped.");
    }

    private async Task ProcessJob(LeasedJob job, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Worker {_workerId} processing job {job.JobId} of type {job.Type} (attempt {job.Attempt})");

        if (!_registry.TryGet(job.Type, out var handler))
        {
            await _store.Nack(job.JobId, _workerId, $"No handler registered for type '{job.Type}'");
            return;
        }

        try
        {
            var result = await handler!.Invoke(job.Payload);
            await _store.Ack(job.JobId, _workerId, result ?? string.Empty);
            Console.WriteLine($"Worker {_workerId} acked job {job.JobId}");
        }
        catch (Exception ex)
        {
            await _store.Nack(job.JobId, _workerId, ex.Message);
            Console.WriteLine($"Worker {_workerId} nacked job {job.JobId}: {ex.Message}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Worker {_workerId} stopping.");
        return Task.CompletedTask;
    }
}
