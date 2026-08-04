using System.Net.Http.Json;
using System.Text.Json.Serialization;
using NLog;

namespace LSModManager.Services.Ai;

/// <summary>
/// KI-Provider gegen OpenAI-kompatible Endpoints (POST /chat/completions). Deckt
/// OpenAI/ChatGPT, Mistral und beliebige andere Anbieter mit derselben API ab
/// (Groq, OpenRouter, LM Studio, …). Ohne <c>response_format=json_object</c> —
/// wir wollen freien Text, nicht strukturiertes JSON.
/// </summary>
public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly string _displayName;

    public OpenAiCompatibleProvider(HttpClient http, string endpoint, string model, string? apiKey,
        string displayName = "OpenAI-kompatibel")
    {
        _http = http;
        _endpoint = endpoint.TrimEnd('/');
        _model = model;
        _apiKey = apiKey;
        _displayName = displayName;
    }

    public string Name => $"{_displayName} ({_model})";

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_endpoint}/models");
            AddAuth(req);
            using var res = await _http.SendAsync(req, cancellationToken);
            // 401 = erreichbar aber Auth-Problem → wir werten das als „Server läuft".
            return res.IsSuccessStatusCode || res.StatusCode == System.Net.HttpStatusCode.Unauthorized;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "{provider}-Verfügbarkeit-Check fehlgeschlagen: {ep}", _displayName, _endpoint);
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_endpoint}/models");
            AddAuth(req);
            using var res = await _http.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) return [];
            var body = await res.Content.ReadFromJsonAsync<ModelsResponse>(cancellationToken);
            return body?.Data?.Select(m => m.Id).ToList() ?? [];
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "{provider}-Modellliste nicht abrufbar: {ep}", _displayName, _endpoint);
            return [];
        }
    }

    public async Task<string> CompleteAsync(
        string systemPrompt, string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new ChatMessage("system", systemPrompt));
        messages.Add(new ChatMessage("user", userPrompt));

        var req = new ChatRequest(Model: _model, Messages: messages, Temperature: 0.3);

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/chat/completions")
        {
            Content = JsonContent.Create(req),
        };
        AddAuth(httpReq);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var response = await _http.SendAsync(httpReq, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"{_displayName} HTTP {(int)response.StatusCode}: {err}");
        }
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken)
            ?? throw new InvalidOperationException($"{_displayName}-Antwort war leer.");
        Log.Debug("{provider} {model}: Completion in {ms} ms",
            _displayName, _model, sw.ElapsedMilliseconds);

        return (body.Choices?.FirstOrDefault()?.Message?.Content ?? "").Trim();
    }

    private void AddAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
            req.Headers.Add("Authorization", "Bearer " + _apiKey);
    }

    // ---- DTOs ---------------------------------------------------------------

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<Choice>? Choices);

    private sealed record Choice(
        [property: JsonPropertyName("message")] ChatMessage? Message);

    private sealed record ModelsResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<ModelId>? Data);

    private sealed record ModelId(
        [property: JsonPropertyName("id")] string Id);
}
