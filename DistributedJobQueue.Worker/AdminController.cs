using Microsoft.AspNetCore.Mvc;

namespace DistributedJobQueue.Worker;

[ApiController]
[Route("admin")]
public class AdminController : ControllerBase
{
    private readonly IWorkerAdmin _workerAdmin;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IWorkerAdmin workerAdmin, ILogger<AdminController> logger)
    {
        _workerAdmin = workerAdmin;
        _logger = logger;
    }

    // POST /admin/jobs/{id}/abort
    [HttpPost("jobs/{id}/abort")]
    public async Task<ActionResult> AbortJob(Guid id, [FromBody] AbortRequest? request)
    {
        var reason = request?.Reason ?? "Force aborted via admin API";
        _logger.LogInformation("Aborting job {JobId}: {Reason}", id, reason);

        try
        {
            await _workerAdmin.AbortJobAsync(id, reason);
            return Ok(new { jobId = id, status = "aborted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to abort job {JobId}", id);
            return Problem(detail: ex.Message);
        }
    }

    // POST /admin/jobs/{id}/simulate-crash
    [HttpPost("jobs/{id}/simulate-crash")]
    public async Task<ActionResult> SimulateCrash(Guid id)
    {
        _logger.LogInformation("Simulating crash for job {JobId}", id);

        try
        {
            await _workerAdmin.SimulateCrashAsync(id);
            return Ok(new { jobId = id, status = "crash-simulated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to simulate crash for job {JobId}", id);
            return Problem(detail: ex.Message);
        }
    }

    // POST /admin/shutdown
    [HttpPost("shutdown")]
    public async Task<ActionResult> Shutdown()
    {
        _logger.LogInformation("Shutdown requested for worker {WorkerId}", _workerAdmin.WorkerId);

        try
        {
            await _workerAdmin.RequestShutdownAsync();
            return Ok(new { workerId = _workerAdmin.WorkerId, status = "shutting-down" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to shutdown worker {WorkerId}", _workerAdmin.WorkerId);
            return Problem(detail: ex.Message);
        }
    }

    // POST /admin/stop-heartbeat
    [HttpPost("stop-heartbeat")]
    public async Task<ActionResult> StopHeartbeat()
    {
        _logger.LogInformation("Stop heartbeat requested for worker {WorkerId}", _workerAdmin.WorkerId);

        try
        {
            await _workerAdmin.StopAllHeartbeatsAsync();
            return Ok(new { workerId = _workerAdmin.WorkerId, status = "heartbeats-stopped" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop heartbeats for worker {WorkerId}", _workerAdmin.WorkerId);
            return Problem(detail: ex.Message);
        }
    }

    // GET /admin/workers
    [HttpGet("workers")]
    public ActionResult<WorkerInfo> GetWorker()
    {
        return Ok(_workerAdmin.GetWorkerInfo());
    }
}

public record AbortRequest(string? Reason);