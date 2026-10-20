using DistributedJobQueue.Core.Persistence;
using DistributedJobQueue.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("JobQueueDb")
    ?? Environment.GetEnvironmentVariable("JobQueueDb__ConnectionString")
    ?? "Host=localhost;Port=5432;Database=job_queue_db;Username=job_queue_user;Password=job_queue_pass";

builder.Services.AddSingleton(new DbConnectionFactory(connectionString));
builder.Services.AddSingleton<IJobStore, PostgresStore>();

var registry = new HandlerRegistry();
registry.Register("send_card_expiry_reminder", Handlers.SuccessHandler);
registry.Register("generate_payment_report", Handlers.SuccessHandler);
registry.Register("generate_ach_file", Handlers.SuccessHandler);
builder.Services.AddSingleton(registry);

var allowedTypes = Environment.GetEnvironmentVariable("ALLOWED_TYPES")
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
var poolSize = int.Parse(Environment.GetEnvironmentVariable("POOL_SIZE") ?? "5");
var leaseSeconds = int.Parse(Environment.GetEnvironmentVariable("LEASE_SECONDS") ?? "30");
var pollIntervalMs = int.Parse(Environment.GetEnvironmentVariable("POLL_INTERVAL") ?? "2000");

builder.Services.AddSingleton(sp => new WorkerLoop(
    sp.GetRequiredService<IJobStore>(),
    sp.GetRequiredService<HandlerRegistry>(),
    $"worker-{Guid.NewGuid():N}",
    allowedTypes,
    poolSize,
    leaseSeconds,
    pollIntervalMs));
builder.Services.AddHostedService<WorkerLoop>();

using var host = builder.Build();
await host.RunAsync();
