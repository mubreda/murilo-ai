using MuriloAI.Backend.Engines;

namespace MuriloAI.Backend.Services;

public interface IImageEnhancementService
{
    Task<bool> EnhanceImageAsync(IImageEngine engine, CancellationToken cancellationToken = default);
}
