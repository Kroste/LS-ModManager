using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LSModManager;

/// <summary>
/// Löst ViewModel → View über Namenskonvention auf:
/// LSModManager.ViewModels.FooViewModel  →  LSModManager.Views.Foo
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null) return new TextBlock { Text = "null" };
        var name = data.GetType().FullName!
            .Replace("ViewModel", string.Empty)
            .Replace(".ViewModels.", ".Views.");
        var type = Type.GetType(name);
        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ObservableObject;
}
