namespace MuriloAI.Backend.Presentation.Api.Responses;

public sealed record ImageJobStatusResponse(
    string JobId,
    string Status,
    int Progress,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    string? ResultUrl,
    string? CorrelationId);
