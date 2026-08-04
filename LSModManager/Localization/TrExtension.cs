using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace LSModManager.Localization;

/// <summary>
/// Markup-Extension für kompaktes XAML-Binding auf lokalisierte Strings:
/// <c>Text="{loc:Tr Header_Title}"</c>. Erzeugt intern einen
/// <see cref="LocalizedString"/>-Wrapper aus dem statischen Cache und bindet
/// an dessen <see cref="LocalizedString.Value"/>-Property.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Wrapper aus dem statischen Cache — nicht pro Binding neu erzeugen!
        // Avalonia hält Binding.Source nicht dauerhaft stark; ein frisch
        // erzeugter Wrapper würde GC'd und die Sprachwechsel-Notification
        // liefe ins Leere.
        return new Binding(nameof(LocalizedString.Value))
        {
            Source = LocalizedString.Get(Key),
            Mode = BindingMode.OneWay,
        };
    }
}
