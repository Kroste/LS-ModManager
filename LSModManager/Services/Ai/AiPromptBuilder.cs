using System.Text;

namespace LSModManager.Services.Ai;

/// <summary>
/// App-spezifische Prompt-Bausteine für die zwei KI-Features im
/// LS-ModManager. Der Kroste-Baukasten liefert bewusst nur die
/// Transport-Schicht — Prompts sind pro App unterschiedlich und leben hier.
/// </summary>
public static class AiPromptBuilder
{
    // ---- Feature 1: Beschreibungs-Zusammenfassung ---------------------------

    public const string SummarizeSystemPrompt =
        "Du bist ein hilfreicher Assistent, der lange Mod-Beschreibungen für " +
        "Landwirtschaftssimulator 25 in eine knappe Zusammenfassung eindampft. " +
        "Antworte in 3 bis 4 kurzen Sätzen in derselben Sprache wie der Original-Text. " +
        "Fokus auf: was ist der Mod, welche Fahrzeuge/Geräte/Maps enthält er, " +
        "besondere Features. Marketing-Sprech und Danksagungen weglassen. " +
        "Keine Aufzählungen, keine Überschriften, kein Markdown — reiner Fließtext.";

    public static string BuildSummarizeUserPrompt(string modTitle, string description)
    {
        var sb = new StringBuilder();
        sb.Append("Mod: ").AppendLine(modTitle);
        sb.AppendLine();
        sb.AppendLine("Beschreibung:");
        sb.Append(description);
        return sb.ToString();
    }

    // ---- Feature 2: Ähnliche Mods-Empfehlungen -----------------------------

    public const string SimilarModsSystemPrompt =
        "Du bist ein hilfreicher Assistent für Landwirtschaftssimulator-25-Mods. " +
        "Der Nutzer schaut sich einen bestimmten Mod an, und du sollst aus einer " +
        "gegebenen Kandidatenliste die 5 verwandtesten empfehlen. Verwandtschaft " +
        "heißt: gleicher Zweck, thematisch passend, oder häufige Kombination im " +
        "Spielverlauf. Antworte NUR mit den fünf Mod-Titeln, exakt so wie in der " +
        "Kandidatenliste geschrieben, einer pro Zeile, ohne Nummerierung, ohne " +
        "Erklärung, ohne Anführungszeichen. Wenn weniger als 5 sinnvoll passen, " +
        "gib nur die tatsächlich passenden zurück.";

    public static string BuildSimilarModsUserPrompt(
        string currentTitle, string currentCategory, string currentAuthor,
        IEnumerable<string> candidateTitles)
    {
        var sb = new StringBuilder();
        sb.Append("Aktueller Mod: ").AppendLine(currentTitle);
        if (!string.IsNullOrWhiteSpace(currentCategory))
            sb.Append("Kategorie: ").AppendLine(currentCategory);
        if (!string.IsNullOrWhiteSpace(currentAuthor))
            sb.Append("Autor: ").AppendLine(currentAuthor);
        sb.AppendLine();
        sb.AppendLine("Kandidaten (wähle die 5 verwandtesten):");
        foreach (var title in candidateTitles)
            sb.Append("- ").AppendLine(title);
        return sb.ToString();
    }

    /// <summary>Parst die Antwort der KI (5 Mod-Titel, einer pro Zeile) und
    /// gibt die Titel als Liste zurück. Robuste Regeln: leere Zeilen weg,
    /// Nummerierungen (<c>1.</c>, <c>-</c>, <c>*</c>) und Anführungszeichen
    /// entfernen — manche Modelle halten sich trotz System-Prompt nicht dran.</summary>
    public static IReadOnlyList<string> ParseSimilarModTitles(string response)
    {
        var result = new List<string>();
        foreach (var raw in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            // Führende Marker entfernen: "1. ", "1) ", "- ", "* ", "• "
            line = System.Text.RegularExpressions.Regex.Replace(
                line, @"^(\d+[\.\)]\s*|[-*•]\s*)", "");
            line = line.Trim('"', '\'', '„', '“', ' ');
            if (line.Length > 0) result.Add(line);
        }
        return result;
    }
}
