using CommunityToolkit.Mvvm.ComponentModel;
using LSModManager.Models;

namespace LSModManager.ViewModels;

/// <summary>UI-Adapter für einen ModHub-Katalog-Eintrag. Preview wird lazy per Bindung geladen.</summary>
public sealed class ModHubItemViewModel : ObservableObject
{
    public ModHubItemViewModel(ModHubEntry entry) => Model = entry;

    public ModHubEntry Model { get; }
    public string Title => Model.Title;
    public string Author => Model.Author;
    public string Category => Model.Category;
    public string PreviewUrl => Model.PreviewUrl;
    public string DetailUrl => Model.DetailUrl;
    public string? Version => Model.Version;
    public string? SizeText => Model.SizeText;

    /// <summary>Menschlich lesbares Label für die Card („GIANTS", „Modhoster", „Hof Hirschfeld").</summary>
    public string SourceLabel => Model.Source switch
    {
        ModHubEntry.ModhosterSource => "Modhoster",
        ModHubEntry.HofHirschfeldSource => "Hof Hirschfeld",
        _ => "GIANTS",
    };
    public bool IsGiantsSource => Model.Source == ModHubEntry.GiantsSource;
    public bool IsModhosterSource => Model.Source == ModHubEntry.ModhosterSource;

    /// <summary>Nur GIANTS erlaubt In-App-Download; Modhoster braucht Browser.</summary>
    public bool CanInAppDownload => Model.CanInAppDownload;
    public bool NeedsBrowser => !Model.CanInAppDownload;
}
