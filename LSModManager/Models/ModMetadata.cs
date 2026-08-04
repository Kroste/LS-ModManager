namespace LSModManager.Models;

/// <summary>
/// Aus <c>modDesc.xml</c> gelesene Metadaten eines FS/LS-Mods.
/// Titel/Beschreibung sind sprachabhängig — wir liefern DE/EN mit Fallback.
/// </summary>
public sealed record ModMetadata(
    string Title,
    string Author,
    string Version,
    string Description,
    string? IconFileName,
    bool MultiplayerSupported,
    int DescVersion);
