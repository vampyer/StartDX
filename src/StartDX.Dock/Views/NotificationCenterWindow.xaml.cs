using System.Windows;
using StartDX.Dock.Services;
using StartDX.Dock.ViewModels;

namespace StartDX.Dock.Views;

/// <summary>Flyout listing received notifications (tray balloons, IPC notify, optional toast bridge).</summary>
public partial class NotificationCenterWindow : FlyoutWindow
{
    private readonly NotificationService _service;

    internal NotificationCenterWindow(NotificationService service)
    {
        _service = service;
        DataContext = service;
        InitializeComponent();
    }

    protected override void OnOpening() => _service.MarkAllRead();

    private void Clear_Click(object sender, RoutedEventArgs e) => _service.Clear();

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is NotificationItem item) _service.Remove(item);
    }
}
