namespace LSModManager.Services.Ai;

/// <summary>
/// Erzeugt für die aktuellen <see cref="AiSettings"/> den passenden
/// <see cref="IAiProvider"/>. Mistral wird über den OpenAI-kompatiblen
/// Provider bedient (Mistrals API ist OpenAI-Chat-Completions-konform).
/// </summary>
public sealed class AiProviderFactory
{
    private readonly IHttpClientFactory _httpFactory;

    public AiProviderFactory(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    /// <summary>Liefert einen konfigurierten Provider oder <c>null</c>, wenn
    /// <see cref="AiSettings.Provider"/> auf <see cref="AiProviderType.None"/>
    /// steht oder der aktive Provider Pflichtfelder wie API-Key vermissen
    /// lässt.</summary>
    public IAiProvider? Create(AiSettings settings)
    {
        var cfg = settings.Active;
        var http = _httpFactory.CreateClient("ai");

        // Ollama: sehr langer Timeout (lokale Modelle können minutenlang rechnen).
        // Cloud-Provider: 2 min reicht für Rate-Limit-Waits, gegen hängende
        // Verbindungen aber ein hartes Ende.
        http.Timeout = settings.Provider == AiProviderType.Ollama
            ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(2);

        return settings.Provider switch
        {
            AiProviderType.None => null,
            AiProviderType.Ollama => new OllamaProvider(http, cfg.Endpoint, cfg.Model),
            AiProviderType.Anthropic => string.IsNullOrWhiteSpace(cfg.ApiKey) ? null
                : new AnthropicProvider(http, cfg.Endpoint, cfg.Model, cfg.ApiKey),
            AiProviderType.OpenAi => string.IsNullOrWhiteSpace(cfg.ApiKey) ? null
                : new OpenAiCompatibleProvider(http, cfg.Endpoint, cfg.Model, cfg.ApiKey, "OpenAI"),
            AiProviderType.Gemini => string.IsNullOrWhiteSpace(cfg.ApiKey) ? null
                : new GeminiProvider(http, cfg.Endpoint, cfg.Model, cfg.ApiKey),
            AiProviderType.Mistral => string.IsNullOrWhiteSpace(cfg.ApiKey) ? null
                : new OpenAiCompatibleProvider(http, cfg.Endpoint, cfg.Model, cfg.ApiKey, "Mistral"),
            AiProviderType.OpenAiCompatible => new OpenAiCompatibleProvider(
                http, cfg.Endpoint, cfg.Model, cfg.ApiKey, "OpenAI-kompatibel"),
            _ => null,
        };
    }

    /// <summary>Baut nur den Ollama-Provider auf, unabhängig von der aktuell
    /// gewählten Provider-Auswahl. Für den Modell-Pull im Einstellungen-Fenster
    /// gedacht — man kann Ollama-Modelle auch dann ziehen wenn ein Cloud-
    /// Provider aktiv ist.</summary>
    public OllamaProvider CreateOllama(AiSettings settings)
    {
        var http = _httpFactory.CreateClient("ai-pull");
        http.Timeout = TimeSpan.FromHours(1); // Pull kann bei 4-GB-Modellen dauern.
        return new OllamaProvider(http, settings.Ollama.Endpoint, settings.Ollama.Model);
    }
}
