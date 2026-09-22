using System.Windows;
using StartDX.Settings.Infrastructure;

namespace StartDX.Settings.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm = new();

    public SettingsWindow()
    {
        DataContext = _vm;
        InitializeComponent();
        Closed += (_, _) => _vm.Dispose();
    }
}
