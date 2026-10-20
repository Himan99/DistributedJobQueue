using DistributedJobQueue.Core.Persistence;
using DistributedJobQueueBrokerService.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton(new DbConnectionFactory(
    builder.Configuration.GetConnectionString("JobQueueDb")!));

builder.Services.AddSingleton<IJobStore, PostgresStore>();

// Health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("JobQueueDb")!,
        name: "postgresql",
        timeout: TimeSpan.FromMilliseconds(builder.Configuration.GetValue<int>("HEALTH_CHECK_DB_TIMEOUT_MS", 2000)));

// Queue depth metrics updater
var queueDepthInterval = TimeSpan.FromSeconds(
    builder.Configuration.GetValue<int>("QUEUE_DEPTH_UPDATE_INTERVAL_SECONDS", 15));
builder.Services.AddHostedService(sp => new QueueDepthUpdater(
    sp.GetRequiredService<IJobStore>(),
    sp.GetRequiredService<ILogger<QueueDepthUpdater>>(),
    queueDepthInterval));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

// Prometheus metrics
app.UseMetricServer();
app.MapMetrics("/metrics");

// Health checks
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false // No checks for liveness
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "postgresql"
});

app.MapControllers();

app.Run();
