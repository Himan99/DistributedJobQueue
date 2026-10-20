namespace DistributedJobQueue.Core.Domain
{
    public class LeasedJob
    {
        public Guid JobId { get; init; }
        public string Type { get; init; } = default!;
        public string Payload { get; init; } = default!;
        public int Attempt { get; init; }
        public DateTime LeaseExpiresAt { get; init; }
    }
}
