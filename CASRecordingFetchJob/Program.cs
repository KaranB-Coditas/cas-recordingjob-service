using CASRecordingFetchJob.Helpers;
using CASRecordingFetchJob.Model;
using CASRecordingFetchJob.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Serilog;
using Serilog.Sinks.GoogleCloudLogging;
using Google.Api;
using CASRecordingFetchJob.Middleware;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<RecordingJobDBContext>(options =>
   options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()                
    .Enrich.FromLogContext()
    .WriteTo.Console()                         
    .WriteTo.File("C:\\Logs\\log-.log",         
                  rollingInterval: RollingInterval.Day,
                  retainedFileCountLimit: 7,
                  outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] [CorrelationId:{CorrelationId}] {Message:lj}{NewLine}{Exception}")
    //.WriteTo.GoogleCloudLogging(projectId: "cas-prod-env")
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect("10.40.1.79:6379")
);

builder.Services.AddSingleton<IDistributedLockManager, RedisLockManager>();

builder.Services.AddScoped<IRecordingJobService, RecordingJobService>();

builder.Services.AddScoped<GoogleCloudStorageHelper>();

builder.Services.AddScoped<SshClientHelper>();

builder.Services.AddScoped<RecordingDataService>();
builder.Services.AddScoped<RecordingProcessor>();
builder.Services.AddScoped<RecordingDownloader>();
builder.Services.AddScoped<CommonFunctions>();
builder.Services.AddScoped<RecordingMover>();
builder.Services.AddScoped<ICorrelationIdAccessor, CorrelationIdAccessor>();

var app = builder.Build();

var enableSwagger = builder.Configuration.GetValue<bool>("EnableSwaggerUI");
if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.UseMiddleware<CorrelationIdMiddleware>();

app.MapControllers();

app.Run();
