using DistributedJobQueue.Core.Domain;
using DistributedJobQueue.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

await Run("HandlerRegistryCaseInsensitiveLookup", () =>
{
    var registry = new HandlerRegistry();
    registry.Register("Generate_Payment_Report", Handlers.SuccessHandler);

    bool found = registry.TryGet("generate_payment_report", out var _);
    if (!found) throw new Exception("Lookup should be case-insensitive");
    return Task.CompletedTask;
});

await Run("HandlerRegistryUnknownTypeReturnsFalse", () =>
{
    var registry = new HandlerRegistry();
    bool found = registry.TryGet("does_not_exist", out var _);
    if (found) throw new Exception("Unknown type should not be found");
    return Task.CompletedTask;
});

await Run("SuccessHandlerReturnsResult", async () =>
{
    var result = await Handlers.SuccessHandler("{}");
    if (string.IsNullOrWhiteSpace(result) || !result.StartsWith("file:///output/"))
        throw new Exception($"Unexpected success result: {result}");
});

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

await Run("WorkerLoopProcessJobWithMockApi", async () =>
{
    var type = $"test_{Guid.NewGuid():N}";
    var jobId = Guid.NewGuid();

    var mockApi = new MockJobQueueApiClient();
    mockApi.AddJob(new LeasedJobDto(jobId, type, "{}", 1, DateTime.UtcNow.AddMinutes(5)));

    var registry = new HandlerRegistry();
    registry.Register(type, Handlers.SuccessHandler);

    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    var heartbeat = new HeartbeatService(mockApi, "test-worker", TimeSpan.FromSeconds(10), loggerFactory.CreateLogger<HeartbeatService>());

    var options = new WorkerOptions
    {
        WorkerId = "test-worker",
        AllowedTypes = new List<string> { type },
        PoolSize = 1,
        LeaseSeconds = 10,
        PollIntervalMs = 100,
        ShutdownTimeoutSeconds = 10
    };

    var worker = new WorkerLoop(mockApi, registry, heartbeat, loggerFactory.CreateLogger<WorkerLoop>(), Options.Create(options));

    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var loopTask = worker.StartAsync(cts.Token);

    await Task.Delay(500);

    cts.Cancel();
    try { await loopTask; } catch (OperationCanceledException) { }
});

await Run("WorkerLoopGracefulShutdownWithMock", async () =>
{
    var type = $"shutdown_{Guid.NewGuid():N}";

    var mockApi = new MockJobQueueApiClient();

    var registry = new HandlerRegistry();
    registry.Register(type, Handlers.SlowHandler(1500));

    using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    var heartbeat = new HeartbeatService(mockApi, "shutdown-worker", TimeSpan.FromSeconds(10), loggerFactory.CreateLogger<HeartbeatService>());

    var options = new WorkerOptions
    {
        WorkerId = "shutdown-worker",
        AllowedTypes = new List<string> { type },
        PoolSize = 1,
        LeaseSeconds = 30,
        PollIntervalMs = 100,
        ShutdownTimeoutSeconds = 10
    };

    var worker = new WorkerLoop(mockApi, registry, heartbeat, loggerFactory.CreateLogger<WorkerLoop>(), Options.Create(options));

    var cts = new CancellationTokenSource();
    var loopTask = worker.StartAsync(cts.Token);

    await Task.Delay(200);

    var stopTask = worker.StopAsync(CancellationToken.None);
    await stopTask;

    try { await loopTask; } catch (OperationCanceledException) { }
});

await Run("WorkerOptionsBinding", () =>
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:WorkerId"] = "test-worker-1",
            ["Worker:AllowedTypes:0"] = "email",
            ["Worker:AllowedTypes:1"] = "sms",
            ["Worker:PoolSize"] = "10",
            ["Worker:LeaseSeconds"] = "30",
            ["Worker:PollIntervalMs"] = "2000",
            ["Worker:ShutdownTimeoutSeconds"] = "60"
        })
        .Build();

    var options = config.GetSection("Worker").Get<WorkerOptions>();

    if (options == null) throw new Exception("Options not bound");
    if (options.WorkerId != "test-worker-1") throw new Exception("WorkerId not bound");
    if (options.AllowedTypes.Count != 2) throw new Exception("AllowedTypes not bound");
    if (options.PoolSize != 10) throw new Exception("PoolSize not bound");
    if (options.LeaseSeconds != 30) throw new Exception("LeaseSeconds not bound");
    if (options.PollIntervalMs != 2000) throw new Exception("PollIntervalMs not bound");
    if (options.ShutdownTimeoutSeconds != 60) throw new Exception("ShutdownTimeoutSeconds not bound");

    return Task.CompletedTask;
});

Console.WriteLine($"\n{passed} passed, {failed} failed out of {passed + failed}");
return failed > 0 ? 1 : 0;