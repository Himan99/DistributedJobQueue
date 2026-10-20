using DistributedJobQueue.Core.Domain;
using DistributedJobQueue.Core.Persistence;
using DistributedJobQueue.Worker;
using Microsoft.Extensions.Logging;

var connectionString = Environment.GetEnvironmentVariable("JobQueueDb__ConnectionString")
    ?? "Host=localhost;Port=5432;Database=job_queue_db;Username=job_queue_user;Password=job_queue_pass";

var store = new PostgresStore(new DbConnectionFactory(connectionString));
int passed = 0, failed = 0;

async Task Run(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine($"[PASS] {name}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FAIL] {name}: {ex.Message}");
        failed++;
    }
}

async Task<Job?> WaitForStatus(Guid jobId, JobStatus target, int attempts = 30, int delayMs = 500)
{
    Job? last = null;
    for (int i = 0; i < attempts; i++)
    {
        last = await store.GetJob(jobId);
        if (last?.Status == target) return last;
        await Task.Delay(delayMs);
    }
    return last;
}

// 1. Handler registry returns a registered handler by type
await Run("HandlerRegistryRegisterAndLookup", async () =>
{
    var registry = new HandlerRegistry();
    registry.Register("send_card_expiry_reminder", Handlers.SuccessHandler);

    bool found = registry.TryGet("send_card_expiry_reminder", out var handler);
    if (!found || handler is null) throw new Exception("Registered handler could not be looked up");

    var result = await handler!.Invoke("{}");
    if (string.IsNullOrWhiteSpace(result) || !result.StartsWith("file:///output/"))
        throw new Exception($"Handler returned unexpected result: {result}");
});

// 2. Registry lookup is case-insensitive
await Run("HandlerRegistryCaseInsensitiveLookup", () =>
{
    var registry = new HandlerRegistry();
    registry.Register("Generate_Payment_Report", Handlers.SuccessHandler);

    bool found = registry.TryGet("generate_payment_report", out var _);
    if (!found) throw new Exception("Lookup should be case-insensitive");
    return Task.CompletedTask;
});

// 3. Registry returns false for an unknown type
await Run("HandlerRegistryUnknownTypeReturnsFalse", () =>
{
    var registry = new HandlerRegistry();
    bool found = registry.TryGet("does_not_exist", out var _);
    if (found) throw new Exception("Unknown type should not be found");
    return Task.CompletedTask;
});

// 4. Success handler produces a result URL
await Run("SuccessHandlerReturnsResult", async () =>
{
    var result = await Handlers.SuccessHandler("{}");
    if (string.IsNullOrWhiteSpace(result) || !result.StartsWith("file:///output/"))
        throw new Exception($"Unexpected success result: {result}");
});

// 5. Failure handler throws
await Run("FailureHandlerThrows", async () =>
{
    bool threw = false;
    try
    {
        await Handlers.FailureHandler("{}");
    }
    catch (InvalidOperationException)
    {
        threw = true;
    }
    if (!threw) throw new Exception("FailureHandler did not throw");
});

// 6. End-to-end: worker leases a job, runs its handler, and acks it
await Run("EndToEndLeaseProcessAck", async () =>
{
    var type = $"e2e_{Guid.NewGuid():N}";
    var jobId = await store.Save(new NewJobRequest(type, "{}", MaxRetries: 3));

    var registry = new HandlerRegistry();
    registry.Register(type, Handlers.SuccessHandler);

    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    var heartbeat = new HeartbeatService(store, "e2e-worker", TimeSpan.FromSeconds(10), loggerFactory.CreateLogger<HeartbeatService>());
    var worker = new WorkerLoop(store, registry, heartbeat, "e2e-worker", new[] { type }, 1, 10, 500);
    var loop = worker.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token);

    var job = await WaitForStatus(jobId, JobStatus.Succeeded);
    try { await loop; } catch (OperationCanceledException) { }

    if (job == null || job.Status != JobStatus.Succeeded)
        throw new Exception($"Expected job to succeed, got status={job?.Status} error={job?.LastError}");
    if (string.IsNullOrEmpty(job.ResultUrl))
        throw new Exception("Acked job has no result_url");
});

// 7. Worker only leases job types in ALLOWED_TYPES
await Run("WorkerRespectsAllowedTypes", async () =>
{
    var allowedType = $"allowed_{Guid.NewGuid():N}";
    var blockedType = $"blocked_{Guid.NewGuid():N}";
    var allowedId = await store.Save(new NewJobRequest(allowedType, "{}"));
    var blockedId = await store.Save(new NewJobRequest(blockedType, "{}"));

    var registry = new HandlerRegistry();
    registry.Register(allowedType, Handlers.SuccessHandler);

    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    var heartbeat = new HeartbeatService(store, "filter-worker", TimeSpan.FromSeconds(10), loggerFactory.CreateLogger<HeartbeatService>());
    var worker = new WorkerLoop(store, registry, heartbeat, "filter-worker", new[] { allowedType }, 1, 10, 300);
    var loop = worker.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);

    var allowed = await WaitForStatus(allowedId, JobStatus.Succeeded);
    try { await loop; } catch (OperationCanceledException) { }

    var blocked = await store.GetJob(blockedId);
    if (allowed == null || allowed.Status != JobStatus.Succeeded)
        throw new Exception("Allowed-type job was not processed by the worker");
    if (blocked?.Status != JobStatus.Queued)
        throw new Exception($"Blocked-type job was leased despite not being in ALLOWED_TYPES (status={blocked?.Status})");
});

Console.WriteLine($"\n{passed} passed, {failed} failed out of {passed + failed}");
return failed > 0 ? 1 : 0;
