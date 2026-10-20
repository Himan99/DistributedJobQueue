namespace DistributedJobQueue.Core.Domain
{
    public class Job
    {
        public Guid JobId { get; init; }
        public string Type { get; init; } = default!;
        public string Payload { get; init; } = default!;
        public JobStatus Status { get; init; }
        public int Attempts { get; init; }
        public int MaxRetries { get; init; }
        public int Priority { get; init; }
        public string? IdempotencyKey { get; init; }
        public string? LeasedBy { get; init; }
        public DateTime? LeaseExpiresAt { get; init; }
        public DateTime? RunAfter { get; init; }
        public string? ResultUrl { get; init; }
        public string? LastError { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? FinishedAt { get; init; }
    }
}