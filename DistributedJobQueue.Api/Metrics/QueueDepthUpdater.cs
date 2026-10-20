using DistributedJobQueue.Core.Persistence;
using DistributedJobQueueBrokerService.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DistributedJobQueueBrokerService.Metrics;

public class QueueDepthUpdater : BackgroundService
{
    private readonly IJobStore _store;
    private readonly ILogger<QueueDepthUpdater> _logger;
    private readonly TimeSpan _interval;

    public QueueDepthUpdater(IJobStore store, ILogger<QueueDepthUpdater> logger, TimeSpan? interval = null)
    {
        _store = store;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(15);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("QueueDepthUpdater started with interval {Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdateGauges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating queue depth gauges");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("QueueDepthUpdater stopped");
    }

    private async Task UpdateGauges()
    {
        // Get all queued jobs grouped by type
        var queuedJobs = await _store.ListJobs("queued", null);
        var runningJobs = await _store.ListJobs("running", null);

        var queuedByType = queuedJobs.GroupBy(j => j.Type).ToDictionary(g => g.Key, g => g.Count());
        var runningByType = runningJobs.GroupBy(j => j.Type).ToDictionary(g => g.Key, g => g.Count());

        // Update gauges for queued jobs
        foreach (var kvp in queuedByType)
        {
            JobQueueMetrics.JobsQueued.WithLabels(kvp.Key).Set(kvp.Value);
        }

        // Set to 0 for types that have no queued jobs but were previously tracked
        // (This is a simplification - in production you'd want to track all known types)

        // Update gauges for running jobs
        foreach (var kvp in runningByType)
        {
            JobQueueMetrics.JobsRunning.WithLabels(kvp.Key).Set(kvp.Value);
        }

        _logger.LogDebug("Updated queue depth: {Queued} queued, {Running} running",
            queuedJobs.Count, runningJobs.Count);
    }
}