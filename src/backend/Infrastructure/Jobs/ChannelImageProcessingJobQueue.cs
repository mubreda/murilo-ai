using System.Threading.Channels;
using MuriloAI.Backend.Application.Jobs;
using MuriloAI.Backend.Domain.Models;

namespace MuriloAI.Backend.Infrastructure.Jobs;

public sealed class ChannelImageProcessingJobQueue : IImageProcessingJobQueue
{
    private readonly Channel<ImageProcessingJob> _channel = Channel.CreateUnbounded<ImageProcessingJob>();

    public ValueTask QueueAsync(ImageProcessingJob job, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(job, cancellationToken);
    }

    public async ValueTask<ImageProcessingJob?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        if (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            return await _channel.Reader.ReadAsync(cancellationToken);
        }

        return null;
    }
}
