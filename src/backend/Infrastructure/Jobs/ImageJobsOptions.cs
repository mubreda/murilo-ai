namespace MuriloAI.Backend.Infrastructure.Jobs;

public sealed class ImageJobsOptions
{
    public const string SectionName = "ImageJobs";

    public int AttemptTimeoutSeconds { get; set; } = 120;

    public int MaxAttempts { get; set; } = 3;

    public int RecoveryWindowHours { get; set; } = 24;
}
