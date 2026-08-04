namespace LSModManager.Services.Ai;

/// <summary>KI-Anbieter. Ollama läuft lokal ohne API-Key; Cloud-Anbieter brauchen
/// jeweils einen persönlichen Key, den die App verschlüsselt speichert (siehe
/// <see cref="SecretProtection"/>).</summary>
public enum AiProviderType
{
    /// <summary>KI deaktiviert.</summary>
    None,
    /// <summary>Lokales Ollama (Default — datenschutzfreundlich, kein API-Key).</summary>
    Ollama,
    /// <summary>Anthropic Claude über die native Messages-API.</summary>
    Anthropic,
    /// <summary>OpenAI ChatGPT über die Chat-Completions-API.</summary>
    OpenAi,
    /// <summary>Google Gemini über die Generative-Language-API.</summary>
    Gemini,
    /// <summary>Mistral über die (OpenAI-kompatible) Chat-Completions-API.</summary>
    Mistral,
    /// <summary>Generischer OpenAI-kompatibler Anbieter (Groq, OpenRouter,
    /// LM Studio, …) mit frei konfigurierbarer Base-URL.</summary>
    OpenAiCompatible,
}

/// <summary>Provider-spezifische Konfiguration: Endpoint, Modellname, API-Key.</summary>
public sealed record AiProviderConfig(string Endpoint, string Model, string? ApiKey = null);

/// <summary>Persistente KI-Konfiguration der App. Die API-Keys werden vor dem
/// Speichern per <see cref="SecretProtection"/> verschlüsselt und liegen NICHT
/// im Klartext in der JSON-Datei.</summary>
public sealed record AiSettings(
    AiProviderType Provider,
    AiProviderConfig Ollama,
    AiProviderConfig Anthropic,
    AiProviderConfig OpenAi,
    AiProviderConfig Gemini,
    AiProviderConfig Mistral,
    AiProviderConfig OpenAiCompatible)
{
    /// <summary>Hol die Config des aktuell ausgewählten Anbieters.</summary>
    public AiProviderConfig Active => Provider switch
    {
        AiProviderType.Ollama => Ollama,
        AiProviderType.Anthropic => Anthropic,
        AiProviderType.OpenAi => OpenAi,
        AiProviderType.Gemini => Gemini,
        AiProviderType.Mistral => Mistral,
        AiProviderType.OpenAiCompatible => OpenAiCompatible,
        _ => Ollama,
    };

    /// <summary>True wenn ein Provider aktiv ist — Feature-Buttons in der UI
    /// bleiben sonst ausgeblendet.</summary>
    public bool IsEnabled => Provider != AiProviderType.None;

    public static AiSettings Default => new(
        Provider: AiProviderType.None,
        Ollama:            AiDefaults.Config(AiProviderType.Ollama),
        Anthropic:         AiDefaults.Config(AiProviderType.Anthropic),
        OpenAi:            AiDefaults.Config(AiProviderType.OpenAi),
        Gemini:            AiDefaults.Config(AiProviderType.Gemini),
        Mistral:           AiDefaults.Config(AiProviderType.Mistral),
        OpenAiCompatible:  AiDefaults.Config(AiProviderType.OpenAiCompatible));
}

/// <summary>Provider-spezifische Standard-Endpunkte und -Modelle. Werden im
/// SettingsWindow überschrieben.</summary>
public static class AiDefaults
{
    public static AiProviderConfig Config(AiProviderType p) => new(
        Endpoint: Endpoint(p),
        Model: Model(p),
        ApiKey: null);

    public static string Endpoint(AiProviderType p) => p switch
    {
        AiProviderType.Ollama            => "http://localhost:11434",
        AiProviderType.Anthropic         => "https://api.anthropic.com/v1",
        AiProviderType.OpenAi            => "https://api.openai.com/v1",
        AiProviderType.Gemini            => "https://generativelanguage.googleapis.com/v1beta",
        AiProviderType.Mistral           => "https://api.mistral.ai/v1",
        AiProviderType.OpenAiCompatible  => "http://localhost:8080/v1",
        _ => "",
    };

    /// <summary>Sinnvolle Standard-Modelle pro Provider (Stand: 2026-08). Bei
    /// Update auch die Skill-Empfehlungen nachziehen.</summary>
    public static string Model(AiProviderType p) => p switch
    {
        AiProviderType.Ollama            => "gemma3:1b",
        AiProviderType.Anthropic         => "claude-haiku-4-5",
        AiProviderType.OpenAi            => "gpt-4o-mini",
        AiProviderType.Gemini            => "gemini-2.0-flash",
        AiProviderType.Mistral           => "mistral-small-latest",
        AiProviderType.OpenAiCompatible  => "",
        _ => "",
    };
}

/// <summary>Ein einzelnes Event aus Ollamas NDJSON-Antwort von POST /api/pull.
/// <c>Completed</c> und <c>Total</c> sind nur während der Download-Phase gesetzt.</summary>
public sealed record OllamaPullEvent(
    string Status,
    long? Completed,
    long? Total,
    string? Digest,
    bool IsError = false,
    string? ErrorMessage = null);

/// <summary>Kuratierte Modellvorschläge für Ollama. Für den LS-ModManager reichen
/// kleine Modelle: die Aufgaben sind Zusammenfassung (kurzer Absatz) und
/// Empfehlungslisten aus Kategorie-Kontext — keine Multi-Turn-Chats, kein Code.</summary>
public sealed record OllamaCuratedModel(string Name, string ApproxSize, string Description);

public static class OllamaCuratedModels
{
    public static IReadOnlyList<OllamaCuratedModel> All { get; } = new[]
    {
        new OllamaCuratedModel("gemma3:1b",       "~815 MB",  "Sehr klein, sehr schnell — Default"),
        new OllamaCuratedModel("qwen2.5:3b",      "~1.9 GB",  "Etwas größer, bessere Textqualität"),
        new OllamaCuratedModel("llama3.2:3b",     "~2.0 GB",  "Meta Llama 3.2, ausgewogen"),
        new OllamaCuratedModel("phi3:mini",       "~2.3 GB",  "Microsoft Phi-3 Mini, mehrsprachig"),
        new OllamaCuratedModel("mistral:7b",      "~4.1 GB",  "Mistral 7B, breiter Sprachschatz"),
    };
}
