using MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;
using MuriloAI.Backend.Presentation.Api.Requests;
using Microsoft.AspNetCore.Mvc;

namespace MuriloAI.Backend.Presentation.Api.Controllers;

[ApiController]
[Route("api/image")]
public sealed class ImageProcessingController : ControllerBase
{
    private readonly IProcessImageWithEngineUseCase _useCase;

    public ImageProcessingController(IProcessImageWithEngineUseCase useCase)
    {
        _useCase = useCase;
    }

    [HttpPost("process")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Process([FromForm] ProcessImageRequest request, CancellationToken cancellationToken)
    {
        var correlationId = HttpContext.TraceIdentifier;
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        var formFile = request.File;
        await using var inputStream = formFile?.OpenReadStream();

        var result = await _useCase.ExecuteAsync(
            new ProcessImageWithEngineCommand(
                formFile?.FileName ?? string.Empty,
                formFile?.ContentType,
                inputStream ?? Stream.Null,
                formFile?.Length ?? 0,
                request.Engine,
                request.Options,
                correlationId),
            cancellationToken);

        if (result.Success && result.OutputPath is not null)
        {
            if (!System.IO.File.Exists(result.OutputPath))
            {
                var missingOutputProblem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Image processing failed",
                    Detail = "Processed file was not found.",
                    Instance = HttpContext.Request.Path
                };
                missingOutputProblem.Extensions["correlationId"] = result.CorrelationId;
                return StatusCode(StatusCodes.Status500InternalServerError, missingOutputProblem);
            }

            var contentType = ResolveContentType(result.ContentType, result.OutputPath);
            var downloadName = ResolveDownloadName(result.OutputFileName, result.OutputPath);

            RegisterCleanupOnCompleted(result.OutputPath);
            return PhysicalFile(result.OutputPath, contentType, downloadName);
        }

        var problem = new ProblemDetails
        {
            Status = result.StatusCode,
            Title = "Image processing failed",
            Detail = result.Message,
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["correlationId"] = result.CorrelationId;

        return StatusCode(result.StatusCode, problem);
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
