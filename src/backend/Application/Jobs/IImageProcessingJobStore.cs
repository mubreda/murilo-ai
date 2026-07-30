using MuriloAI.Backend.Domain.Models;

namespace MuriloAI.Backend.Application.Jobs;

public interface IImageProcessingJobStore
{
    Task<ImageProcessingJob> CreateAsync(ImageProcessingJob job, CancellationToken cancellationToken = default);
    Task<ImageProcessingJob?> GetByIdAsync(string jobId, CancellationToken cancellationToken = default);
    Task UpdateAsync(ImageProcessingJob job, CancellationToken cancellationToken = default);
}
