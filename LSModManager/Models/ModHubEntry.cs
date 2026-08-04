namespace LSModManager.Models;

/// <summary>
/// Ein Eintrag aus dem offiziellen ModHub-Katalog
/// (<c>farming-simulator.com/mods.php</c>).
/// Der Download läuft immer über den Browser (User-Zustimmung, ToS-konform).
/// </summary>
public sealed record ModHubEntry(
    string Title,
    string Author,
    string Category,
    string PreviewUrl,
    string DetailUrl,
    string? Version,
    string? SizeText);
