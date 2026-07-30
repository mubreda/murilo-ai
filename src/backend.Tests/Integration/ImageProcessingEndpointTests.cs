using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MuriloAI.Backend.Application.Jobs;
using MuriloAI.Backend.Domain.Contracts;
using MuriloAI.Backend.Domain.Models;
using MuriloAI.Backend.Infrastructure.Jobs;
using Xunit;

namespace MuriloAI.Backend.Tests.Integration;

public class ImageProcessingEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    public ImageProcessingEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Process_ReturnsProcessedFile_WhenRequestIsValid()
    {
        FakeEngineGateway.Reset();

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
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.False(string.IsNullOrWhiteSpace(response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName));
        Assert.NotEmpty(responseBytes);

        Assert.NotNull(FakeEngineGateway.LastOutputPath);
        var deleted = await WaitUntilDeleted(FakeEngineGateway.LastOutputPath!, CleanupTimeout);
        Assert.True(deleted, "Expected response temp output file to be removed after response completion.");
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

    [Fact]
    public async Task CreateJob_ReturnsAccepted_WithJobIdAndQueuedStatus()
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

        content.Add(fileContent, "file", "job.png");
        content.Add(new StringContent("realesrgan"), "engine");
        content.Add(new StringContent("{\"tile\":128}"), "options");

        using var response = await client.PostAsync("/api/image/jobs", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("jobId", out var jobIdElement));
        Assert.False(string.IsNullOrWhiteSpace(jobIdElement.GetString()));
        Assert.True(json.RootElement.TryGetProperty("status", out var statusElement));
        Assert.Equal("Queued", statusElement.GetString());
    }

    [Fact]
    public async Task JobLifecycle_ReturnsCompletedAndResult_WhenProcessingFinishes()
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

        content.Add(fileContent, "file", "lifecycle.png");
        content.Add(new StringContent("realesrgan"), "engine");
        content.Add(new StringContent("{\"tile\":128}"), "options");

        using var createResponse = await client.PostAsync("/api/image/jobs", content);
        var createBody = await createResponse.Content.ReadAsStringAsync();
        using var createJson = JsonDocument.Parse(createBody);
        var jobId = createJson.RootElement.GetProperty("jobId").GetString();

        Assert.NotNull(jobId);

        using var statusResponse = await WaitForStatusAsync(client, jobId!, "Completed", TimeSpan.FromSeconds(10));
        var statusBody = await statusResponse.Content.ReadAsStringAsync();
        using var statusJson = JsonDocument.Parse(statusBody);

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.Equal("Completed", statusJson.RootElement.GetProperty("status").GetString());
        Assert.True(statusJson.RootElement.GetProperty("progress").GetInt32() >= 100);

        using var resultResponse = await client.GetAsync($"/api/image/jobs/{jobId}/result");
        var resultBytes = await resultResponse.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);
        Assert.NotEmpty(resultBytes);
    }

    [Fact]
    public async Task DownloadResult_RemovesWorkingDirectory_AfterCompletedJob()
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

        content.Add(fileContent, "file", "cleanup.png");
        content.Add(new StringContent("realesrgan"), "engine");
        content.Add(new StringContent("{\"tile\":128}"), "options");

        using var createResponse = await client.PostAsync("/api/image/jobs", content);
        var createBody = await createResponse.Content.ReadAsStringAsync();
        using var createJson = JsonDocument.Parse(createBody);
        var jobId = createJson.RootElement.GetProperty("jobId").GetString();

        Assert.NotNull(jobId);

        using var statusResponse = await WaitForStatusAsync(client, jobId!, "Completed", TimeSpan.FromSeconds(10));
        var statusBody = await statusResponse.Content.ReadAsStringAsync();
        using var statusJson = JsonDocument.Parse(statusBody);
        Assert.Equal("Completed", statusJson.RootElement.GetProperty("status").GetString());

        using var resultResponse = await client.GetAsync($"/api/image/jobs/{jobId}/result");
        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IImageProcessingJobStore>();
        var job = await store.GetByIdAsync(jobId!);
        Assert.NotNull(job);
        Assert.False(string.IsNullOrWhiteSpace(job!.WorkingDirectory));

        var deleted = await WaitUntilDeleted(job.WorkingDirectory!, CleanupTimeout);
        Assert.True(deleted, "Expected job working directory to be removed after result download.");
    }

    [Fact]
    public async Task DownloadResult_UsesOutputExtensionForContentType()
    {
        FakeEngineGateway.OutputExtension = ".jpg";

        try
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

            content.Add(fileContent, "file", "content-type.png");
            content.Add(new StringContent("realesrgan"), "engine");
            content.Add(new StringContent("{\"tile\":128}"), "options");

            using var createResponse = await client.PostAsync("/api/image/jobs", content);
            var createBody = await createResponse.Content.ReadAsStringAsync();
            using var createJson = JsonDocument.Parse(createBody);
            var jobId = createJson.RootElement.GetProperty("jobId").GetString();

            Assert.NotNull(jobId);
            using var statusResponse = await WaitForStatusAsync(client, jobId!, "Completed", TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

            using var resultResponse = await client.GetAsync($"/api/image/jobs/{jobId}/result");
            Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);
            Assert.Equal("image/jpeg", resultResponse.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            FakeEngineGateway.OutputExtension = ".png";
        }
    }

    [Fact]
    public async Task SqliteStore_PersistsJobAcrossServiceScopes()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"murilo-ai-tests-{Guid.NewGuid():N}.db");
        File.Delete(tempDbPath);

        WebApplicationFactory<Program>? factory = null;

        try
        {
            factory = CreateFactory(tempDbPath);

            using var scope = factory.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IImageProcessingJobStore>();

            var job = new ImageProcessingJob
            {
                Id = Guid.NewGuid().ToString("N"),
                Status = ImageProcessingJobStatus.Queued,
                Engine = "realesrgan",
                OriginalFileName = "sample.png",
                ContentType = "image/png",
                CorrelationId = "test-correlation",
                Progress = 0
            };

            await store.CreateAsync(job);

            var loaded = await store.GetByIdAsync(job.Id);
            Assert.NotNull(loaded);
            Assert.Equal(ImageProcessingJobStatus.Queued, loaded!.Status);

            loaded.Status = ImageProcessingJobStatus.Processing;
            loaded.Progress = 55;
            await store.UpdateAsync(loaded);

            using var secondScope = factory.Services.CreateScope();
            var storeFromSecondScope = secondScope.ServiceProvider.GetRequiredService<IImageProcessingJobStore>();
            var reloaded = await storeFromSecondScope.GetByIdAsync(job.Id);

            Assert.NotNull(reloaded);
            Assert.Equal(ImageProcessingJobStatus.Processing, reloaded!.Status);
            Assert.Equal(55, reloaded.Progress);
        }
        finally
        {
            factory?.Dispose();
            File.Delete(tempDbPath);
        }
    }

    [Fact]
    public async Task StartupBootstrap_ReEnqueuesPersistedQueuedJob()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"murilo-ai-tests-{Guid.NewGuid():N}.db");
        File.Delete(tempDbPath);

        WebApplicationFactory<Program>? initialFactory = null;
        WebApplicationFactory<Program>? restartedFactory = null;

        try
        {
            FakeEngineGateway.Reset();
            initialFactory = CreateFactory(tempDbPath);
            using var initialScope = initialFactory.Services.CreateScope();
            var initialStore = initialScope.ServiceProvider.GetRequiredService<IImageProcessingJobStore>();

            var inputPath = await CreateInputFileAsync("queued-bootstrap.png");
            var workingDirectory = Path.Combine(Path.GetTempPath(), "murilo-ai-jobs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);

            var job = new ImageProcessingJob
            {
                Id = $"queued-bootstrap-{Guid.NewGuid():N}",
                Status = ImageProcessingJobStatus.Queued,
                Engine = "realesrgan",
                OriginalFileName = "queued-bootstrap.png",
                ContentType = "image/png",
                CorrelationId = "queued-bootstrap",
                InputPath = inputPath,
                WorkingDirectory = workingDirectory,
                Progress = 0
            };

            await initialStore.CreateAsync(job);

            restartedFactory = CreateFactory(tempDbPath);
            using var client = restartedFactory.CreateClient();

            using var statusResponse = await WaitForStatusAsync(client, job.Id, "Completed", TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            Assert.True(FakeEngineGateway.ProcessCallCount > 0);
        }
        finally
        {
            initialFactory?.Dispose();
            restartedFactory?.Dispose();
            await DeleteFileIfExistsAsync(tempDbPath);
        }
    }

    [Fact]
    public async Task StartupBootstrap_ReEnqueuesPersistedProcessingJob()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"murilo-ai-tests-{Guid.NewGuid():N}.db");
        File.Delete(tempDbPath);

        WebApplicationFactory<Program>? initialFactory = null;
        WebApplicationFactory<Program>? restartedFactory = null;

        try
        {
            FakeEngineGateway.Reset();
            initialFactory = CreateFactory(tempDbPath);
            using var initialScope = initialFactory.Services.CreateScope();
            var initialStore = initialScope.ServiceProvider.GetRequiredService<IImageProcessingJobStore>();

            var inputPath = await CreateInputFileAsync("processing-bootstrap.png");
            var workingDirectory = Path.Combine(Path.GetTempPath(), "murilo-ai-jobs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);

            var job = new ImageProcessingJob
            {
                Id = $"processing-bootstrap-{Guid.NewGuid():N}",
                Status = ImageProcessingJobStatus.Processing,
                Engine = "realesrgan",
                OriginalFileName = "processing-bootstrap.png",
                ContentType = "image/png",
                CorrelationId = "processing-bootstrap",
                InputPath = inputPath,
                WorkingDirectory = workingDirectory,
                Progress = 10
            };

            await initialStore.CreateAsync(job);

            restartedFactory = CreateFactory(tempDbPath);
            using var client = restartedFactory.CreateClient();

            using var statusResponse = await WaitForStatusAsync(client, job.Id, "Completed", TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            Assert.True(FakeEngineGateway.ProcessCallCount > 0);
        }
        finally
        {
            initialFactory?.Dispose();
            restartedFactory?.Dispose();
            await DeleteFileIfExistsAsync(tempDbPath);
        }
    }

    [Fact]
    public async Task StartupBootstrap_DoesNotReEnqueueCompletedJob()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"murilo-ai-tests-{Guid.NewGuid():N}.db");
        File.Delete(tempDbPath);

        WebApplicationFactory<Program>? initialFactory = null;
        WebApplicationFactory<Program>? restartedFactory = null;

        try
        {
            FakeEngineGateway.Reset();
            initialFactory = CreateFactory(tempDbPath);
            using var initialScope = initialFactory.Services.CreateScope();
            var initialStore = initialScope.ServiceProvider.GetRequiredService<IImageProcessingJobStore>();

            var inputPath = await CreateInputFileAsync("completed-bootstrap.png");
            var workingDirectory = Path.Combine(Path.GetTempPath(), "murilo-ai-jobs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);

            var job = new ImageProcessingJob
            {
                Id = $"completed-bootstrap-{Guid.NewGuid():N}",
                Status = ImageProcessingJobStatus.Completed,
                Engine = "realesrgan",
                OriginalFileName = "completed-bootstrap.png",
                ContentType = "image/png",
                CorrelationId = "completed-bootstrap",
                InputPath = inputPath,
                WorkingDirectory = workingDirectory,
                OutputPath = Path.Combine(workingDirectory, "output.png"),
                Progress = 100,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await initialStore.CreateAsync(job);

            restartedFactory = CreateFactory(tempDbPath);
            await Task.Delay(1000);

            using var restartedScope = restartedFactory.Services.CreateScope();
            var restartedStore = restartedScope.ServiceProvider.GetRequiredService<IImageProcessingJobStore>();
            var reloaded = await restartedStore.GetByIdAsync(job.Id);

            Assert.NotNull(reloaded);
            Assert.Equal(ImageProcessingJobStatus.Completed, reloaded!.Status);
            Assert.Equal(0, FakeEngineGateway.ProcessCallCount);
        }
        finally
        {
            initialFactory?.Dispose();
            restartedFactory?.Dispose();
            await DeleteFileIfExistsAsync(tempDbPath);
        }
    }

    [Fact]
    public async Task StartupBootstrap_ReEnqueuesQueuedJobWithinRecoveryWindow()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"murilo-ai-tests-{Guid.NewGuid():N}.db");
        File.Delete(tempDbPath);

        WebApplicationFactory<Program>? initialFactory = null;
        WebApplicationFactory<Program>? restartedFactory = null;

        try
        {
            FakeEngineGateway.Reset();
            initialFactory = CreateFactory(tempDbPath, recoveryWindowHours: 24);
            using var initialScope = initialFactory.Services.CreateScope();
            var initialStore = initialScope.ServiceProvider.GetRequiredService<IImageProcessingJobStore>();

            var inputPath = await CreateInputFileAsync("queued-within-window.png");
            var workingDirectory = Path.Combine(Path.GetTempPath(), "murilo-ai-jobs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);

            var job = new ImageProcessingJob
            {
                Id = $"queued-within-window-{Guid.NewGuid():N}",
                Status = ImageProcessingJobStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
                Engine = "realesrgan",
                OriginalFileName = "queued-within-window.png",
                ContentType = "image/png",
                CorrelationId = "queued-within-window",
                InputPath = inputPath,
                WorkingDirectory = workingDirectory,
                Progress = 0
            };

            await initialStore.CreateAsync(job);

            restartedFactory = CreateFactory(tempDbPath, recoveryWindowHours: 24);
            using var client = restartedFactory.CreateClient();

            using var statusResponse = await WaitForStatusAsync(client, job.Id, "Completed", TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            Assert.True(FakeEngineGateway.ProcessCallCount > 0);
        }
        finally
        {
            initialFactory?.Dispose();
            restartedFactory?.Dispose();
            await DeleteFileIfExistsAsync(tempDbPath);
        }
    }

    [Fact]
    public async Task StartupBootstrap_ReEnqueuesProcessingJobWithinRecoveryWindow()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"murilo-ai-tests-{Guid.NewGuid():N}.db");
        File.Delete(tempDbPath);

        WebApplicationFactory<Program>? initialFactory = null;
        WebApplicationFactory<Program>? restartedFactory = null;

        try
        {
            FakeEngineGateway.Reset();
            initialFactory = CreateFactory(tempDbPath, recoveryWindowHours: 24);
            using var initialScope = initialFactory.Services.CreateScope();
            var initialStore = initialScope.ServiceProvider.GetRequiredService<IImageProcessingJobStore>();

            var inputPath = await CreateInputFileAsync("processing-within-window.png");
            var workingDirectory = Path.Combine(Path.GetTempPath(), "murilo-ai-jobs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);

            var job = new ImageProcessingJob
            {
                Id = $"processing-within-window-{Guid.NewGuid():N}",
                Status = ImageProcessingJobStatus.Processing,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-3),
                Engine = "realesrgan",
                OriginalFileName = "processing-within-window.png",
                ContentType = "image/png",
                CorrelationId = "processing-within-window",
                InputPath = inputPath,
                WorkingDirectory = workingDirectory,
                Progress = 10
            };

            await initialStore.CreateAsync(job);

            restartedFactory = CreateFactory(tempDbPath, recoveryWindowHours: 24);
            using var client = restartedFactory.CreateClient();

            using var statusResponse = await WaitForStatusAsync(client, job.Id, "Completed", TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            Assert.True(FakeEngineGateway.ProcessCallCount > 0);
        }
        finally
        {
            initialFactory?.Dispose();
            restartedFactory?.Dispose();
            await DeleteFileIfExistsAsync(tempDbPath);
        }
    }

    [Fact]
    public async Task StartupBootstrap_IgnoresNonFinalJobOutsideRecoveryWindow()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"murilo-ai-tests-{Guid.NewGuid():N}.db");
        File.Delete(tempDbPath);

        WebApplicationFactory<Program>? initialFactory = null;
        WebApplicationFactory<Program>? restartedFactory = null;

        try
        {
            FakeEngineGateway.Reset();
            initialFactory = CreateFactory(tempDbPath, recoveryWindowHours: 24);
            using var initialScope = initialFactory.Services.CreateScope();
            var initialStore = initialScope.ServiceProvider.GetRequiredService<IImageProcessingJobStore>();

            var inputPath = await CreateInputFileAsync("queued-outside-window.png");
            var workingDirectory = Path.Combine(Path.GetTempPath(), "murilo-ai-jobs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);

            var job = new ImageProcessingJob
            {
                Id = $"queued-outside-window-{Guid.NewGuid():N}",
                Status = ImageProcessingJobStatus.Queued,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-48),
                Engine = "realesrgan",
                OriginalFileName = "queued-outside-window.png",
                ContentType = "image/png",
                CorrelationId = "queued-outside-window",
                InputPath = inputPath,
                WorkingDirectory = workingDirectory,
                Progress = 0
            };

            await initialStore.CreateAsync(job);

            restartedFactory = CreateFactory(tempDbPath, recoveryWindowHours: 24);
            await Task.Delay(1000);

            using var restartedScope = restartedFactory.Services.CreateScope();
            var restartedStore = restartedScope.ServiceProvider.GetRequiredService<IImageProcessingJobStore>();
            var reloaded = await restartedStore.GetByIdAsync(job.Id);

            Assert.NotNull(reloaded);
            Assert.Equal(ImageProcessingJobStatus.Queued, reloaded!.Status);
            Assert.Equal(0, FakeEngineGateway.ProcessCallCount);
        }
        finally
        {
            initialFactory?.Dispose();
            restartedFactory?.Dispose();
            await DeleteFileIfExistsAsync(tempDbPath);
        }
    }

    private sealed class FakeEngineGateway : IEngineGateway
    {
        private static readonly object Sync = new();
        public static string? LastOutputPath { get; private set; }
        public static string OutputExtension { get; set; } = ".png";
        public static int ProcessCallCount { get; private set; }

        public static void Reset()
        {
            lock (Sync)
            {
                LastOutputPath = null;
                OutputExtension = ".png";
                ProcessCallCount = 0;
            }
        }

        public async Task<EngineProcessResult> ProcessAsync(EngineProcessRequest request, CancellationToken cancellationToken = default)
        {
            lock (Sync)
            {
                ProcessCallCount++;
            }

            Directory.CreateDirectory(request.OutputDirectory);
            var outputPath = Path.Combine(request.OutputDirectory, $"output{OutputExtension}");
            await File.WriteAllBytesAsync(outputPath, [137, 80, 78, 71], cancellationToken);

            lock (Sync)
            {
                LastOutputPath = outputPath;
            }

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

    private WebApplicationFactory<Program> CreateFactory(string? sqliteConnectionString = null, int? recoveryWindowHours = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ImageJobs:StoreProvider", "Sqlite");
            builder.UseSetting("ImageJobs:RecoveryWindowHours", (recoveryWindowHours ?? 24).ToString());

            if (!string.IsNullOrWhiteSpace(sqliteConnectionString))
            {
                builder.UseSetting("ImageJobs:SqliteConnectionString", $"Data Source={sqliteConnectionString}");
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IEngineGateway, FakeEngineGateway>();
            });
        });
    }

    private static async Task<string> CreateInputFileAsync(string fileName)
    {
        var inputDirectory = Path.Combine(Path.GetTempPath(), "murilo-ai-bootstrap", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(inputDirectory);
        var inputPath = Path.Combine(inputDirectory, fileName);
        await File.WriteAllBytesAsync(inputPath, [137, 80, 78, 71]);
        return inputPath;
    }

    private static async Task<bool> WaitUntilDeleted(string path, TimeSpan timeout)
    {
        var endAt = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= endAt)
        {
            if (!File.Exists(path))
            {
                return true;
            }

            await Task.Delay(50);
        }

        return !File.Exists(path);
    }

    private static async Task DeleteFileIfExistsAsync(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }
            catch (IOException) when (attempt < 9)
            {
                await Task.Delay(250);
            }
            catch (IOException)
            {
                return;
            }
        }
    }

    private static async Task<HttpResponseMessage> WaitForStatusAsync(HttpClient client, string jobId, string expectedStatus, TimeSpan timeout)
    {
        var endAt = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= endAt)
        {
            var response = await client.GetAsync($"/api/image/jobs/{jobId}");
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("status", out var statusElement) &&
                    string.Equals(statusElement.GetString(), expectedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return response;
                }
            }

            await Task.Delay(100);
        }

        return await client.GetAsync($"/api/image/jobs/{jobId}");
    }
}
