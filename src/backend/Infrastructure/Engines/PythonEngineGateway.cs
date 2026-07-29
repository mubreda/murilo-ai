using MuriloAI.Backend.Domain.Contracts;
using MuriloAI.Backend.Services;

namespace MuriloAI.Backend.Infrastructure.Engines;

public sealed class PythonEngineGateway : IEngineGateway
{
    private readonly PythonEngineRunner _pythonEngineRunner;
    private readonly ILogger<PythonEngineGateway> _logger;

    public PythonEngineGateway(PythonEngineRunner pythonEngineRunner, ILogger<PythonEngineGateway> logger)
    {
        _pythonEngineRunner = pythonEngineRunner;
        _logger = logger;
    }

    public async Task<EngineProcessResult> ProcessAsync(EngineProcessRequest request, CancellationToken cancellationToken = default)
    {
        var runnerResult = await _pythonEngineRunner.RunAsync(
            request.Engine,
            request.InputPath,
            request.OutputDirectory,
            request.OptionsJson,
            cancellationToken);

        if (!runnerResult.Success)
        {
            return new EngineProcessResult(
                Success: false,
                Engine: request.Engine,
                InputPath: request.InputPath,
                OutputPath: null,
                Duration: runnerResult.Duration,
                ExitCode: runnerResult.ExitCode ?? -1,
                Message: runnerResult.Message ?? "Engine execution failed.");
        }

        var outputPath = ResolveOutputPath(request.OutputDirectory, runnerResult.OutputPath);
        if (outputPath is null)
        {
            _logger.LogError(
                "Engine reported success but output file was not found. Engine={Engine}, OutputDir={OutputDirectory}",
                request.Engine,
                request.OutputDirectory);

            return new EngineProcessResult(
                Success: false,
                Engine: request.Engine,
                InputPath: request.InputPath,
                OutputPath: null,
                Duration: runnerResult.Duration,
                ExitCode: runnerResult.ExitCode ?? -1,
                Message: "Engine did not produce an output file.");
        }

        return new EngineProcessResult(
            Success: true,
            Engine: request.Engine,
            InputPath: request.InputPath,
            OutputPath: outputPath,
            Duration: runnerResult.Duration,
            ExitCode: runnerResult.ExitCode ?? 0,
            Message: runnerResult.Message ?? "Engine execution completed.");
    }

    private static string? ResolveOutputPath(string outputDirectory, string? runnerOutputPath)
    {
        if (!string.IsNullOrWhiteSpace(runnerOutputPath) && File.Exists(runnerOutputPath))
        {
            return runnerOutputPath;
        }

        if (!Directory.Exists(outputDirectory))
        {
            return null;
        }

        return Directory.GetFiles(outputDirectory)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
