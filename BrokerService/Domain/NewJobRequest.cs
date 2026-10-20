namespace DistributedJobQueue.Core.Domain
{
    public record NewJobRequest(
        string Type,
        string Payload,
        int MaxRetries = 3,
        string? IdempotencyKey = null,
        TimeSpan? RunAfter = null,
        int Priority = 0
    );
}