using System.Net.Http.Json;
using System.Text.Json;

namespace DistributedJobQueue.Worker;

public record LeasedJobDto(
    Guid JobId,
    string Type,
    string Payload,
    int Attempt,
    DateTime LeaseExpiresAt
);

public record LeaseRequestDto(string WorkerId, List<string> AllowedTypes, int LeaseSeconds = 30);

public record AckRequestDto(Guid JobId, string WorkerId, string ResultUrl);

public record NackRequestDto(Guid JobId, string WorkerId, string Error);

public record HeartbeatRequestDto(Guid JobId, string WorkerId, int LeaseExtensionSeconds = 30);

public record AbortRequestDto(string Reason);

public interface IJobQueueApiClient
{
    Task<LeasedJobDto?> LeaseAsync(LeaseRequestDto request, CancellationToken ct = default);
    Task<Guid> AckAsync(AckRequestDto request, CancellationToken ct = default);
    Task<Guid> NackAsync(NackRequestDto request, CancellationToken ct = default);
    Task<Guid> HeartbeatAsync(HeartbeatRequestDto request, CancellationToken ct = default);
    Task<Guid> AbortAsync(Guid jobId, AbortRequestDto request, CancellationToken ct = default);
    Task<Guid> SimulateCrashAsync(Guid jobId, CancellationToken ct = default);
}

public sealed class JobQueueApiClient : IJobQueueApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<JobQueueApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public JobQueueApiClient(HttpClient http, ILogger<JobQueueApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<LeasedJobDto?> LeaseAsync(LeaseRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/jobs/lease", request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<LeasedJobDto>(_jsonOptions, ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lease request failed for worker {WorkerId}", request.WorkerId);
            throw;
        }
    }

    public async Task<Guid> AckAsync(AckRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/jobs/ack", request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<Guid>(_jsonOptions, ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ack request failed for job {JobId}", request.JobId);
            throw;
        }
    }

    public async Task<Guid> NackAsync(NackRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/jobs/nack", request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<Guid>(_jsonOptions, ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nack request failed for job {JobId}", request.JobId);
            throw;
        }
    }

    public async Task<Guid> HeartbeatAsync(HeartbeatRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/jobs/heartbeat", request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<Guid>(_jsonOptions, ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat request failed for job {JobId}", request.JobId);
            throw;
        }
    }

    public async Task<Guid> AbortAsync(Guid jobId, AbortRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"api/jobs/{jobId}/abort", request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<Guid>(_jsonOptions, ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Abort request failed for job {JobId}", jobId);
            throw;
        }
    }

    public async Task<Guid> SimulateCrashAsync(Guid jobId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync($"api/jobs/{jobId}/simulate-crash", null, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<Guid>(_jsonOptions, ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SimulateCrash request failed for job {JobId}", jobId);
            throw;
        }
    }
}