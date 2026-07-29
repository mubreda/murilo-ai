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

        if (result.Success &&
            result.OutputPath is not null &&
            result.ContentType is not null &&
            result.OutputFileName is not null)
        {
            RegisterCleanupOnCompleted(result.OutputPath);
            return PhysicalFile(result.OutputPath, result.ContentType, result.OutputFileName);
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
