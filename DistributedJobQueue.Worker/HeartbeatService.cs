using DistributedJobQueue.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace DistributedJobQueue.Worker;

public sealed class HeartbeatService
{
    private readonly IJobStore _store;
    private readonly string _workerId;
    private readonly TimeSpan _interval;
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(IJobStore store, string workerId, TimeSpan interval, ILogger<HeartbeatService> logger)
    {
        _store = store;
        _workerId = workerId;
        _interval = interval;
        _logger = logger;
    }

    public async Task StartAsync(Guid jobId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting heartbeat for job {JobId} with interval {Interval}", jobId, _interval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                await _store.HeartBeat(jobId, _workerId, _interval + TimeSpan.FromSeconds(5));
                _logger.LogDebug("Heartbeat sent for job {JobId}", jobId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat failed for job {JobId} (will retry)", jobId);
            }
        }

        _logger.LogDebug("Heartbeat stopped for job {JobId}", jobId);
    }
}