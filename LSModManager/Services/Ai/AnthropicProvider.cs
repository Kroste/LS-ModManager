using System.Net.Http.Json;
using System.Text.Json.Serialization;
using NLog;

namespace LSModManager.Services.Ai;

/// <summary>
/// Anthropic Claude über die native Messages-API (POST /v1/messages).
/// Header <c>x-api-key</c> und <c>anthropic-version</c> wie in der offiziellen
/// Anleitung.
/// </summary>
public sealed class AnthropicProvider : IAiProvider
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string? _apiKey;

    public AnthropicProvider(HttpClient http, string endpoint, string model, string? apiKey)
    {
        _http = http;
        _endpoint = endpoint.TrimEnd('/');
        _model = model;
        _apiKey = apiKey;
    }

    public string Name => $"Anthropic ({_model})";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(!string.IsNullOrWhiteSpace(_apiKey));

    /// <summary>Anthropic hat aktuell keinen öffentlichen "list models"-Endpoint,
    /// daher liefern wir eine kuratierte Liste (Stand 2026-08 — bei Bedarf
    /// aktualisieren).</summary>
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(
        [
            "claude-opus-4-7",
            "claude-sonnet-4-6",
            "claude-haiku-4-5",
            "claude-3-7-sonnet-latest",
        ]);

    public async Task<string> CompleteAsync(
        string systemPrompt, string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var req = new MessagesRequest(
            Model: _model,
            MaxTokens: 1024,
            System: systemPrompt,
            Messages: [new AnthropicMessage("user", userPrompt)]);

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/messages")
        {
            Content = JsonContent.Create(req),
        };
        httpReq.Headers.Add("x-api-key", _apiKey ?? "");
        httpReq.Headers.Add("anthropic-version", "2023-06-01");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var response = await _http.SendAsync(httpReq, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Anthropic HTTP {(int)response.StatusCode}: {err}");
        }
        var body = await response.Content.ReadFromJsonAsync<MessagesResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Anthropic-Antwort war leer.");
        Log.Debug("Anthropic {model}: Completion in {ms} ms", _model, sw.ElapsedMilliseconds);

        var text = body.Content?.FirstOrDefault(b => b.Type == "text")?.Text ?? "";
        return text.Trim();
    }

    // ---- DTOs ---------------------------------------------------------------

    private sealed record MessagesRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicMessage> Messages);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record MessagesResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<ContentBlock>? Content);

    private sealed record ContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}
