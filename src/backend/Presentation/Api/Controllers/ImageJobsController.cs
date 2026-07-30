using System.Text.Json;
using MuriloAI.Backend.Application.Jobs;
using MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;
using MuriloAI.Backend.Domain.Models;
using MuriloAI.Backend.Presentation.Api.Requests;
using MuriloAI.Backend.Presentation.Api.Responses;
using Microsoft.AspNetCore.Mvc;

namespace MuriloAI.Backend.Presentation.Api.Controllers;

[ApiController]
[Route("api/image")]
public sealed class ImageJobsController : ControllerBase
{
    private readonly IImageProcessingJobStore _jobStore;
    private readonly IImageProcessingJobQueue _jobQueue;
    private readonly ILogger<ImageJobsController> _logger;

    public ImageJobsController(IImageProcessingJobStore jobStore, IImageProcessingJobQueue jobQueue, ILogger<ImageJobsController> logger)
    {
        _jobStore = jobStore;
        _jobQueue = jobQueue;
        _logger = logger;
    }

    [HttpPost("jobs")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateJob([FromForm] ProcessImageRequest request, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        if (request.File is null || request.File.Length <= 0)
        {
            return CreateProblemDetails(StatusCodes.Status400BadRequest, "A non-empty image file is required.", correlationId);
        }

        if (string.IsNullOrWhiteSpace(request.Engine))
        {
            return CreateProblemDetails(StatusCodes.Status400BadRequest, "Engine is required.", correlationId);
        }

        if (!string.IsNullOrWhiteSpace(request.Options))
        {
            try
            {
                using var document = JsonDocument.Parse(request.Options);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return CreateProblemDetails(StatusCodes.Status400BadRequest, "Options must be a valid JSON object.", correlationId);
                }
            }
            catch (JsonException)
            {
                return CreateProblemDetails(StatusCodes.Status400BadRequest, "Options must be valid JSON.", correlationId);
            }
        }

        var extension = Path.GetExtension(request.File.FileName);
        var tempRoot = Path.Combine(Path.GetTempPath(), "murilo-ai-jobs", Guid.NewGuid().ToString("N"));
        var inputDirectory = Path.Combine(tempRoot, "input");
        Directory.CreateDirectory(inputDirectory);
        var inputPath = Path.Combine(inputDirectory, $"source{extension}");

        await using (var inputStream = request.File.OpenReadStream())
        {
            await using var fileStream = System.IO.File.Create(inputPath);
            await inputStream.CopyToAsync(fileStream, cancellationToken);
        }

        var job = new ImageProcessingJob
        {
            CorrelationId = correlationId,
            Engine = request.Engine,
            OriginalFileName = request.File.FileName,
            ContentType = request.File.ContentType,
            OptionsJson = request.Options,
            InputPath = inputPath,
            WorkingDirectory = tempRoot,
            Progress = 0
        };

        await _jobStore.CreateAsync(job, cancellationToken);
        await _jobQueue.QueueAsync(job, cancellationToken);

        var response = new CreateImageJobResponse(
            job.Id,
            job.Status.ToString(),
            job.CreatedAt,
            correlationId,
            $"/api/image/jobs/{job.Id}",
            $"/api/image/jobs/{job.Id}/result");

        return Accepted(response);
    }

    [HttpGet("jobs/{jobId}")]
    public async Task<IActionResult> GetStatus(string jobId, CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return CreateProblemDetails(StatusCodes.Status404NotFound, "Job not found.", HttpContext.TraceIdentifier);
        }

        var response = new ImageJobStatusResponse(
            job.Id,
            job.Status.ToString(),
            job.Progress,
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.ErrorMessage,
            job.Status == ImageProcessingJobStatus.Completed ? $"/api/image/jobs/{job.Id}/result" : null,
            job.CorrelationId);

