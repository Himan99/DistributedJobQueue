// See https://aka.ms/new-console-template for more information

using DistributedJobQueue.Core.Persistence;
using DistributedJobQueueConsoleTester;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.WriteLine("Hello, World!");

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(new DbConnectionFactory(
    builder.Configuration.GetConnectionString("JobQueueDb")!));

var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Debug));

//NpgsqlLoggingConfiguration.InitializeLogging(loggerFactory, parameterLoggingEnabled: true);

builder.Services.AddSingleton<IJobStore, PostgresStore>();
builder.Services.AddHostedService<PostgresStoreTest>();

using IHost host = builder.Build();
await host.RunAsync();