namespace MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;

public sealed record ProcessImageWithEngineResult(
    bool Success,
    int StatusCode,
    string CorrelationId,
    string Message,
    string? OutputFileName,
    string? ContentType,
    byte[]? FileBytes
);
