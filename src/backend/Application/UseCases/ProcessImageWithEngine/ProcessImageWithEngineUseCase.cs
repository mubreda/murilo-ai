using System.Diagnostics;
using System.Text.Json;
using MuriloAI.Backend.Domain.Contracts;

namespace MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;

public sealed class ProcessImageWithEngineUseCase : IProcessImageWithEngineUseCase
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp"
    };

    private const int StatusBadRequest = 400;
    private const int StatusOk = 200;
    private const int StatusUnprocessableEntity = 422;
    private const int StatusInternalServerError = 500;
    private const long MaxFileSizeBytes = 20 * 1024 * 1024;

    private readonly IEngineGateway _engineGateway;
    private readonly ILogger<ProcessImageWithEngineUseCase> _logger;

    public ProcessImageWithEngineUseCase(IEngineGateway engineGateway, ILogger<ProcessImageWithEngineUseCase> logger)
    {
        _engineGateway = engineGateway;
        _logger = logger;
    }

    public async Task<ProcessImageWithEngineResult> ExecuteAsync(ProcessImageWithEngineCommand command, CancellationToken cancellationToken = default)
    {
        var validation = ValidateCommand(command);
        if (validation is not null)
        {
            return validation;
        }

        var extension = Path.GetExtension(command.OriginalFileName);
        var safeCorrelationId = BuildSafeCorrelationId(command.CorrelationId);
        var requestDirectory = $"{safeCorrelationId}-{Guid.NewGuid():N}";
        var tempRoot = Path.Combine(Path.GetTempPath(), "murilo-ai", requestDirectory);
        var inputDirectory = Path.Combine(tempRoot, "input");
        var outputDirectory = Path.Combine(tempRoot, "output");
        Directory.CreateDirectory(inputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var inputPath = Path.Combine(inputDirectory, $"source{extension}");

        try
        {
            await using (var stream = File.Create(inputPath))
            {
                if (command.FileStream.CanSeek)
                {
                    command.FileStream.Seek(0, SeekOrigin.Begin);
                }

                await command.FileStream.CopyToAsync(stream, cancellationToken);
            }

            var stopwatch = Stopwatch.StartNew();
            var engineResult = await _engineGateway.ProcessAsync(
                new EngineProcessRequest(command.Engine!, inputPath, outputDirectory, command.OptionsJson),
                cancellationToken);
            stopwatch.Stop();

            _logger.LogInformation(
                "Engine processing finished. CorrelationId={CorrelationId}, Engine={Engine}, DurationMs={DurationMs}, ExitCode={ExitCode}",
                command.CorrelationId,
                command.Engine,
                stopwatch.ElapsedMilliseconds,
                engineResult.ExitCode);

            if (!engineResult.Success)
            {
                return new ProcessImageWithEngineResult(
                    Success: false,
                    StatusCode: StatusUnprocessableEntity,
                    CorrelationId: command.CorrelationId,
                    Message: string.IsNullOrWhiteSpace(engineResult.Message) ? "Engine processing failed." : engineResult.Message,
                    OutputPath: null,
                    OutputFileName: null,
                    ContentType: null);
            }

            if (string.IsNullOrWhiteSpace(engineResult.OutputPath) || !File.Exists(engineResult.OutputPath))
            {
                return new ProcessImageWithEngineResult(
                    Success: false,
                    StatusCode: StatusInternalServerError,
                    CorrelationId: command.CorrelationId,
                    Message: "Engine did not produce an output file.",
                    OutputPath: null,
                    OutputFileName: null,
                    ContentType: null);
            }

            var outputExtension = Path.GetExtension(engineResult.OutputPath);
            if (string.IsNullOrWhiteSpace(outputExtension))
            {
                outputExtension = extension;
            }

            var responseOutput = CopyToResponseOutput(
                engineResult.OutputPath,
                command.OriginalFileName,
                outputExtension,
                safeCorrelationId);

            var contentType = ResolveContentType(outputExtension);

            return new ProcessImageWithEngineResult(
                Success: true,
                StatusCode: StatusOk,
                CorrelationId: command.CorrelationId,
                Message: "Image processed successfully.",
                OutputPath: responseOutput.OutputPath,
                OutputFileName: responseOutput.OutputFileName,
                ContentType: contentType);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Engine processing canceled. CorrelationId={CorrelationId}, Engine={Engine}",
                command.CorrelationId,
                command.Engine);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled processing error. CorrelationId={CorrelationId}, Engine={Engine}",
                command.CorrelationId,
                command.Engine);

            return new ProcessImageWithEngineResult(
                Success: false,
                StatusCode: StatusInternalServerError,
                CorrelationId: command.CorrelationId,
                Message: "Unexpected error while processing image.",
                OutputPath: null,
                OutputFileName: null,
                ContentType: null);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static ProcessImageWithEngineResult? ValidateCommand(ProcessImageWithEngineCommand command)
    {
        if (command.FileStream is null || !command.FileStream.CanRead || command.FileSizeBytes <= 0)
        {
            return ValidationFailure(command.CorrelationId, "A non-empty image file is required.");
        }

        if (command.FileSizeBytes > MaxFileSizeBytes)
        {
            return ValidationFailure(command.CorrelationId, $"File exceeds the maximum size of {MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        var extension = Path.GetExtension(command.OriginalFileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            return ValidationFailure(command.CorrelationId, "Unsupported file extension.");
        }

        if (string.IsNullOrWhiteSpace(command.Engine))
        {
            return ValidationFailure(command.CorrelationId, "Engine is required.");
        }

        if (!string.IsNullOrWhiteSpace(command.OptionsJson))
        {
            try
            {
                using var document = JsonDocument.Parse(command.OptionsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return ValidationFailure(command.CorrelationId, "Options must be a valid JSON object.");
                }
            }
            catch (JsonException)
            {
                return ValidationFailure(command.CorrelationId, "Options must be valid JSON.");
            }
        }

        return null;
    }

    private static ProcessImageWithEngineResult ValidationFailure(string correlationId, string message)
    {
        return new ProcessImageWithEngineResult(
            Success: false,
            StatusCode: StatusBadRequest,
            CorrelationId: correlationId,
            Message: message,
            OutputPath: null,
            OutputFileName: null,
            ContentType: null);
    }

    private static string ResolveContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup: temporary files must not crash the request flow.
        }
    }

    private static string BuildSafeCorrelationId(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return Guid.NewGuid().ToString("N");
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedChars = correlationId
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray();
        var sanitized = new string(sanitizedChars).Trim();

        if (string.IsNullOrWhiteSpace(sanitized) || sanitized.All(ch => ch == '_'))
        {
            return Guid.NewGuid().ToString("N");
        }

        return sanitized;
    }

    private static (string OutputPath, string OutputFileName) CopyToResponseOutput(
        string sourceOutputPath,
        string originalFileName,
        string outputExtension,
        string safeCorrelationId)
    {
        var responseRoot = Path.Combine(Path.GetTempPath(), "murilo-ai-response", $"{safeCorrelationId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(responseRoot);

        var outputFileName = $"{Path.GetFileNameWithoutExtension(originalFileName)}-processed{outputExtension}";
        var responseOutputPath = Path.Combine(responseRoot, outputFileName);
        File.Copy(sourceOutputPath, responseOutputPath, overwrite: true);

        return (responseOutputPath, outputFileName);
    }
}
