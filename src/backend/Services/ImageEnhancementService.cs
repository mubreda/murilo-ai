using MuriloAI.Backend.Engines;
using MuriloAI.Backend.Models;
using System.Threading;
using System.Threading.Tasks;

namespace MuriloAI.Backend.Services;

public class ImageEnhancementService : IImageEnhancementService
{
    private readonly PythonEngineRunner _pythonEngineRunner;

    public ImageEnhancementService(PythonEngineRunner pythonEngineRunner)
    {
        _pythonEngineRunner = pythonEngineRunner;
    }

    public async Task<bool> EnhanceImageAsync(IImageEngine engine, CancellationToken cancellationToken = default)
    {
        var inputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ai", "input", "teste.jpg");
        var outputPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ai", "output");

        var result = await _pythonEngineRunner.RunAsync("realesrgan", inputPath, outputPath, "{\"tile\":128}", cancellationToken);

        return result.Success;
    }
}
