using Microsoft.AspNetCore.Http;

namespace MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;

public sealed record ProcessImageWithEngineCommand(
    IFormFile? File,
    string? Engine,
    string? OptionsJson,
    string CorrelationId
);
