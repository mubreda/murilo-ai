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

    public ImageJobsController(IImageProcessingJobStore jobStore, IImageProcessingJobQueue jobQueue)
    {
        _jobStore = jobStore;
        _jobQueue = jobQueue;
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
        RegisterCleanupOnCompleted(job.OutputPath);
        return File(stream, contentType, downloadName);
    }

    private static string ResolveContentType(string? contentType, string outputPath)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            return contentType;
        }

        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
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

    private void RegisterCleanupOnCompleted(string outputPath)
    {
        HttpContext.Response.OnCompleted(() =>
        {
            try
            {
                if (System.IO.File.Exists(outputPath))
                {
                    System.IO.File.Delete(outputPath);
                }

                var parentDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(parentDirectory) && Directory.Exists(parentDirectory))
                {
                    Directory.Delete(parentDirectory, recursive: true);
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
