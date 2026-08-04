using System.Text.RegularExpressions;
using NLog.LayoutRenderers.Wrappers;

namespace LSModManager.Logging;

/// <summary>
/// Kroste-Pflicht-Masking: entfernt Passwörter/Tokens/API-Keys aus Log-Messages,
/// bevor NLog sie in Datei/Konsole schreibt. Registrierung in Program.Main via
/// <c>LogManager.Setup().SetupExtensions(...)</c>.
/// </summary>
public sealed class MaskingLayoutRenderer : WrapperLayoutRendererBase
{
    private static readonly Regex[] Patterns =
    {
        new(@"(?i)(password|passwd|pwd)\s*=\s*[^\s;,]+", RegexOptions.Compiled),
        new(@"(?i)(api[_-]?key|apikey|token|bearer)\s*[:=]\s*[^\s;,]+", RegexOptions.Compiled),
        new(@"(?i)(Authorization:\s*Bearer)\s+[A-Za-z0-9._~+/=-]+", RegexOptions.Compiled),
    };

    protected override string Transform(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var result = text;
        foreach (var pattern in Patterns)
            result = pattern.Replace(result, m => $"{m.Groups[1].Value}=***");
        return result;
    }
}
