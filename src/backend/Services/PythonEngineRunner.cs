using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MuriloAI.Backend.Models;

namespace MuriloAI.Backend.Services;

public class PythonEngineRunner
{
    private readonly ILogger<PythonEngineRunner> _logger;

    public PythonEngineRunner(ILogger<PythonEngineRunner> logger)
    {
        _logger = logger;
    }

    public async Task<EngineResult> RunAsync(string engineName, string inputPath, string outputPath, string? optionsJson = null, CancellationToken cancellationToken = default)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ai"));
        var pythonExecutable = Path.GetFullPath(Path.Combine(projectRoot, ".venv", "Scripts", "python.exe"));
        var pythonScript = Path.Combine(projectRoot, "run_engine.py");

        if (!File.Exists(pythonExecutable))
        {
            throw new InvalidOperationException($"Virtual environment não encontrada em: {pythonExecutable}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutable,
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        var arguments = new List<string>
        {
            "run_engine.py",
            engineName,
            inputPath,
            outputPath,
        };

        if (!string.IsNullOrWhiteSpace(optionsJson))
        {
            arguments.Add("--options");
            arguments.Add(optionsJson);
        }

        startInfo.ArgumentList.Add(pythonScript);
        startInfo.ArgumentList.Add(engineName);
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add(outputPath);

        if (!string.IsNullOrWhiteSpace(optionsJson))
        {
            startInfo.ArgumentList.Add("--options");
            startInfo.ArgumentList.Add(optionsJson);
        }

        var executedCommand = $"{startInfo.FileName} {string.Join(" ", startInfo.ArgumentList)}";

        _logger.LogInformation(
            "Iniciando processamento Python. Engine={Engine}, Input={InputPath}, Output={OutputPath}",
            engineName,
            inputPath,
            outputPath);
        _logger.LogInformation("Python Executable: {PythonExecutable}", pythonExecutable);
        _logger.LogInformation("Diretório de trabalho: {WorkingDirectory}", startInfo.WorkingDirectory);
        _logger.LogInformation("Comando executado: {Command}", executedCommand);

        using var process = new Process { StartInfo = startInfo };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            lock (stdoutBuilder)
            {
                stdoutBuilder.AppendLine(e.Data);
            }
            _logger.LogInformation("[PYTHON STDOUT] {Line}", e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            lock (stderrBuilder)
            {
                stderrBuilder.AppendLine(e.Data);
            }
            _logger.LogError("[PYTHON STDERR] {Line}", e.Data);
        };

        var start = Stopwatch.StartNew();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);
        process.CancelOutputRead();
        process.CancelErrorRead();
        start.Stop();

        string stdout;
        string stderr;
        lock (stdoutBuilder)
        {
            stdout = stdoutBuilder.ToString();
        }

        lock (stderrBuilder)
        {
            stderr = stderrBuilder.ToString();
        }

        _logger.LogInformation(
            "Processamento Python finalizado. Engine={Engine}, ExitCode={ExitCode}, DurationMs={DurationMs}, Input={InputPath}, Output={OutputPath}",
            engineName,
            process.ExitCode,
            start.ElapsedMilliseconds,
            inputPath,
            outputPath);

        if (process.ExitCode != 0)
        {
            return new EngineResult
            {
                Success = false,
                Engine = engineName,
                InputPath = inputPath,
                OutputPath = outputPath,
                ExitCode = process.ExitCode,
                Message = stderr.Trim()
            };
        }

        try
        {
            var result = JsonSerializer.Deserialize<EngineResult>(stdout, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result is not null)
            {
                result.ExitCode = process.ExitCode;
            }

            return result ?? new EngineResult
            {
                Success = false,
                Engine = engineName,
                InputPath = inputPath,
                OutputPath = outputPath,
                ExitCode = process.ExitCode,
                Message = "Resposta do Python não foi reconhecida."
            };
        }
        catch (JsonException ex)
        {
            return new EngineResult
            {
                Success = false,
                Engine = engineName,
                InputPath = inputPath,
                OutputPath = outputPath,
                ExitCode = process.ExitCode,
                Message = $"Falha ao desserializar resposta do Python: {ex.Message}"
            };
        }
    }
}
