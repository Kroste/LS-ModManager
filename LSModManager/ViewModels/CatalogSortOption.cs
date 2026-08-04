using LSModManager.Localization;

namespace LSModManager.ViewModels;

/// <summary>Sortierschlüssel für den Katalog-Tab.</summary>
public enum CatalogSortKey
{
    /// <summary>Ladereihenfolge — was der Katalog-Loader vom ModHub/Modhoster/
    /// Hof-Hirschfeld geliefert hat. Default weil bei GIANTS meistens die
    /// „Neueste zuerst"-Reihenfolge der Site.</summary>
    Default,
    Name,
    Author,
    Category,
}

/// <summary>ComboBox-Item für die Sortier-Auswahl im Katalog-Tab. Analog zur
/// <see cref="InstalledSortOption"/> — <see cref="LocalizedString"/>-Wrapper
/// für Live-Sprachwechsel.</summary>
public sealed record CatalogSortOption(CatalogSortKey Key, LocalizedString LabelWrapper);
