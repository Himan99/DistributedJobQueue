using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using DistributedJobQueue.Core.Domain;
using DistributedJobQueue.Core.Persistence;
using DistributedJobQueueBrokerService.Metrics;
using Microsoft.AspNetCore.Mvc;

namespace DistributedJobQueueBrokerService.Controllers
{
    [ApiController]
    [Route("api/jobs/")]
    public class JobsController : ControllerBase
    {
        private readonly IJobStore _jobStore;

        public JobsController(IJobStore jobStore)
        {
            _jobStore = jobStore;
        }

        // POST api/jobs/save
        [HttpPost("save")]
        public async Task<ActionResult<Guid>> Save([FromBody] NewJobRequest request)
        {
            if (request is null) return BadRequest("Request body required.");
            try
            {
                var id = await _jobStore.Save(request);
                JobQueueMetrics.JobsQueued.WithLabels(request.Type).Inc();
                return Ok(id);
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        // POST api/jobs/lease
        [HttpPost("lease")]
        public async Task<ActionResult<LeasedJob>> Lease([FromBody] LeaseRequest request)
        {
            if (request is null) return BadRequest("Request body required.");
            try
            {
                var lease = await _jobStore.Lease(request.WorkerId, request.AllowedTypes ?? new List<string>(), TimeSpan.FromSeconds(request.LeaseSeconds));
                if (lease == null) return NoContent();

                // Track running job
                JobQueueMetrics.JobsRunning.WithLabels(lease.Type).Inc();

                return Ok(lease);
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        // POST api/jobs/ack
        [HttpPost("ack")]
        public async Task<ActionResult<Guid>> Ack([FromBody] AckRequest request)
        {
            if (request is null) return BadRequest("Request body required.");
            try
            {
                var id = await _jobStore.Ack(request.JobId, request.WorkerId, request.ResultUrl);
                JobQueueMetrics.JobsSucceeded.Inc();
                JobQueueMetrics.JobsRunning.WithLabels(request.WorkerId).Dec();
                return Ok(id);
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        // POST api/jobs/nack
        [HttpPost("nack")]
        public async Task<ActionResult<Guid>> Nack([FromBody] NackRequest request)
        {
            if (request is null) return BadRequest("Request body required.");
            try
            {
                var id = await _jobStore.Nack(request.JobId, request.WorkerId, request.Error);
                JobQueueMetrics.JobsDeadLettered.Inc();
                JobQueueMetrics.JobsRunning.WithLabels(request.WorkerId).Dec();
                return Ok(id);
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        // POST api/jobs/heartbeat
        [HttpPost("heartbeat")]
        public async Task<ActionResult<Guid>> Heartbeat([FromBody] HeartbeatRequest request)
        {
            if (request is null) return BadRequest("Request body required.");
            try
            {
                var id = await _jobStore.HeartBeat(request.JobId, request.WorkerId, TimeSpan.FromSeconds(request.LeaseExtensionSeconds));
                return Ok(id);
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        // GET api/jobs/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<Job>> GetJob(Guid id)
        {
            try
            {
                var job = await _jobStore.GetJob(id);
                if (job == null) return NotFound();
                return Ok(job);
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        // GET api/jobs?status=...&type=...
        [HttpGet]
        public async Task<ActionResult<List<Job>>> ListJobs(string? status, string? type)
        {
            try
            {
                var jobs = await _jobStore.ListJobs(status, type);
                return Ok(jobs);
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        // POST api/jobs/{id}/replay
        [HttpPost("{id}/replay")]
        public async Task<ActionResult> ReplayJob(Guid id)
        {
            try
            {
                var job = await _jobStore.GetJob(id);
                if (job == null) return NotFound();
                if (job.Status != JobStatus.DeadLettered)
                    return BadRequest("Job is not in dead_lettered status.");

                await _jobStore.Replay(id);
                return Ok(id);
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        // GET api/jobs/{id}/attempts
        [HttpGet("{id}/attempts")]
        public async Task<ActionResult<List<JobAttempt>>> GetAttempts(Guid id)
        {
            try
            {
                var job = await _jobStore.GetJob(id);
                if (job == null) return NotFound();

                var attempts = await _jobStore.GetAttempts(id);
                return Ok(attempts);
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        // --- Request DTOs used by the controller ---
        public record LeaseRequest(string WorkerId, List<string>? AllowedTypes, int LeaseSeconds = 30);
        public record AckRequest(Guid JobId, string WorkerId, string ResultUrl);
        public record NackRequest(Guid JobId, string WorkerId, string Error);
        public record HeartbeatRequest(Guid JobId, string WorkerId, int LeaseExtensionSeconds = 30);
    }
}