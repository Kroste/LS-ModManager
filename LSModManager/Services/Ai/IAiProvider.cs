namespace LSModManager.Services.Ai;

/// <summary>
/// Provider-Abstraktion für die KI-Aufrufe. Das Interface ist bewusst
/// generisch (nicht auf eine konkrete Aufgabe wie „übersetzen" oder
/// „zusammenfassen" spezialisiert) — jedes Feature baut seinen eigenen
/// System-/User-Prompt und interpretiert die String-Antwort selbst.
/// </summary>
public interface IAiProvider
{
    /// <summary>Kurzer Anzeige-Name (für Logs und UI).</summary>
    string Name { get; }

    /// <summary>Prüft ohne echte Anfrage, ob der Provider ansprechbar ist.
    /// Ollama: <c>GET /api/tags</c>. Cloud-Provider: kurzer authentifizierter
    /// Modellliste-Call oder eine minimale Ping-Completion — die konkrete
    /// Umsetzung ist Provider-spezifisch.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Listet die verfügbaren Modelle. Ollama: die installierten via
    /// <c>/api/tags</c>. Cloud-Provider: kuratierte Empfehlungsliste oder
    /// die API-Modellliste falls verfügbar.</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Führt eine einzelne Completion aus: System-Prompt (Rolle/Anweisung an
    /// die KI) + User-Prompt (die eigentliche Anfrage) → String-Antwort.
    /// Der Aufrufer parst die Antwort selbst (z.B. JSON extrahieren wenn er
    /// im System-Prompt JSON angefordert hat).
    /// </summary>
    /// <param name="systemPrompt">Rolle/Verhalten der KI (kann leer sein).</param>
    /// <param name="userPrompt">Die konkrete Anfrage.</param>
    /// <returns>Antwort-Text ohne führende/nachfolgende Whitespaces.</returns>
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
