using LSModManager.ViewModels;

namespace LSModManager.Views;

public partial class ModDetailWindow : ChromeWindow
{
    public ModDetailWindow()
    {
        InitializeComponent();
    }

    public ModDetailWindow(ModDetailViewModel vm) : this()
    {
        DataContext = vm;
        _ = vm.InitializeAsync();
    }
}
