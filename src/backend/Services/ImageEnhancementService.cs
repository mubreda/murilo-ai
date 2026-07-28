using MuriloAI.Backend.Engines;
using System.Threading;
using System.Threading.Tasks;

namespace MuriloAI.Backend.Services;

public class ImageEnhancementService : IImageEnhancementService
{
    public async Task<bool> EnhanceImageAsync(IImageEngine engine, CancellationToken cancellationToken = default)
    {
        return await engine.EnhanceAsync(cancellationToken);
    }
}
