using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Native;
using StartDX.Dock.Services;
using StartDX.Dock.Theming;
using StartDX.Dock.ViewModels;
using StartDX.Shared;
using StartDX.Shared.Ipc;

namespace StartDX.Dock.Views;

/// <summary>
/// The docked bar. Responsibilities, in order of appearance below:
///   1. Win32 plumbing  - become a tool window, register as an appbar, hook the window procedure.
///   2. Layout          - dock to an edge/monitor, flip the internal layout between horizontal and vertical.
///   3. Interaction     - start/notification flyouts, task switching, tray clicks, drag-to-dock, drop-to-pin, menus.
/// Everything visual is theme-driven (see Themes/); nothing in this file mentions a colour.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppServices _s;
    private readonly DockViewModel _vm;
    private readonly DispatcherTimer _relayout;

    private IntPtr _hwnd;
    private StartMenuViewModel? _startVm;
    private StartMenuWindow? _start;
    private NotificationCenterWindow? _notifications;

    // drag-to-dock state
    private POINT _dragOrigin;
    private bool _dragging;

    internal MainWindow(AppServices services)
    {
        _s = services;
        _vm = new DockViewModel(_s.Settings, _s.Tasks, _s.Tray, _s.Notifications, _s.SystemStatus);
        DataContext = _vm;
        InitializeComponent();

        WindowBackdrop.Register(this, BackdropRole.Dock);

        // Debounced re-dock: display/DPI/appbar notifications arrive in bursts.
        _relayout = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) =>
        {
            _relayout!.Stop();
            Redock();
        }, Dispatcher);

        _s.Settings.Changed += OnSettingsChanged;
        _s.AppBar.LayoutInvalidated += ScheduleRelayout;                  // ABN_POSCHANGED
        _s.Tasks.ForegroundChanged += _s.AppBar.ReassertTopmost;          // keep HWND_TOPMOST after focus changes
        _s.WinKey.WinKeyTapped += ToggleStart;
        _s.Tray.RectProvider = TrayIconScreenRect;
        _s.Ipc.CommandReceived += name =>
        {
            if (name == IpcCommands.ToggleStart) ToggleStart();
            else if (name == IpcCommands.ToggleNotifications) ToggleNotifications();
            else if (name == IpcCommands.Exit) Application.Current.Shutdown();
        };
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  1. Win32 plumbing
    // ═════════════════════════════════════════════════════════════════════════════════════════

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // TOOLWINDOW: never shown in Alt-Tab.  NOACTIVATE: clicking the dock does not steal focus from the app you are
        // using, which keeps "is this window the foreground window?" (task-button toggle) meaningful.
        var ex = (long)User32.GetWindowLongPtr(_hwnd, GWL.EXSTYLE);
        User32.SetWindowLongPtr(_hwnd, GWL.EXSTYLE, (IntPtr)(ex | WSEX.TOOLWINDOW | WSEX.NOACTIVATE));
        User32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP.NOMOVE | SWP.NOSIZE | SWP.NOZORDER | SWP.NOACTIVATE | SWP.FRAMECHANGED);

        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);

        _s.AppBar.Register(_hwnd);          // ABM_NEW - from here on Windows reserves our screen strip
        Redock();
    }

    protected override void OnClosed(EventArgs e)
    {
        _s.AppBar.Unregister();              // ABM_REMOVE - hand the screen space back
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Appbar callback (ABN_*), WM_ACTIVATE -> ABM_ACTIVATE, WM_WINDOWPOSCHANGED -> ABM_WINDOWPOSCHANGED.
        _s.AppBar.OnWndProc((uint)msg, wParam, lParam);

        switch ((uint)msg)
        {
            case WM.DISPLAYCHANGE:      // resolution / monitor topology changed
            case WM.DPICHANGED:         // dragged to / settings changed on a monitor with a different scale
                ScheduleRelayout();
                break;
        }
        return IntPtr.Zero;
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  2. Layout & positioning
    // ═════════════════════════════════════════════════════════════════════════════════════════

    private void ScheduleRelayout()
    {
        _relayout.Stop();
        _relayout.Start();
    }

    /// <summary>The dock rectangle, edge and monitor - flyouts use this to anchor themselves.</summary>
    private DockPlacement Placement => new(_s.AppBar.Bounds, _s.AppBar.Edge, _s.AppBar.Monitor);

    /// <summary>
    /// (Re)apply the configured edge / monitor / thickness. Converts the DIP thickness to physical pixels *for the target
    /// monitor's DPI* (appbar rectangles are always physical pixels) and runs the QUERYPOS/SETPOS negotiation.
    /// </summary>
    private void Redock()
    {
        if (_hwnd == IntPtr.Zero) return;
        var s = _s.Settings.Current;

        var monitor = MonitorHelper.Resolve(s.MonitorDevice);
        var thicknessPx = (int)Math.Round(s.ThicknessDip * monitor.Scale);
        _s.AppBar.Dock(s.Edge, monitor, thicknessPx);

        _vm.SyncFromSettings(s);
        ApplyOrientation();

        // Flyouts anchored to the previous edge are now in the wrong place.
        _start?.HideFlyout();
        _notifications?.HideFlyout();
    }

    /// <summary>Flip the internal layout: Start button first and tray last, along the dock's long axis.</summary>
    private void ApplyOrientation()
    {
        var horizontal = _vm.IsHorizontal;

        // (Fully qualified: inside namespace StartDX.Dock the bare name "Dock" would resolve to the namespace.)
        DockPanel.SetDock(StartButton, horizontal ? System.Windows.Controls.Dock.Left : System.Windows.Controls.Dock.Top);
        DockPanel.SetDock(TrayCluster, horizontal ? System.Windows.Controls.Dock.Right : System.Windows.Controls.Dock.Bottom);

        CenterScroll.HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
        CenterScroll.VerticalScrollBarVisibility = horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;

        Divider.Width = horizontal ? 1 : 24;
        Divider.Height = horizontal ? 24 : 1;

        // Vertical bars get a little breathing room so 44px buttons are not flush against the border.
        Layout.Margin = horizontal ? new Thickness(6, 4, 6, 4) : new Thickness(4, 6, 4, 6);
    }

    private void OnSettingsChanged(AppSettings old, AppSettings cur)
    {
        if (old.Edge != cur.Edge || old.ThicknessDip != cur.ThicknessDip || old.MonitorDevice != cur.MonitorDevice)
            Redock();
        else
            _vm.SyncFromSettings(cur);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  3. Interaction
    // ═════════════════════════════════════════════════════════════════════════════════════════

    // ── Flyouts ─────────────────────────────────────────────────────────────────────────────

    private void StartButton_Click(object sender, RoutedEventArgs e) => ToggleStart();
    private void NotifButton_Click(object sender, RoutedEventArgs e) => ToggleNotifications();

    // ── System indicators (volume / network / battery) ──────────────────────────────────────
    // Click toggles mute, the wheel changes the volume, right-click opens sound settings.

    private void Input_Click(object sender, RoutedEventArgs e) => SystemStatusService.CycleInputMethod();

    private void Volume_Click(object sender, RoutedEventArgs e) => _s.SystemStatus.ToggleMute();

    private void Volume_Wheel(object sender, MouseWheelEventArgs e)
    {
        _s.SystemStatus.AdjustVolume(e.Delta > 0 ? 2 : -2);
        e.Handled = true;
    }

    private void Volume_RightUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowMenu((FrameworkElement)sender,
            ("Sound settings", () => ShellLauncher.Launch("ms-settings:sound"), false),
            ("Volume mixer", () => ShellLauncher.Launch("ms-settings:apps-volume"), false));
    }

    private void Network_Click(object sender, RoutedEventArgs e) => ShellLauncher.Launch("ms-settings:network");
    private void Battery_Click(object sender, RoutedEventArgs e) => ShellLauncher.Launch("ms-settings:batterysaver");

    private void ClockButton_Click(object sender, RoutedEventArgs e) => ShellLauncher.Launch("ms-settings:dateandtime");

    private void ToggleStart()
    {
        _startVm ??= new StartMenuViewModel(_s.Usage, _s.Settings);
        _start ??= new StartMenuWindow(_startVm);
        _notifications?.HideFlyout();
        _start.Toggle(Placement, FlyoutAlign.Start);
    }

    private void ToggleNotifications()
    {
        _notifications ??= new NotificationCenterWindow(_s.Notifications);
        _start?.HideFlyout();
        _notifications.Toggle(Placement, FlyoutAlign.End);
    }

    // ── Task switcher ───────────────────────────────────────────────────────────────────────

    private void Task_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TaskItem t) _s.Tasks.Toggle(t);
    }

    private void Task_RightUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (((FrameworkElement)sender).DataContext is not TaskItem t) return;
        ShowMenu((FrameworkElement)sender,
            ("Restore", () => TaskListService.Restore(t), false),
            ("Minimize", () => TaskListService.Minimize(t), false),
            ("Maximize", () => TaskListService.Maximize(t), false),
            (null, null, false),
            ("Close window", () => TaskListService.Close(t), false));
    }

    // ── Pinned shortcuts ────────────────────────────────────────────────────────────────────

    private void Pinned_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PinnedAppViewModel p) ShellLauncher.Launch(p.Target);
    }

    private void Pinned_RightUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (((FrameworkElement)sender).DataContext is not PinnedAppViewModel p) return;
        ShowMenu((FrameworkElement)sender,
            ("Open", () => ShellLauncher.Launch(p.Target), false),
            ("Unpin from dock", () => _s.Settings.Update(s => s.Pinned.RemoveAll(x => x.Target == p.Target)), false));
    }

    private void Chrome_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Drop .exe / .lnk / .url / .bat files anywhere on the dock to pin them.</summary>
    private void Chrome_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var allowed = new[] { ".exe", ".lnk", ".url", ".bat", ".cmd", ".appref-ms" };
        _s.Settings.Update(s =>
        {
            foreach (var f in files.Where(f => allowed.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
                if (!s.Pinned.Any(p => string.Equals(p.Target, f, StringComparison.OrdinalIgnoreCase)))
                    s.Pinned.Add(new PinnedApp { Name = Path.GetFileNameWithoutExtension(f), Target = f });
        });
    }

    // ── Notification-area icons ─────────────────────────────────────────────────────────────

    private void Tray_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not TrayIconItem item) return;
        User32.GetCursorPos(out var pt);
        TrayClick? click = e.ChangedButton switch
        {
            MouseButton.Left => e.ClickCount >= 2 ? TrayClick.LeftDouble : TrayClick.Left,
            MouseButton.Right => TrayClick.Right,
            _ => null,
        };
        if (click is null) return;
        e.Handled = true;
        _s.Tray.SendClick(item, click.Value, pt);
    }

    /// <summary>Answers Shell_NotifyIconGetRect: the icon's on-screen rectangle in physical pixels.</summary>
    private RECT? TrayIconScreenRect(TrayIconItem item)
    {
        if (TrayItems.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement fe || !fe.IsVisible) return null;
        var scale = _s.AppBar.Monitor.Scale;
        var topLeft = fe.PointToScreen(new Point(0, 0));      // WPF returns physical pixels here
        return new RECT((int)topLeft.X, (int)topLeft.Y,
            (int)(topLeft.X + fe.ActualWidth * scale), (int)(topLeft.Y + fe.ActualHeight * scale));
    }

    // ── Drag-to-dock (grab any empty part of the bar and drag toward another screen edge) ───

    private void Chrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        User32.GetCursorPos(out _dragOrigin);
        _dragging = false;
        Chrome.CaptureMouse();
    }

    private void Chrome_MouseMove(object sender, MouseEventArgs e)
    {
        if (!Chrome.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        User32.GetCursorPos(out var pt);

        // Require a deliberate drag so a slightly sloppy click never re-docks the bar.
        var threshold = 24 * _s.AppBar.Monitor.Scale;
        if (!_dragging && Math.Abs(pt.X - _dragOrigin.X) + Math.Abs(pt.Y - _dragOrigin.Y) < threshold) return;
        _dragging = true;

        // Snap to whichever edge of whichever monitor the cursor is nearest - live, so the bar visibly follows.
        var monitor = MonitorHelper.FromPoint(pt.X, pt.Y);
        var edge = MonitorHelper.NearestEdge(monitor.Bounds, pt.X, pt.Y);
        var device = monitor.IsPrimary ? null : monitor.DeviceName;

        var cur = _s.Settings.Current;
        if (edge != cur.Edge || device != cur.MonitorDevice)
            _s.Settings.Update(s => { s.Edge = edge; s.MonitorDevice = device; });   // -> Changed -> Redock (+ IPC state push)
    }

    private void Chrome_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Chrome.ReleaseMouseCapture();
        _dragging = false;
    }

    // ── Dock context menu ───────────────────────────────────────────────────────────────────

    private void Chrome_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var cur = _s.Settings.Current;
        var items = new List<(string?, Action?, bool)>();

        foreach (var (edge, label) in new[] { (DockEdge.Top, "Dock to top"), (DockEdge.Bottom, "Dock to bottom"), (DockEdge.Left, "Dock to left"), (DockEdge.Right, "Dock to right") })
        {
            var target = edge;
            items.Add((label, () => _s.Settings.Update(s => s.Edge = target), cur.Edge == edge));
        }
        items.Add((null, null, false));

        foreach (var t in ThemeManager.Instance.Available)
        {
            var id = t.Id;
            items.Add(($"Theme: {t.DisplayName}", () => _s.Settings.Update(s => s.Theme = id), cur.Theme == id));
        }
        items.Add((null, null, false));

        items.Add(("Replace Windows taskbar && Start menu", () => _s.Settings.Update(s => s.ReplaceNativeTaskbar = !s.ReplaceNativeTaskbar), cur.ReplaceNativeTaskbar));
        items.Add(("Start with Windows", () => _s.Settings.Update(s => s.RunAtStartup = !s.RunAtStartup), cur.RunAtStartup));
        items.Add(("Settings…", OpenSettings, false));
        items.Add((null, null, false));
        items.Add(("Exit StartDX", () => Application.Current.Shutdown(), false));

        ShowMenu(Chrome, items.ToArray());
        e.Handled = true;
    }

    private static void OpenSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "StartDX.Settings.exe");
        if (File.Exists(path)) ShellLauncher.Launch(path);
        else Log.Warn($"Settings executable not found at {path}");
    }

    /// <summary>Show a themed context menu on the side of the anchor that faces away from the screen edge.</summary>
    private void ShowMenu(FrameworkElement anchor, params (string? Header, Action? Action, bool Checked)[] entries)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = _s.AppBar.Edge switch
            {
                DockEdge.Bottom => System.Windows.Controls.Primitives.PlacementMode.Top,
                DockEdge.Top => System.Windows.Controls.Primitives.PlacementMode.Bottom,
                DockEdge.Left => System.Windows.Controls.Primitives.PlacementMode.Right,
                _ => System.Windows.Controls.Primitives.PlacementMode.Left,
            },
        };

        foreach (var (header, action, isChecked) in entries)
        {
            if (header is null) { menu.Items.Add(new Separator()); continue; }
            var item = new MenuItem { Header = header, IsCheckable = false, IsChecked = isChecked };
            var act = action;
            item.Click += (_, _) => act?.Invoke();
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
}
