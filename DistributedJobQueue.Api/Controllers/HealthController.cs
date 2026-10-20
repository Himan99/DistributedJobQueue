using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DistributedJobQueueBrokerService.Controllers
{
    [ApiController]
    [Route("health")]
    public class HealthController : ControllerBase
    {
        private readonly HealthCheckService _healthCheckService;
        private readonly IConfiguration _configuration;

        public HealthController(HealthCheckService healthCheckService, IConfiguration configuration)
        {
            _healthCheckService = healthCheckService;
            _configuration = configuration;
        }

        [HttpGet("live")]
        public IActionResult GetLive()
        {
            return Ok(new { status = "healthy" });
        }

        [HttpGet("ready")]
        public async Task<IActionResult> GetReady()
        {
            var timeoutMs = _configuration.GetValue<int>("HEALTH_CHECK_DB_TIMEOUT_MS", 2000);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

            var report = await _healthCheckService.CheckHealthAsync(cts.Token);

            if (report.Status == HealthStatus.Healthy)
            {
                return Ok(new { status = "healthy" });
            }

            return StatusCode(503, new { status = "unhealthy", reason = "Database unreachable" });
        }
    }
}