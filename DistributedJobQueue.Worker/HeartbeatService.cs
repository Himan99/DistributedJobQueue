using Microsoft.Extensions.Logging;

namespace DistributedJobQueue.Worker;

public sealed class HeartbeatService
{
    private readonly IJobQueueApiClient _apiClient;
    private readonly string _workerId;
    private readonly TimeSpan _interval;
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(IJobQueueApiClient apiClient, string workerId, TimeSpan interval, ILogger<HeartbeatService> logger)
    {
        _apiClient = apiClient;
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
                await _apiClient.HeartbeatAsync(new HeartbeatRequestDto(jobId, _workerId, (int)_interval.TotalSeconds + 5), cancellationToken);
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