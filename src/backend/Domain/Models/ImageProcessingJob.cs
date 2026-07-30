namespace MuriloAI.Backend.Domain.Models;

public sealed class ImageProcessingJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public ImageProcessingJobStatus Status { get; set; } = ImageProcessingJobStatus.Queued;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Engine { get; set; }

    public string? OriginalFileName { get; set; }

    public string? ContentType { get; set; }

    public string? OptionsJson { get; set; }

    public string? CorrelationId { get; set; }

    public string? InputPath { get; set; }

    public string? OutputPath { get; set; }

    public string? ErrorMessage { get; set; }

    public int Progress { get; set; }
}

public enum ImageProcessingJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}
