using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;
using MuriloAI.Backend.Domain.Contracts;
using Xunit;

namespace MuriloAI.Backend.Tests.Application;

public class ProcessImageWithEngineUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsSuccess_WhenGatewayProcessesImage()
    {
        var gateway = new Mock<IEngineGateway>();
        var useCase = new ProcessImageWithEngineUseCase(gateway.Object, NullLogger<ProcessImageWithEngineUseCase>.Instance);

        var outputDir = Path.Combine(Path.GetTempPath(), "murilo-ai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
        var outputFile = Path.Combine(outputDir, "result.png");
        await File.WriteAllBytesAsync(outputFile, [137, 80, 78, 71]);

        gateway
            .Setup(x => x.ProcessAsync(It.IsAny<EngineProcessRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EngineProcessRequest req, CancellationToken _) =>
                new EngineProcessResult(true, req.Engine, req.InputPath, outputFile, 0.12, 0, "ok"));

        var command = new ProcessImageWithEngineCommand(
            "test.png",
            "image/png",
            CreateInputStream([1, 2, 3, 4]),
            4,
            "realesrgan",
            "{\"tile\":128}",
            Guid.NewGuid().ToString("N"));

        var result = await useCase.ExecuteAsync(command, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal("image/png", result.ContentType);

        gateway.Verify(x => x.ProcessAsync(It.IsAny<EngineProcessRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        if (result.OutputPath is not null)
        {
            File.Delete(result.OutputPath);
            var parent = Path.GetDirectoryName(result.OutputPath);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                Directory.Delete(parent, true);
            }
        }

        Directory.Delete(outputDir, true);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsBadRequest_WhenOptionsJsonIsInvalid()
    {
        var gateway = new Mock<IEngineGateway>();
        var useCase = new ProcessImageWithEngineUseCase(gateway.Object, NullLogger<ProcessImageWithEngineUseCase>.Instance);

        var command = new ProcessImageWithEngineCommand(
            "test.png",
            "image/png",
            CreateInputStream([1, 2, 3, 4]),
            4,
            "realesrgan",
            "not-json",
            Guid.NewGuid().ToString("N"));

        var result = await useCase.ExecuteAsync(command, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        gateway.Verify(x => x.ProcessAsync(It.IsAny<EngineProcessRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotThrowIOException_WhenCorrelationIdHasInvalidPathChars()
    {
        var gateway = new Mock<IEngineGateway>();
        var useCase = new ProcessImageWithEngineUseCase(gateway.Object, NullLogger<ProcessImageWithEngineUseCase>.Instance);

        var outputDir = Path.Combine(Path.GetTempPath(), "murilo-ai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
        var outputFile = Path.Combine(outputDir, "result.png");
        await File.WriteAllBytesAsync(outputFile, [137, 80, 78, 71]);

        EngineProcessRequest? capturedRequest = null;
        gateway
            .Setup(x => x.ProcessAsync(It.IsAny<EngineProcessRequest>(), It.IsAny<CancellationToken>()))
            .Callback<EngineProcessRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync((EngineProcessRequest req, CancellationToken _) =>
                new EngineProcessResult(true, req.Engine, req.InputPath, outputFile, 0.10, 0, "ok"));

        var command = new ProcessImageWithEngineCommand(
            "test.png",
            "image/png",
            CreateInputStream([1, 2, 3, 4]),
            4,
            "realesrgan",
            "{\"tile\":128}",
            "0HNNDMHRGPA3F:00000001\\bad?*");

        var result = await useCase.ExecuteAsync(command, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(capturedRequest);

        var requestDirectory = Directory.GetParent(Directory.GetParent(capturedRequest!.InputPath)!.FullName)!.Name;
        Assert.DoesNotContain(':', requestDirectory);
        Assert.DoesNotContain('\\', requestDirectory);
        Assert.DoesNotContain('/', requestDirectory);
        Assert.DoesNotContain('?', requestDirectory);
        Assert.DoesNotContain('*', requestDirectory);

        if (result.OutputPath is not null)
        {
            File.Delete(result.OutputPath);
            var parent = Path.GetDirectoryName(result.OutputPath);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                Directory.Delete(parent, true);
            }
        }
    }

    private static Stream CreateInputStream(byte[] bytes)
    {
        return new MemoryStream(bytes);
    }
}
