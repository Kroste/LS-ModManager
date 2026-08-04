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
public sealed class InstalledModItemViewModel : ObservableObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public InstalledModItemViewModel(InstalledMod mod)
    {
        Model = mod;
        Preview = LoadPreview(mod.PreviewImagePath);
    }

    public InstalledMod Model { get; }
    public Bitmap? Preview { get; }

    public string DisplayTitle => Model.Metadata?.Title is { Length: > 0 } t
        ? t
        : Path.GetFileNameWithoutExtension(Model.FileName);

    public string Author => Model.Metadata?.Author ?? "";
    public string Version => Model.Metadata?.Version ?? "";
    public string SizeText => FormatSize(Model.FileSizeBytes);
    public string StatusText => Model.IsEnabled ? "Aktiv" : "Deaktiviert";
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
            Log.Debug(ex, "Preview-Load fehlgeschlagen: {p}", path);
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
