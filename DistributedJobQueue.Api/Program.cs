using DistributedJobQueue.Core.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddSingleton(new DbConnectionFactory(
    builder.Configuration.GetConnectionString("JobQueueDb")!));

var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Debug));

builder.Services.AddSingleton<IJobStore, PostgresStore>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
