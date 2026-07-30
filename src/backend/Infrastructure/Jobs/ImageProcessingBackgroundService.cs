using MuriloAI.Backend.Application.Jobs;
using MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;
using MuriloAI.Backend.Domain.Models;

namespace MuriloAI.Backend.Infrastructure.Jobs;

public sealed class ImageProcessingBackgroundService : BackgroundService
{
    private readonly IImageProcessingJobQueue _queue;
    private readonly IImageProcessingJobStore _store;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ImageProcessingBackgroundService> _logger;

    public ImageProcessingBackgroundService(
        IImageProcessingJobQueue queue,
        IImageProcessingJobStore store,
        IServiceProvider serviceProvider,
        ILogger<ImageProcessingBackgroundService> logger)
    {
        _queue = queue;
        _store = store;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await _queue.DequeueAsync(stoppingToken);
            if (job is null)
            {
                continue;
            }

            await ProcessJobAsync(job, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(ImageProcessingJob job, CancellationToken cancellationToken)
    {
        job.Status = ImageProcessingJobStatus.Processing;
        job.StartedAt = DateTimeOffset.UtcNow;
        job.Progress = 10;
        await _store.UpdateAsync(job, cancellationToken);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var useCase = scope.ServiceProvider.GetRequiredService<IProcessImageWithEngineUseCase>();

            if (string.IsNullOrWhiteSpace(job.InputPath) || !File.Exists(job.InputPath))
            {
                throw new InvalidOperationException("Input file for job was not found.");
            }

            job.Progress = 20;
            await _store.UpdateAsync(job, cancellationToken);

            await using var inputStream = File.OpenRead(job.InputPath);
            var result = await useCase.ExecuteAsync(
                new ProcessImageWithEngineCommand(
                    job.OriginalFileName ?? "image",
                    job.ContentType,
                    inputStream,
                    inputStream.Length,
                    job.Engine,
                    job.OptionsJson,
                    job.CorrelationId ?? Guid.NewGuid().ToString("N"),
                    PreserveOutput: true),
                cancellationToken);

            job.Progress = 80;
            await _store.UpdateAsync(job, cancellationToken);

            if (!result.Success || string.IsNullOrWhiteSpace(result.OutputPath) || !File.Exists(result.OutputPath))
            {
                job.Status = ImageProcessingJobStatus.Failed;
                job.ErrorMessage = string.IsNullOrWhiteSpace(result.Message) ? "Engine processing failed." : result.Message;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Progress = 100;
                await _store.UpdateAsync(job, cancellationToken);
                TryDeleteWorkingDirectory(job.WorkingDirectory);
                return;
            }

            var resultDirectory = Path.Combine(job.WorkingDirectory ?? Path.GetTempPath(), "result");
            Directory.CreateDirectory(resultDirectory);
            var targetOutputPath = Path.Combine(resultDirectory, Path.GetFileName(result.OutputPath));
            File.Copy(result.OutputPath, targetOutputPath, overwrite: true);

            job.OutputPath = targetOutputPath;
            job.Status = ImageProcessingJobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Progress = 100;
            await _store.UpdateAsync(job, cancellationToken);

            _logger.LogInformation("Background image job completed. JobId={JobId}, CorrelationId={CorrelationId}", job.Id, job.CorrelationId);
        }
        catch (Exception ex)
        {
            job.Status = ImageProcessingJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Progress = 100;
            await _store.UpdateAsync(job, cancellationToken);
            TryDeleteWorkingDirectory(job.WorkingDirectory);
            _logger.LogError(ex, "Background image job failed. JobId={JobId}, CorrelationId={CorrelationId}", job.Id, job.CorrelationId);
        }
    }

    private static void TryDeleteWorkingDirectory(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
