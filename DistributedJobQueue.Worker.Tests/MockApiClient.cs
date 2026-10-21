using DistributedJobQueue.Worker;

class MockJobQueueApiClient : IJobQueueApiClient
{
    private readonly Dictionary<Guid, LeasedJobDto> _jobs = new();
    private readonly object _lock = new();

    public Task<LeasedJobDto?> LeaseAsync(LeaseRequestDto request, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var job = _jobs.Values.FirstOrDefault(j => request.AllowedTypes.Contains(j.Type));
            if (job != null)
            {
                _jobs.Remove(job.JobId);
                return Task.FromResult<LeasedJobDto?>(job);
            }
            return Task.FromResult<LeasedJobDto?>(null);
        }
    }

    public Task<Guid> AckAsync(AckRequestDto request, CancellationToken ct = default) => Task.FromResult(request.JobId);
    public Task<Guid> NackAsync(NackRequestDto request, CancellationToken ct = default) => Task.FromResult(request.JobId);
    public Task<Guid> HeartbeatAsync(HeartbeatRequestDto request, CancellationToken ct = default) => Task.FromResult(request.JobId);
    public Task<Guid> AbortAsync(Guid jobId, AbortRequestDto request, CancellationToken ct = default) => Task.FromResult(jobId);
    public Task<Guid> SimulateCrashAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult(jobId);

    public void AddJob(LeasedJobDto job) => _jobs[job.JobId] = job;
}