namespace MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;

public sealed record ProcessImageWithEngineCommand(
    string OriginalFileName,
    string? ContentType,
    Stream FileStream,
    long FileSizeBytes,
    string? Engine,
    string? OptionsJson,
    string CorrelationId,
    bool PreserveOutput = false
);
