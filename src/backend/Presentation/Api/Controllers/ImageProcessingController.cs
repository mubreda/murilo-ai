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

        var result = await _useCase.ExecuteAsync(
            new ProcessImageWithEngineCommand(
                request.File,
                request.Engine,
                request.Options,
                correlationId),
            cancellationToken);

        if (result.Success && result.FileBytes is not null && result.ContentType is not null && result.OutputFileName is not null)
        {
            return File(result.FileBytes, result.ContentType, result.OutputFileName);
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
}
