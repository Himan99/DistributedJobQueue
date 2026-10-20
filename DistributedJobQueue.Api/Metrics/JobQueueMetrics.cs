using Prometheus;

namespace DistributedJobQueueBrokerService.Metrics;

public static class JobQueueMetrics
{
    // Job state gauges
    public static readonly Gauge JobsQueued = Prometheus.Metrics
        .CreateGauge("jobs_queued_total", "Current number of queued jobs", new[] { "type" });

    public static readonly Gauge JobsRunning = Prometheus.Metrics
        .CreateGauge("jobs_running_total", "Current number of running jobs", new[] { "type" });

    // Job outcome counters
    public static readonly Counter JobsSucceeded = Prometheus.Metrics
        .CreateCounter("jobs_succeeded_total", "Total number of jobs acknowledged successfully");

    public static readonly Counter JobsDeadLettered = Prometheus.Metrics
        .CreateCounter("jobs_dead_lettered_total", "Total number of jobs dead-lettered");

    // Latency histograms
    public static readonly Histogram JobLeaseDuration = Prometheus.Metrics
        .CreateHistogram("job_lease_duration_seconds", "Time from lease to ack/nack", new[] { "outcome" },
            new HistogramConfiguration
            {
                Buckets = Histogram.ExponentialBuckets(0.1, 2, 10)
            });

    public static readonly Histogram JobRetryBackoff = Prometheus.Metrics
        .CreateHistogram("job_retry_backoff_seconds", "Configured backoff duration per nack",
            new HistogramConfiguration
            {
                Buckets = Histogram.ExponentialBuckets(1, 2, 10)
            });

    // Worker metrics
    public static readonly Gauge WorkerPoolSize = Prometheus.Metrics
        .CreateGauge("worker_pool_size", "Configured worker pool size", new[] { "pool" });

    public static readonly Gauge WorkerInFlight = Prometheus.Metrics
        .CreateGauge("worker_in_flight", "Currently executing jobs in worker pool", new[] { "pool" });
}