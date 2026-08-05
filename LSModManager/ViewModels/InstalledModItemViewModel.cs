using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LSModManager.Models;
using NLog;

namespace LSModManager.ViewModels;

/// <summary>
/// UI-Adapter für einen installierten Mod. Kapselt Anzeige-Format
/// (Größe in MB, Titel-Fallback, Bitmap-Load) — hält das MainVM sauber.
/// </summary>
public sealed partial class InstalledModItemViewModel : ObservableObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public InstalledModItemViewModel(InstalledMod mod, bool isAlreadyInstalled = false)
    {
        Model = mod;
        Preview = LoadPreview(mod.PreviewImagePath);
        _isAlreadyInstalled = isAlreadyInstalled;
    }

    public InstalledMod Model { get; }
    public Bitmap? Preview { get; }

    /// <summary>True wenn dieses Downloads-Tab-Item bereits einen entsprechenden
    /// installierten Mod im Mod-Ordner hat (exakter Filename-Match). Für den
    /// Installiert-Tab immer false — dort ist der Zustand trivial „installiert".
    /// Steuert den grünen „✓ Installiert"-Badge auf Downloads-Cards. Muss
    /// mutable sein damit ein Install/Uninstall live ins UI durchschlägt
    /// ohne neue VM-Instanz.</summary>
    [ObservableProperty] private bool _isAlreadyInstalled;

    /// <summary>Wird von der Update-Prüfung gesetzt, wenn eine neuere Version im Katalog steht.</summary>
    [ObservableProperty]
    private string? _updateAvailableVersion;

    public bool HasUpdate => !string.IsNullOrWhiteSpace(UpdateAvailableVersion);
    partial void OnUpdateAvailableVersionChanged(string? value) => OnPropertyChanged(nameof(HasUpdate));

    public void SetUpdateAvailable(string newVersion) => UpdateAvailableVersion = newVersion;

    public string DisplayTitle => Model.Metadata?.Title is { Length: > 0 } t
        ? t
        : Path.GetFileNameWithoutExtension(Model.FileName);

    public string Author => Model.Metadata?.Author ?? "";
    public string Version => Model.Metadata?.Version ?? "";
    public string SizeText => FormatSize(Model.FileSizeBytes);
    public bool HasError => Model.ReadError is { Length: > 0 };
    public string? ErrorText => Model.ReadError;
    public bool MultiplayerSupported => Model.Metadata?.MultiplayerSupported ?? false;

    private static Bitmap? LoadPreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            // Cache-Datei ist korrupt (z.B. Altlasten aus einer früheren App-Version
            // mit anderem Format). Löschen, damit der nächste Refresh sie neu holt
            // oder still auf den Emoji-Fallback zurückfällt.
            Log.Debug(ex, "Preview-Load fehlgeschlagen — lösche kaputten Cache: {p}", path);
            try { File.Delete(path); } catch { /* best-effort */ }
            return null;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:F1} KB";
        return $"{bytes / (1024d * 1024d):F1} MB";
    }
}
