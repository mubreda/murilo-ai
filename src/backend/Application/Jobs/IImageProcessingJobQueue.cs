using MuriloAI.Backend.Domain.Models;

namespace MuriloAI.Backend.Application.Jobs;

public interface IImageProcessingJobQueue
{
    ValueTask QueueAsync(ImageProcessingJob job, CancellationToken cancellationToken = default);
    ValueTask<ImageProcessingJob?> DequeueAsync(CancellationToken cancellationToken = default);
}
