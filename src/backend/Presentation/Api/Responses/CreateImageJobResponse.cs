namespace MuriloAI.Backend.Presentation.Api.Responses;

public sealed record CreateImageJobResponse(
    string JobId,
    string Status,
    DateTimeOffset CreatedAt,
    string? CorrelationId,
    string? StatusUrl,
    string? ResultUrl);
