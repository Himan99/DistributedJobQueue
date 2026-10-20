namespace DistributedJobQueue.Core.Domain
{
    public enum JobStatus
    {
        Queued = 0,
        Running = 1,
        Succeeded = 2,
        DeadLettered = 3
    }
}