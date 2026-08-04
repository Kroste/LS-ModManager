using LSModManager.Localization;

namespace LSModManager.ViewModels;

/// <summary>Sortierschlüssel für die Installiert-Liste.</summary>
public enum InstalledSortKey
{
    Name,
    Size,
    Date,
    Status,
}

/// <summary>
/// ComboBox-Item für die Sortier-Auswahl im Installiert-Tab. Der Label wird
/// via <see cref="LocalizedString"/>-Wrapper live-fähig gebunden — bei
/// Sprachwechsel aktualisiert sich der ComboBox-Text automatisch, ohne dass
/// die Options-Liste neu gebaut werden müsste.
/// </summary>
public sealed record InstalledSortOption(InstalledSortKey Key, LocalizedString LabelWrapper);
