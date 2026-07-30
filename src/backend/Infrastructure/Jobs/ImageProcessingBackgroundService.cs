using MuriloAI.Backend.Application.Jobs;
using MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;
using MuriloAI.Backend.Domain.Models;
using Microsoft.Extensions.Options;

namespace MuriloAI.Backend.Infrastructure.Jobs;

public sealed class ImageProcessingBackgroundService : BackgroundService
{
    private readonly IImageProcessingJobQueue _queue;
    private readonly IImageProcessingJobStore _store;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ImageProcessingBackgroundService> _logger;
    private readonly ImageJobsOptions _options;

    public ImageProcessingBackgroundService(
        IImageProcessingJobQueue queue,
        IImageProcessingJobStore store,
        IServiceProvider serviceProvider,
        ILogger<ImageProcessingBackgroundService> logger,
        IOptions<ImageJobsOptions> options)
    {
        _queue = queue;
        _store = store;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
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
        if (await IsCanceledAsync(job.Id, cancellationToken))
        {
            _logger.LogInformation("Cancel applied before processing started. JobId={JobId}", job.Id);
            return;
        }

        job.Status = ImageProcessingJobStatus.Processing;
        job.StartedAt = DateTimeOffset.UtcNow;
        job.Progress = 10;
        await _store.UpdateAsync(job, cancellationToken);

        var attemptTimeoutSeconds = Math.Max(1, _options.AttemptTimeoutSeconds);
        var maxAttempts = Math.Max(1, _options.MaxAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCancellation.CancelAfter(TimeSpan.FromSeconds(attemptTimeoutSeconds));

            try
            {
                _logger.LogInformation("Starting image job attempt. JobId={JobId}, CorrelationId={CorrelationId}, Attempt={Attempt}/{MaxAttempts}, TimeoutSeconds={TimeoutSeconds}", job.Id, job.CorrelationId, attempt, maxAttempts, attemptTimeoutSeconds);

                if (await IsCanceledAsync(job.Id, linkedCancellation.Token))
                {
                    await MarkCanceledAsync(job, cancellationToken, "Cancellation requested before processing attempt.");
                    return;
                }

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
                    linkedCancellation.Token);

                if (await IsCanceledAsync(job.Id, linkedCancellation.Token))
                {
                    await MarkCanceledAsync(job, cancellationToken, "Cancellation requested after engine processing.");
                    return;
                }

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

                _logger.LogInformation("Background image job completed. JobId={JobId}, CorrelationId={CorrelationId}, Attempt={Attempt}/{MaxAttempts}", job.Id, job.CorrelationId, attempt, maxAttempts);
                return;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (await IsCanceledAsync(job.Id, cancellationToken))
                {
                    await MarkCanceledAsync(job, cancellationToken, "Cancellation requested during processing attempt.");
                    return;
                }

                if (attempt < maxAttempts)
                {
                    _logger.LogWarning(ex, "Image job attempt timed out. Retrying. JobId={JobId}, CorrelationId={CorrelationId}, Attempt={Attempt}/{MaxAttempts}", job.Id, job.CorrelationId, attempt, maxAttempts);
                    await ApplyBackoffAsync(attempt, cancellationToken);
                    continue;
                }

                job.Status = ImageProcessingJobStatus.Failed;
                job.ErrorMessage = "Processing attempt timed out.";
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Progress = 100;
                await _store.UpdateAsync(job, cancellationToken);
                TryDeleteWorkingDirectory(job.WorkingDirectory);
                _logger.LogError(ex, "Background image job failed after retries. JobId={JobId}, CorrelationId={CorrelationId}", job.Id, job.CorrelationId);
                return;
            }
            catch (IOException ex)
            {
                if (await IsCanceledAsync(job.Id, cancellationToken))
                {
                    await MarkCanceledAsync(job, cancellationToken, "Cancellation requested during I/O handling.");
                    return;
                }

                if (attempt < maxAttempts)
                {
                    _logger.LogWarning(ex, "Image job attempt failed due to I/O. Retrying. JobId={JobId}, CorrelationId={CorrelationId}, Attempt={Attempt}/{MaxAttempts}", job.Id, job.CorrelationId, attempt, maxAttempts);
                    await ApplyBackoffAsync(attempt, cancellationToken);
                    continue;
                }

                job.Status = ImageProcessingJobStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Progress = 100;
                await _store.UpdateAsync(job, cancellationToken);
                TryDeleteWorkingDirectory(job.WorkingDirectory);
                _logger.LogError(ex, "Background image job failed after retries. JobId={JobId}, CorrelationId={CorrelationId}", job.Id, job.CorrelationId);
                return;
            }
            catch (Exception ex)
            {
                if (await IsCanceledAsync(job.Id, cancellationToken))
                {
                    await MarkCanceledAsync(job, cancellationToken, "Cancellation requested during exception handling.");
                    return;
                }

                job.Status = ImageProcessingJobStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Progress = 100;
                await _store.UpdateAsync(job, cancellationToken);
                TryDeleteWorkingDirectory(job.WorkingDirectory);
                _logger.LogError(ex, "Background image job failed without retry. JobId={JobId}, CorrelationId={CorrelationId}", job.Id, job.CorrelationId);
                return;
            }
        }
    }

    private async Task<bool> IsCanceledAsync(string jobId, CancellationToken cancellationToken)
    {
        var current = await _store.GetByIdAsync(jobId, cancellationToken);
        return current?.Status == ImageProcessingJobStatus.Canceled;
    }

    private async Task MarkCanceledAsync(ImageProcessingJob job, CancellationToken cancellationToken, string reason)
    {
        if (job.Status == ImageProcessingJobStatus.Canceled)
        {
            return;
        }

        job.Status = ImageProcessingJobStatus.Canceled;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.Progress = 100;
        job.ErrorMessage = reason;
        await _store.UpdateAsync(job, cancellationToken);
        _logger.LogInformation("Cancel applied. JobId={JobId}, Reason={Reason}", job.Id, reason);
    }

    private static async Task ApplyBackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        var delayMs = attempt switch
        {
            1 => 500,
            2 => 1500,
            _ => 0
        };

        if (delayMs > 0)
        {
            await Task.Delay(delayMs, cancellationToken);
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
