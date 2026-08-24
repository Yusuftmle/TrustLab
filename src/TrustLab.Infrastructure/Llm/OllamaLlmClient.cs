using OllamaSharp;
using OllamaSharp.Models.Chat;
using TrustLab.Application.Interfaces;

namespace TrustLab.Infrastructure.Llm;

/// <summary>
/// Gerçek LLM istemcisi: Ollama üzerinden yerel model çağrısı yapar (Qwen3.5, Phi-4 vb.)
/// ILlmClient arayüzünü implement eder — MockDeterministicLlmClient ile drop-in değiştirilebilir.
/// </summary>
public sealed class OllamaLlmClient : ILlmClient
{
    private readonly OllamaApiClient _client;
    private readonly string _model;

    /// <param name="baseUrl">Ollama sunucusu (varsayılan: http://localhost:11434)</param>
    /// <param name="model">Kullanılacak model adı (örn: "qwen2.5:7b")</param>
    public OllamaLlmClient(string baseUrl = "http://localhost:11434", string model = "qwen2.5:7b")
    {
        _client = new OllamaApiClient(new Uri(baseUrl));
        _model = model;
    }

    /// <summary>
    /// Ollama aracılığıyla modele prompt gönderir; yanıtı tek string olarak döndürür.
    /// temperature: 0.0 = deterministik (RAG için önerilir), 0.7 = yaratıcı
    /// </summary>
    public async Task<string> GenerateResponseAsync(
        string prompt,
        string? systemInstruction = null,
        float temperature = 0.0f,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<Message>();

        if (!string.IsNullOrWhiteSpace(systemInstruction))
        {
            messages.Add(new Message { Role = ChatRole.System, Content = systemInstruction });
        }

        messages.Add(new Message { Role = ChatRole.User, Content = prompt });

        var request = new OllamaSharp.Models.Chat.ChatRequest
        {
            Model = _model,
            Messages = messages,
            Options = new OllamaSharp.Models.RequestOptions
            {
                Temperature = temperature
            },
            Stream = false
        };

        var responseBuilder = new System.Text.StringBuilder();

        await foreach (var chunk in _client.ChatAsync(request, cancellationToken))
        {
            if (chunk?.Message?.Content is { } content)
            {
                responseBuilder.Append(content);
            }
        }

        return responseBuilder.ToString().Trim();
    }

    /// <summary>
    /// Model erişilebilir mi kontrol eder — HealthCheck için kullanılabilir.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var models = await _client.ListLocalModelsAsync(cancellationToken);
            return models.Any(m => m.Name.StartsWith(_model.Split(':')[0], StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Modeli Ollama'ya indirir (yoksa). İlk çalıştırmada otomatik tetiklenir.
    /// </summary>
    public async Task EnsureModelPulledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var models = await _client.ListLocalModelsAsync(cancellationToken);
            bool exists = models.Any(m => m.Name.StartsWith(_model.Split(':')[0], StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                await foreach (var _ in _client.PullModelAsync(_model, cancellationToken)) { }
            }
        }
        catch
        {
            // Ollama çalışmıyorsa sessizce geç — fallback devreye girer
        }
    }
}
