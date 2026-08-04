namespace LSModManager.Models;

/// <summary>
/// Ein Mod, wie er im lokalen LS25-Mod-Ordner liegt (aktiviert oder deaktiviert).
/// Deaktiviert = Endung <c>.zip.disabled</c> (LS25 lädt nur echte .zip-Dateien).
/// </summary>
public sealed record InstalledMod(
    string FilePath,
    string FileName,
    long FileSizeBytes,
    DateTime InstalledUtc,
    bool IsEnabled,
    ModMetadata? Metadata,
    string? PreviewImagePath = null,
    string? ReadError = null);
