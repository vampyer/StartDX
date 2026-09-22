using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Native;

namespace StartDX.Dock.Theming;

public enum BackdropRole
{
    /// <summary>The docked bar: always square (it is flush with a screen edge).</summary>
    Dock,
    /// <summary>Start menu / notification centre: honours the theme's corner style.</summary>
    Flyout,
}

/// <summary>
/// Applies the *window-level* half of a theme - blur, tint, corner style, border colour - using the Desktop Window
/// Manager. WPF cannot blur what is behind its own window, so this is done natively:
///   * Acrylic / Blur : SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND / BLURBEHIND) - unlike the
///                      DWM system-backdrop, it stays blurred while the window is inactive (a dock never has focus).
///   * Mica           : DWMWA_SYSTEMBACKDROP_TYPE (Windows 11 22H2+).
/// Every registered window is re-styled automatically when <see cref="ThemeManager.ThemeChanged"/> fires.
/// </summary>
internal static class WindowBackdrop
{
    private static readonly List<(WeakReference<Window> Window, BackdropRole Role)> Registered = new();
    private static readonly HashSet<IntPtr> _micaApplied = new();
    private static bool _subscribed;

    public static void Register(Window window, BackdropRole role)
    {
        if (!_subscribed)
        {
            ThemeManager.Instance.ThemeChanged += (_, _) => ReapplyAll();
            _subscribed = true;
        }

        Registered.Add((new WeakReference<Window>(window), role));
        window.Closed += (_, _) => Registered.RemoveAll(r => !r.Window.TryGetTarget(out var w) || w == window);

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Apply(window, role);
        else window.SourceInitialized += (_, _) => Apply(window, role);

        // Style/frame changes made while the window is being created (FRAMECHANGED, ex-style edits) can reset the accent
        // policy, so apply once more after the first real render.
        window.ContentRendered += (_, _) => Apply(window, role);
    }

    private static void ReapplyAll()
    {
        foreach (var (weak, role) in Registered.ToList())
            if (weak.TryGetTarget(out var w) && new WindowInteropHelper(w).Handle != IntPtr.Zero)
                Apply(w, role);
    }

    private static void Apply(Window window, BackdropRole role)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var tm = ThemeManager.Instance;
        var kind = tm.Get(ThemeKeys.BackdropKind, "None");
        var tint = tm.Get(ThemeKeys.BackdropTint, Color.FromArgb(0xCC, 0x10, 0x10, 0x14));
        var dark = tm.Get(ThemeKeys.BackdropDark, true);
        var corners = role == BackdropRole.Dock ? "Square" : tm.Get(ThemeKeys.BackdropCorners, "Round");
        var border = tm.Get(ThemeKeys.BackdropBorderColor, Colors.Transparent);

        // 1) Let WPF's D3D surface carry per-pixel alpha, and let DWM composite it over the blur.
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        Dwm.DwmExtendFrameIntoClientArea(hwnd, ref margins);

        // 2) Blur / acrylic / mica. The two mechanisms are mutually exclusive: touching DWMWA_SYSTEMBACKDROP_TYPE while
        //    the legacy accent is active cancels the blur on Windows 11, so it is only ever set for Mica (and reset to
        //    AUTO when leaving Mica, so a theme switch away from it cleans up after itself).
        switch (kind)
        {
            case "Acrylic": SetAccent(hwnd, AccentState.EnableAcrylicBlurBehind, tint); break;
            case "Blur": SetAccent(hwnd, AccentState.EnableBlurBehind, tint); break;
            case "Mica": SetAccent(hwnd, AccentState.Disabled, tint); break;
            default: SetAccent(hwnd, AccentState.Disabled, tint); break;
        }
        if (kind == "Mica") { _micaApplied.Add(hwnd); Dwm.SetInt(hwnd, Dwm.DWMWA_SYSTEMBACKDROP_TYPE, Dwm.DWMSBT_MAINWINDOW); }
        else if (_micaApplied.Remove(hwnd)) Dwm.SetInt(hwnd, Dwm.DWMWA_SYSTEMBACKDROP_TYPE, Dwm.DWMSBT_AUTO);

        // 3) Window chrome.
        Dwm.SetInt(hwnd, Dwm.DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
        Dwm.SetInt(hwnd, Dwm.DWMWA_WINDOW_CORNER_PREFERENCE, corners switch
        {
            "Round" => Dwm.DWMWCP_ROUND,
            "RoundSmall" => Dwm.DWMWCP_ROUNDSMALL,
            _ => Dwm.DWMWCP_DONOTROUND,
        });
        Dwm.SetUInt(hwnd, Dwm.DWMWA_BORDER_COLOR, border.A == 0
            ? Dwm.DWMWA_COLOR_NONE
            : (uint)(border.B << 16 | border.G << 8 | border.R));            // COLORREF = 0x00BBGGRR
    }

    private static void SetAccent(IntPtr hwnd, AccentState state, Color tint)
    {
        var policy = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = state == AccentState.EnableBlurBehind ? 0x20 | 0x40 | 0x80 | 0x100 : 0,
            GradientColor = (uint)(tint.A << 24 | tint.B << 16 | tint.G << 8 | tint.R),   // 0xAABBGGRR
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = ptr, SizeOfData = size };
            var ok = User32.SetWindowCompositionAttribute(hwnd, ref data);
            Log.Info($"SetWindowCompositionAttribute(0x{hwnd:X}, {state}, tint=#{tint.A:X2}{tint.R:X2}{tint.G:X2}{tint.B:X2}) -> {ok}");
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
