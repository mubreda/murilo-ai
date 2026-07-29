using Microsoft.AspNetCore.Mvc;

namespace MuriloAI.Backend.Presentation.Api.Requests;

public sealed class ProcessImageRequest
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; set; }

    [FromForm(Name = "engine")]
    public string? Engine { get; set; }

    [FromForm(Name = "options")]
    public string? Options { get; set; }
}
