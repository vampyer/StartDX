using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using StartDX.Dock.Services;
using StartDX.Dock.ViewModels;

namespace StartDX.Dock.Views;

/// <summary>Search bar + all-apps launcher list + frequently-used / pinned 128x128 tiles + power footer.</summary>
public partial class StartMenuWindow : FlyoutWindow
{
    private readonly StartMenuViewModel _vm;

    internal StartMenuWindow(StartMenuViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    protected override void OnOpening()
    {
        _vm.Reset();
        // Focus must be requested after the window is actually shown and activated.
        Dispatcher.BeginInvoke(() => { SearchBox.Focus(); Keyboard.Focus(SearchBox); }, DispatcherPriority.Input);
    }

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (_vm.ActivateTopResult()) HideFlyout();
    }

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not AppEntry app) return;
        HideFlyout();
        _vm.Launch(app);
    }

    private void Row_RightUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (((FrameworkElement)sender).DataContext is not AppEntry app) return;

        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => { HideFlyout(); _vm.Launch(app); };
        var pin = new MenuItem { Header = _vm.IsPinned(app) ? "Unpin from dock" : "Pin to dock" };
        pin.Click += (_, _) => _vm.TogglePin(app);
        menu.Items.Add(open);
        menu.Items.Add(pin);
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    // ── Footer ──────────────────────────────────────────────────────────────────────────────

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        HideFlyout();
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "StartDX.Settings.exe");
        if (System.IO.File.Exists(path)) ShellLauncher.Launch(path);
    }

    private void Lock_Click(object sender, RoutedEventArgs e) { HideFlyout(); StartMenuViewModel.Lock(); }
    private void Sleep_Click(object sender, RoutedEventArgs e) { HideFlyout(); StartMenuViewModel.Sleep(); }
    private void Restart_Click(object sender, RoutedEventArgs e) => Confirm("Restart this PC?", StartMenuViewModel.Restart);
    private void Shutdown_Click(object sender, RoutedEventArgs e) => Confirm("Shut down this PC?", StartMenuViewModel.Shutdown);

    private void Confirm(string question, Action action)
    {
        HideFlyout();
        if (MessageBox.Show(question, "StartDX", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            action();
    }
}

/// <summary>Visible only while both counts are zero (drives the empty-state hint).</summary>
public sealed class BothEmptyConverter : IMultiValueConverter
{
    public static readonly BothEmptyConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.All(v => v is int n && n == 0) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
