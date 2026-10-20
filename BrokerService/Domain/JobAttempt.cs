namespace DistributedJobQueue.Core.Domain
{
    public class JobAttempt
    {
        public Guid Id { get; init; }
        public Guid JobId { get; init; }
        public int AttemptNum { get; init; }
        public string WorkerId { get; init; } = default!;
        public string Outcome { get; init; } = default!;
        public string? Error { get; init; }
        public DateTime StartedAt { get; init; }
        public DateTime? FinishedAt { get; init; }
    }
}