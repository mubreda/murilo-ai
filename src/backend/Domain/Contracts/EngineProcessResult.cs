namespace MuriloAI.Backend.Domain.Contracts;

public sealed record EngineProcessResult(
    bool Success,
    string Engine,
    string InputPath,
    string? OutputPath,
    double Duration,
    int ExitCode,
    string Message
);
