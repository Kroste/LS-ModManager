using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LSModManager.Localization;

/// <summary>
/// Bindbarer Wrapper um einen einzelnen Localization-Key. Wird von
/// <see cref="TrExtension"/> über <see cref="Get"/> beschafft (nicht pro
/// Binding neu erzeugt!) und im XAML per Binding an <see cref="Value"/>
/// konsumiert.
/// <para>
/// <b>Warum statisch gecacht:</b> Avalonias <c>Binding.Source</c> hält die
/// Referenz nicht dauerhaft stark. Ein pro-Binding erzeugter Wrapper würde
/// nach dem ersten Rendering vom GC eingesammelt und die Sprachwechsel-
/// Notification liefe ins Leere (real passiert in RenPack v0.5.1). Der
/// statische Cache hält für jeden Key genau einen Wrapper stark für die
/// App-Lebensdauer — typisch ~150 Instanzen, wenige KB gesamt.
/// </para>
/// </summary>
public sealed class LocalizedString : INotifyPropertyChanged
{
    public string Key { get; }
    public string Value => LocalizationService.Instance[Key];

    private static readonly Dictionary<string, LocalizedString> _cache = new(StringComparer.Ordinal);
    private static readonly object _lock = new();

    private LocalizedString(string key) => Key = key;

    public static LocalizedString Get(string key)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out var s))
            {
                s = new LocalizedString(key);
                _cache[key] = s;
            }
            return s;
        }
    }

    /// <summary>
    /// Feuert <c>PropertyChanged(nameof(Value))</c> auf jedem gecachten
    /// Wrapper. Wird vom <see cref="LocalizationService"/> beim Sprachwechsel
    /// aufgerufen.
    /// </summary>
    internal static void NotifyAllChanged()
    {
        LocalizedString[] snapshot;
        lock (_lock) snapshot = _cache.Values.ToArray();
        foreach (var s in snapshot) s.OnPropertyChanged(nameof(Value));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
