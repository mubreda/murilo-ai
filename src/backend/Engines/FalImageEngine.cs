using System.Net.Http.Headers;
using System.Text.Json;
using MuriloAI.Backend.Engines;

namespace MuriloAI.Backend.Engines;

public class FalImageEngine : IImageEngine
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FalImageEngine> _logger;

    public FalImageEngine(HttpClient httpClient, IConfiguration configuration, ILogger<FalImageEngine> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> EnhanceAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["FAL_KEY"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError("FAL_KEY não foi configurada.");
            throw new InvalidOperationException("Chave de API Fal.ai não configurada.");
        }

        var requestUrl = "https://api.fal.ai/v1/inference/codeformer";
        var payload = new
        {
            image_url = "https://images.unsplash.com/photo-1517263904808-5dc0f6d2f4b1?auto=format&fit=crop&w=800&q=80"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(payload));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        _logger.LogInformation("Enviando requisição para Fal.ai na URL {RequestUrl}", requestUrl);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Falha no Fal.ai: {StatusCode} - {ResponseBody}", response.StatusCode, responseBody);
                return false;
            }

            _logger.LogInformation("Fal.ai retornou sucesso: {ResponseBody}", responseBody);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Erro de rede ao chamar Fal.ai.");
            return false;
        }
        catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Requisição Fal.ai cancelada pelo token.");
            return false;
        }
    }
}
