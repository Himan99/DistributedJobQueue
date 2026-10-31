using DistributedJobQueue.Worker;
using DistributedJobQueueBrokerService.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Prometheus;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Worker configuration from environment/config
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));

// Health checks - Worker uses HTTP to Main API, no DB needed
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Worker running"));

// HTTP client for Main API
builder.Services.AddHttpClient<IJobQueueApiClient, JobQueueApiClient>(client =>
{
    var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5000";
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Worker services - register handlers
var handlerRegistry = new HandlerRegistry();
handlerRegistry.Register("email", Handlers.SuccessHandler);
handlerRegistry.Register("sms", Handlers.SuccessHandler);
handlerRegistry.Register("report", Handlers.SuccessHandler);
handlerRegistry.Register("ach", Handlers.SuccessHandler);
builder.Services.AddSingleton(handlerRegistry);

// HeartbeatService needs API client and worker ID
builder.Services.AddSingleton<HeartbeatService>(sp =>
{
    var apiClient = sp.GetRequiredService<IJobQueueApiClient>();
    var options = sp.GetRequiredService<IOptions<WorkerOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<HeartbeatService>>();
    var interval = TimeSpan.FromSeconds(options.LeaseSeconds / 2); // heartbeat at half lease interval
    return new HeartbeatService(apiClient, options.WorkerId, interval, logger);
});

builder.Services.AddSingleton<WorkerLoop>();
builder.Services.AddSingleton<IWorkerAdmin>(sp => sp.GetRequiredService<WorkerLoop>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<WorkerLoop>());

// Health checks - Worker doesn't need DB connection anymore
// builder.Services.AddHealthChecks()
//     .AddNpgSql(builder.Configuration.GetConnectionString("JobQueueDb")!,
//         name: "postgresql",
//         timeout: TimeSpan.FromMilliseconds(builder.Configuration.GetValue<int>("HEALTH_CHECK_DB_TIMEOUT_MS", 2000)));

var app = builder.Build();

// Configure the HTTP request pipeline
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
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "self"
});

app.MapControllers();

app.Run();