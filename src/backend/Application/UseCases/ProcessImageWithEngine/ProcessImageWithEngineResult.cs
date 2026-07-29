namespace MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;

public sealed record ProcessImageWithEngineResult(
    bool Success,
    int StatusCode,
    string CorrelationId,
    string Message,
    string? OutputPath,
    string? OutputFileName,
    string? ContentType
);
