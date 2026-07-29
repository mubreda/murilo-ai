namespace MuriloAI.Backend.Domain.Contracts;

public sealed record EngineProcessRequest(
    string Engine,
    string InputPath,
    string OutputDirectory,
    string? OptionsJson
);
