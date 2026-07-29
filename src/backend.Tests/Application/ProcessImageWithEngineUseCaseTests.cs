using Microsoft.AspNetCore.Http;
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
            CreateFormFile("test.png", [1, 2, 3, 4]),
            "realesrgan",
            "{\"tile\":128}",
            Guid.NewGuid().ToString("N"));

        var result = await useCase.ExecuteAsync(command, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.NotNull(result.FileBytes);
        Assert.Equal("image/png", result.ContentType);

        gateway.Verify(x => x.ProcessAsync(It.IsAny<EngineProcessRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        Directory.Delete(outputDir, true);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsBadRequest_WhenOptionsJsonIsInvalid()
    {
        var gateway = new Mock<IEngineGateway>();
        var useCase = new ProcessImageWithEngineUseCase(gateway.Object, NullLogger<ProcessImageWithEngineUseCase>.Instance);

        var command = new ProcessImageWithEngineCommand(
            CreateFormFile("test.png", [1, 2, 3, 4]),
            "realesrgan",
            "not-json",
            Guid.NewGuid().ToString("N"));

        var result = await useCase.ExecuteAsync(command, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        gateway.Verify(x => x.ProcessAsync(It.IsAny<EngineProcessRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static IFormFile CreateFormFile(string fileName, byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
    }
}