        return Ok(response);
    }

    [HttpPost("jobs/{jobId}/cancel")]
    public async Task<IActionResult> CancelJob(string jobId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cancel requested. JobId={JobId}", jobId);

        var job = await _jobStore.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return CreateProblemDetails(StatusCodes.Status404NotFound, "Job not found.", HttpContext.TraceIdentifier);
        }

        if (job.Status == ImageProcessingJobStatus.Canceled)
        {
            _logger.LogInformation("Cancel ignored/rejected. JobId={JobId}, Status={Status}", job.Id, job.Status);
            return Ok(new ImageJobActionResponse(job.Id, job.Status.ToString(), "cancel", Accepted: true, "Job is already canceled.", AlreadyInDesiredState: true));
        }

        if (job.Status is ImageProcessingJobStatus.Completed or ImageProcessingJobStatus.Failed)
        {
            _logger.LogInformation("Cancel ignored/rejected. JobId={JobId}, Status={Status}", job.Id, job.Status);
            return CreateProblemDetails(StatusCodes.Status409Conflict, "Job cannot be canceled in its current state.", job.CorrelationId);
        }

        var previousStatus = job.Status;
        job.Status = ImageProcessingJobStatus.Canceled;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.ErrorMessage = "Job canceled by operator.";
        await _jobStore.UpdateAsync(job, cancellationToken);

        _logger.LogInformation("Cancel applied. JobId={JobId}, PreviousStatus={PreviousStatus}", job.Id, previousStatus);
        return Ok(new ImageJobActionResponse(job.Id, job.Status.ToString(), "cancel", Accepted: true, "Job canceled successfully.", AlreadyInDesiredState: false));
    }

    [HttpPost("jobs/{jobId}/retry")]
    public async Task<IActionResult> RetryJob(string jobId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Retry requested. JobId={JobId}", jobId);

        var job = await _jobStore.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return CreateProblemDetails(StatusCodes.Status404NotFound, "Job not found.", HttpContext.TraceIdentifier);
        }

        if (job.Status == ImageProcessingJobStatus.Queued)
        {
            _logger.LogInformation("Retry ignored/rejected. JobId={JobId}, Status={Status}", job.Id, job.Status);
            return Ok(new ImageJobActionResponse(job.Id, job.Status.ToString(), "retry", Accepted: true, "Job is already queued.", AlreadyInDesiredState: true));
        }

        if (job.Status != ImageProcessingJobStatus.Failed)
        {
            _logger.LogInformation("Retry rejected. JobId={JobId}, Status={Status}", job.Id, job.Status);
            return CreateProblemDetails(StatusCodes.Status409Conflict, "Only failed jobs can be retried.", job.CorrelationId);
        }

        job.Status = ImageProcessingJobStatus.Queued;
        job.StartedAt = null;
        job.CompletedAt = null;
        job.Progress = 0;
        job.ErrorMessage = null;
        job.OutputPath = null;
        await _jobStore.UpdateAsync(job, cancellationToken);
        await _jobQueue.QueueAsync(job, cancellationToken);

        _logger.LogInformation("Retry accepted and enqueued. JobId={JobId}", job.Id);
        return Ok(new ImageJobActionResponse(job.Id, job.Status.ToString(), "retry", Accepted: true, "Retry accepted and enqueued.", AlreadyInDesiredState: false));
    }

    [HttpGet("jobs/{jobId}/result")]
    public async Task<IActionResult> DownloadResult(string jobId, CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return CreateProblemDetails(StatusCodes.Status404NotFound, "Job not found.", HttpContext.TraceIdentifier);
        }

        if (job.Status != ImageProcessingJobStatus.Completed || string.IsNullOrWhiteSpace(job.OutputPath) || !System.IO.File.Exists(job.OutputPath))
        {
            return CreateProblemDetails(StatusCodes.Status409Conflict, "Job is not completed yet.", job.CorrelationId);
        }

        var contentType = ResolveContentType(job.ContentType, job.OutputPath);
        var downloadName = ResolveDownloadName(job.OriginalFileName, job.OutputPath);

        var stream = System.IO.File.OpenRead(job.OutputPath);
        RegisterCleanupOnCompleted(job.WorkingDirectory);
        return File(stream, contentType, downloadName);
    }

    private static string ResolveContentType(string? contentType, string? outputPath)
    {
        var extension = Path.GetExtension(outputPath ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => !string.IsNullOrWhiteSpace(contentType) ? contentType : "application/octet-stream"
        };
    }

    private static string ResolveDownloadName(string? outputFileName, string outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputFileName))
        {
            return outputFileName;
        }

        var extension = Path.GetExtension(outputPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        return $"processed-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{extension}";
    }

    private IActionResult CreateProblemDetails(int statusCode, string detail, string? correlationId)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode switch
            {
                StatusCodes.Status400BadRequest => "Bad request",
                StatusCodes.Status404NotFound => "Not found",
                StatusCodes.Status409Conflict => "Conflict",
                _ => "Request failed"
            },
            Detail = detail,
            Instance = HttpContext.Request.Path
        };

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            problem.Extensions["correlationId"] = correlationId;
        }

        return StatusCode(statusCode, problem);
    }

    private void RegisterCleanupOnCompleted(string? workingDirectory)
    {
        HttpContext.Response.OnCompleted(() =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
                {
                    Directory.Delete(workingDirectory, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }

            return Task.CompletedTask;
        });
    }
}
