using DistributedJobQueue.Core.Domain;

namespace DistributedJobQueue.Core.Persistence
{
    public interface IJobStore
    {
        Task<Guid> Save(NewJobRequest request);
        Task<LeasedJob?> Lease(string workerId, List<string> allowedTypes, TimeSpan leaseDuration);
        Task<Guid> HeartBeat(Guid jobId, string workerId, TimeSpan leaseExtension);
        Task<Guid> Ack(Guid jobId, string workerId, string resultUrl);
        Task<Guid> Nack(Guid jobId, string workerId, string error);
        Task<Job?> GetJob(Guid id);
        Task<List<Job>> ListJobs(string? status, string? type);
        Task Replay(Guid jobId);
        Task<List<JobAttempt>> GetAttempts(Guid jobId);
    }
}