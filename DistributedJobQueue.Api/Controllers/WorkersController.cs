using DistributedJobQueue.Core.Domain;
using DistributedJobQueue.Core.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace DistributedJobQueueBrokerService.Controllers;

[ApiController]
[Route("api/workers")]
public class WorkersController : ControllerBase
{
    private readonly IJobStore _jobStore;
    private readonly ILogger<WorkersController> _logger;

    public WorkersController(IJobStore jobStore, ILogger<WorkersController> logger)
    {
        _jobStore = jobStore;
        _logger = logger;
    }

    // GET api/workers
    [HttpGet]
    public async Task<ActionResult<List<WorkerInfo>>> GetWorkers()
    {
        try
        {
            // Get all jobs with lease info to determine active workers
            var allJobs = await _jobStore.ListJobs(null, null);

            var workerDict = new Dictionary<string, WorkerInfoBuilder>();

            foreach (var job in allJobs.Where(j => j.Status == JobStatus.Running && j.LeasedBy != null))
            {
                if (!workerDict.TryGetValue(job.LeasedBy!, out var builder))
                {
                    builder = new WorkerInfoBuilder
                    {
                        WorkerId = job.LeasedBy!,
                        LastHeartbeat = job.LeaseExpiresAt
                    };
                    workerDict[job.LeasedBy!] = builder;
                }

                builder.InFlightCount++;
                // Track allowed types from job types
                if (!builder.AllowedTypes.Contains(job.Type))
                    builder.AllowedTypes.Add(job.Type);

                // Update last heartbeat to the most recent
                if (job.LeaseExpiresAt > (builder.LastHeartbeat ?? DateTime.MinValue))
                    builder.LastHeartbeat = job.LeaseExpiresAt;
            }

            var result = workerDict.Values.Select(b => b.Build()).ToList();
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workers");
            return Problem(detail: ex.Message);
        }
    }
}

internal class WorkerInfoBuilder
{
    public string WorkerId { get; set; } = string.Empty;
    public List<string> AllowedTypes { get; set; } = new();
    public int InFlightCount { get; set; }
    public DateTime? LastHeartbeat { get; set; }

    public WorkerInfo Build()
    {
        return new WorkerInfo
        {
            WorkerId = WorkerId,
            Pool = DeterminePool(AllowedTypes),
            AllowedTypes = AllowedTypes.ToArray(),
            PoolSize = 0, // Unknown from API perspective
            InFlightCount = InFlightCount,
            LastHeartbeat = LastHeartbeat,
            Status = "healthy"
        };
    }

    private static string DeterminePool(List<string> allowedTypes)
    {
        if (allowedTypes.Any(t => t == "email" || t == "sms"))
            return "fast";
        if (allowedTypes.Any(t => t == "report" || t == "ach"))
            return "batch";
        return "unknown";
    }
}

public record WorkerInfo
{
    public string WorkerId { get; init; } = default!;
    public string Pool { get; init; } = default!;
    public string[] AllowedTypes { get; init; } = default!;
    public int PoolSize { get; init; }
    public int InFlightCount { get; init; }
    public DateTime? LastHeartbeat { get; init; }
    public string Status { get; init; } = default!;
}