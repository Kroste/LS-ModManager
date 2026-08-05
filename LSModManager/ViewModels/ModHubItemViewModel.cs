using CommunityToolkit.Mvvm.ComponentModel;
using LSModManager.Models;

namespace LSModManager.ViewModels;

/// <summary>UI-Adapter für einen ModHub-Katalog-Eintrag. Preview wird lazy per Bindung geladen.</summary>
public sealed partial class ModHubItemViewModel : ObservableObject
{
    public ModHubItemViewModel(ModHubEntry entry, bool isNew = false)
    {
        Model = entry;
        _isNew = isNew;
    }

    public ModHubEntry Model { get; }

    /// <summary>True wenn dieser Eintrag beim vorherigen App-Start noch nicht im
    /// Katalog war (Diff über CatalogCache.LoadSeenSnapshot). Steuert das
    /// „NEU"-Badge auf der Card. Wird auf false gesetzt sobald der Nutzer den
    /// Mod ansieht (Details öffnen, Download starten, Browser öffnen) — der
    /// Badge ist damit „gesehen"-getrieben, nicht nur zeitbasiert.</summary>
    [ObservableProperty] private bool _isNew;

    /// <summary>Vom MainVM aufgerufen sobald der User den Mod interagiert
    /// (Details/Download/Browser). No-op wenn schon nicht mehr neu.</summary>
    public void MarkAsSeen()
    {
        if (IsNew) IsNew = false;
    }

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
