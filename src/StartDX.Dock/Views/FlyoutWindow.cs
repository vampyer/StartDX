using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StartDX.Dock.Native;
using StartDX.Dock.Services;
using StartDX.Dock.Theming;
using StartDX.Shared;

namespace StartDX.Dock.Views;

/// <summary>Where the dock currently is, in physical pixels. Flyouts anchor themselves to this.</summary>
internal readonly record struct DockPlacement(RECT Bounds, DockEdge Edge, MonitorInfo Monitor);

internal enum FlyoutAlign { Start, End }

/// <summary>
/// Shared behaviour of the Start menu and the notification centre: a borderless, topmost, tool-window popup that
/// (a) applies the active theme's backdrop, (b) positions itself flush against the dock on whichever edge/monitor it
/// occupies, (c) slides/fades in using the theme's motion tokens, and (d) dismisses itself when it loses focus.
/// The XAML root of a derived flyout must contain an element named <c>FlyoutRoot</c> (the animated surface).
/// </summary>
public class FlyoutWindow : Window
{
    private DateTime _lastHiddenUtc = DateTime.MinValue;
    private bool _hiding;

    public FlyoutWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = true;
        Topmost = true;
        AllowsTransparency = false;          // required so DWM blur can render behind us
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        WindowBackdrop.Register(this, BackdropRole.Flyout);
        SourceInitialized += (_, _) =>
        {
            // Hide from Alt-Tab / taskbar enumeration.
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = (long)User32.GetWindowLongPtr(hwnd, GWL.EXSTYLE);
            User32.SetWindowLongPtr(hwnd, GWL.EXSTYLE, (IntPtr)(ex | WSEX.TOOLWINDOW));
        };
        Deactivated += (_, _) => HideFlyout();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { HideFlyout(); e.Handled = true; } };
    }

    /// <summary>Raised just before the flyout is shown (refresh content here).</summary>
    protected virtual void OnOpening() { }

    internal void Toggle(DockPlacement placement, FlyoutAlign align)
    {
        if (IsVisible) { HideFlyout(); return; }

        // The click that dismissed us (via Deactivated) is the same click that lands on the dock button: don't reopen.
        if ((DateTime.UtcNow - _lastHiddenUtc).TotalMilliseconds < 250) return;
        ShowAnchored(placement, align);
    }

    internal void ShowAnchored(DockPlacement p, FlyoutAlign align)
    {
        OnOpening();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var scale = p.Monitor.Scale;

        int w = (int)Math.Round(Width * scale);
        int h = (int)Math.Round(Height * scale);
        int gap = (int)Math.Round(8 * scale);
        var mon = p.Monitor.Bounds;
        var dock = p.Bounds;

        // Never taller/wider than the space the dock leaves free.
        h = Math.Min(h, mon.Height - (p.Edge is DockEdge.Top or DockEdge.Bottom ? dock.Height : 0) - 2 * gap);
        w = Math.Min(w, mon.Width - (p.Edge is DockEdge.Left or DockEdge.Right ? dock.Width : 0) - 2 * gap);

        int x, y;
        switch (p.Edge)
        {
            case DockEdge.Top:
                y = dock.Bottom + gap;
                x = align == FlyoutAlign.Start ? dock.Left + gap : dock.Right - w - gap;
                break;
            case DockEdge.Left:
                x = dock.Right + gap;
                y = align == FlyoutAlign.Start ? dock.Top + gap : dock.Bottom - h - gap;
                break;
            case DockEdge.Right:
                x = dock.Left - w - gap;
                y = align == FlyoutAlign.Start ? dock.Top + gap : dock.Bottom - h - gap;
                break;
            default: // Bottom
                y = dock.Top - h - gap;
                x = align == FlyoutAlign.Start ? dock.Left + gap : dock.Right - w - gap;
                break;
        }
        x = Math.Clamp(x, mon.Left, Math.Max(mon.Left, mon.Right - w));
        y = Math.Clamp(y, mon.Top, Math.Max(mon.Top, mon.Bottom - h));

        _hiding = false;
        Opacity = 0;
        User32.SetWindowPos(hwnd, User32.HWND_TOPMOST, x, y, w, h, SWP.SHOWWINDOW);
        Show();
        // If the flyout landed on a monitor with a different DPI, WPF resizes it on WM_DPICHANGED; re-assert our rect.
        Dispatcher.BeginInvoke(() => User32.SetWindowPos(hwnd, User32.HWND_TOPMOST, x, y, w, h, SWP.NOACTIVATE),
            System.Windows.Threading.DispatcherPriority.Background);
        Activate();
        AnimateIn(p.Edge);
    }

    private void AnimateIn(DockEdge edge)
    {
        var tm = ThemeManager.Instance;
        var ms = tm.Get(ThemeKeys.OpenMs, 200.0);
        var slide = tm.Get(ThemeKeys.SlidePx, 16.0);
        var duration = TimeSpan.FromMilliseconds(Math.Max(1, ms));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });

        if (FindName("FlyoutRoot") is not FrameworkElement root) return;
        var t = new TranslateTransform();
        root.RenderTransform = t;

        // Emerge from the dock: offset away from the dock edge, settle to zero.
        var (dx, dy) = edge switch
        {
            DockEdge.Bottom => (0.0, slide),
            DockEdge.Top => (0.0, -slide),
            DockEdge.Left => (-slide, 0.0),
            _ => (slide, 0.0),
        };
        if (dx != 0) t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, duration) { EasingFunction = ease });
        if (dy != 0) t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, duration) { EasingFunction = ease });
    }

    public void HideFlyout()
    {
        if (!IsVisible || _hiding) return;
        _hiding = true;
        _lastHiddenUtc = DateTime.UtcNow;
        Hide();
        _hiding = false;
    }
}
