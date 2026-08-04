using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LSModManager.Views;

/// <summary>
/// Wiederverwendbarer Ja/Nein-Bestätigungsdialog auf Kroste-ChromeWindow-Basis.
/// Nutzung: <c>await ConfirmDialog.ShowAsync(owner, title, message)</c> —
/// liefert <c>true</c> bei „Bestätigen", <c>false</c> bei „Abbrechen" oder
/// wenn das Fenster geschlossen wird (X-Klick).
/// </summary>
public partial class ConfirmDialog : ChromeWindow
{
    private bool _result;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(Window owner, string title, string message)
    {
        var dialog = new ConfirmDialog();
        dialog.Title = title;
        dialog.TitleBarControl.Title = title;
        dialog.MessageText.Text = message;
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }
}
