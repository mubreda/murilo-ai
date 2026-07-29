using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MuriloAI.Backend.Domain.Contracts;
using Xunit;

namespace MuriloAI.Backend.Tests.Integration;

public class ImageProcessingEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ImageProcessingEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Process_ReturnsProcessedFile_WhenRequestIsValid()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IEngineGateway, FakeEngineGateway>();
            });
        });

        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();

        var fileBytes = new byte[] { 137, 80, 78, 71 };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        content.Add(fileContent, "file", "photo.png");
        content.Add(new StringContent("realesrgan"), "engine");
        content.Add(new StringContent("{\"tile\":128}"), "options");

        using var response = await client.PostAsync("/api/image/process", content);
        var responseBytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(responseBytes);
    }

    [Fact]
    public async Task Process_ReturnsProblemDetails_WhenOptionsJsonIsInvalid()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IEngineGateway, FakeEngineGateway>();
            });
        });

        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();

        var fileBytes = new byte[] { 137, 80, 78, 71 };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        content.Add(fileContent, "file", "valid.png");
        content.Add(new StringContent("realesrgan"), "engine");
        content.Add(new StringContent("not-json"), "options");

        using var response = await client.PostAsync("/api/image/process", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("correlationId", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Options must be valid JSON", body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeEngineGateway : IEngineGateway
    {
        public async Task<EngineProcessResult> ProcessAsync(EngineProcessRequest request, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(request.OutputDirectory);
            var outputPath = Path.Combine(request.OutputDirectory, "output.png");
            await File.WriteAllBytesAsync(outputPath, [137, 80, 78, 71], cancellationToken);

            return new EngineProcessResult(
                Success: true,
                Engine: request.Engine,
                InputPath: request.InputPath,
                OutputPath: outputPath,
                Duration: 0.15,
                ExitCode: 0,
                Message: "ok");
        }
    }
}
