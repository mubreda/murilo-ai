using MuriloAI.Backend.Application.Jobs;
using MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;
using MuriloAI.Backend.Domain.Contracts;
using MuriloAI.Backend.Domain.Models;
using MuriloAI.Backend.Infrastructure.Engines;
using MuriloAI.Backend.Infrastructure.Jobs;
using MuriloAI.Backend.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<PythonEngineRunner>();
builder.Services.AddSingleton<IImageProcessingJobQueue, ChannelImageProcessingJobQueue>();
builder.Services.AddScoped<IEngineGateway, PythonEngineGateway>();
builder.Services.AddScoped<IProcessImageWithEngineUseCase, ProcessImageWithEngineUseCase>();
builder.Services.Configure<ImageJobsOptions>(builder.Configuration.GetSection(ImageJobsOptions.SectionName));
builder.Services.Configure<ImageJobsStoreOptions>(builder.Configuration.GetSection(ImageJobsStoreOptions.SectionName));

var storeProvider = builder.Configuration.GetValue<string>("ImageJobs:StoreProvider") ?? "InMemory";
if (string.Equals(storeProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IImageProcessingJobStore, SqliteImageProcessingJobStore>();
}
else
{
    builder.Services.AddSingleton<IImageProcessingJobStore, InMemoryImageProcessingJobStore>();
}

builder.Services.AddHostedService<ImageProcessingBackgroundService>();

var app = builder.Build();

app.UseCors("AllowAngularDev");

app.MapControllers();

await RecoverPendingJobsAsync(app);

app.Run();

static async Task RecoverPendingJobsAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ImageJobRecovery");
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var store = scope.ServiceProvider.GetRequiredService<IImageProcessingJobStore>();
    var queue = scope.ServiceProvider.GetRequiredService<IImageProcessingJobQueue>();
    var options = scope.ServiceProvider.GetRequiredService<IOptions<ImageJobsOptions>>().Value;

    var storeProvider = configuration["ImageJobs:StoreProvider"] ?? "InMemory";
    var hasSqliteConnectionString = !string.IsNullOrWhiteSpace(configuration["ImageJobs:SqliteConnectionString"]);

    logger.LogInformation(
        "Starting image job recovery bootstrap. StoreProvider={StoreProvider}, HasSqliteConnectionString={HasSqliteConnectionString}, RecoveryWindowHours={RecoveryWindowHours}",
        storeProvider,
        hasSqliteConnectionString,
        options.RecoveryWindowHours);

    var pendingJobs = (await store.ListNonFinalAsync()).OrderBy(job => job.CreatedAt).ToList();
    var cutoff = DateTimeOffset.UtcNow.AddHours(-options.RecoveryWindowHours);
    var eligibleJobs = pendingJobs.Where(job => job.CreatedAt >= cutoff).ToList();
    var ignoredJobs = pendingJobs.Count - eligibleJobs.Count;

    logger.LogInformation("Found {FoundCount} non-final image job(s) for recovery bootstrap, {EligibleCount} eligible by recovery window, {IgnoredCount} ignored by age.", pendingJobs.Count, eligibleJobs.Count, ignoredJobs);

    if (eligibleJobs.Count == 0)
    {
        logger.LogInformation("No persisted image jobs needed recovery.");
        return;
    }

    var seenJobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var recoveredJobs = new List<ImageProcessingJob>();

    foreach (var job in eligibleJobs)
    {
        if (!seenJobIds.Add(job.Id))
        {
            continue;
        }

        if (job.Status == ImageProcessingJobStatus.Processing)
        {
            job.Status = ImageProcessingJobStatus.Queued;
            job.ErrorMessage = string.IsNullOrWhiteSpace(job.ErrorMessage)
                ? "Recovered from interrupted processing state."
                : $"{job.ErrorMessage} [Recovered from interrupted processing state.]";
            await store.UpdateAsync(job);
            logger.LogInformation("Recovered interrupted image job by resetting status to Queued. JobId={JobId}", job.Id);
        }

        await queue.QueueAsync(job);
        recoveredJobs.Add(job);
        logger.LogInformation("Re-enqueued recovered image job. JobId={JobId}, Status={Status}", job.Id, job.Status);
    }

    logger.LogInformation("Recovered {Count} persisted image job(s) for startup processing. JobIds={JobIds}", recoveredJobs.Count, string.Join(",", recoveredJobs.Select(job => job.Id)));
}

public partial class Program;
