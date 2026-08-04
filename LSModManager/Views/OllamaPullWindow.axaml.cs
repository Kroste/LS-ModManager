using Avalonia.Threading;
using LSModManager.ViewModels;

namespace LSModManager.Views;

public partial class OllamaPullWindow : ChromeWindow
{
    public OllamaPullWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is OllamaPullViewModel vm)
            vm.CloseRequested += () =>
                Dispatcher.UIThread.Post(() => Close(vm.State == PullState.Succeeded));
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is OllamaPullViewModel vm)
            await vm.StartAsync();
    }
}
