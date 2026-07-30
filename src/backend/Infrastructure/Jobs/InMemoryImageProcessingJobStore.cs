using System.Collections.Concurrent;
using MuriloAI.Backend.Application.Jobs;
using MuriloAI.Backend.Domain.Models;

namespace MuriloAI.Backend.Infrastructure.Jobs;

public sealed class InMemoryImageProcessingJobStore : IImageProcessingJobStore
{
    private readonly ConcurrentDictionary<string, ImageProcessingJob> _jobs = new(StringComparer.OrdinalIgnoreCase);

    public Task<ImageProcessingJob> CreateAsync(ImageProcessingJob job, CancellationToken cancellationToken = default)
    {
        _jobs[job.Id] = job;
        return Task.FromResult(job);
    }

    public Task<ImageProcessingJob?> GetByIdAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    public Task UpdateAsync(ImageProcessingJob job, CancellationToken cancellationToken = default)
    {
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<ImageProcessingJob>> ListNonFinalAsync(CancellationToken cancellationToken = default)
    {
        var jobs = _jobs.Values
            .Where(job => job.Status is ImageProcessingJobStatus.Queued or ImageProcessingJobStatus.Processing)
            .OrderBy(job => job.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyCollection<ImageProcessingJob>>(jobs);
    }
}
