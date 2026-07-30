namespace MuriloAI.Backend.Presentation.Api.Responses;

public sealed record ImageJobActionResponse(
    string JobId,
    string Status,
    string Action,
    bool Accepted,
    string Message,
    bool AlreadyInDesiredState);
