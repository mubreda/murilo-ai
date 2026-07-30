namespace MuriloAI.Backend.Infrastructure.Jobs;

public sealed class ImageJobsStoreOptions
{
    public const string SectionName = "ImageJobs";

    public string StoreProvider { get; set; } = "InMemory";

    public string SqliteConnectionString { get; set; } = "Data Source=jobs.db";
}
