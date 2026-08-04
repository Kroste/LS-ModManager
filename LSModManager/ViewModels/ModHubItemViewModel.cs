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
}
