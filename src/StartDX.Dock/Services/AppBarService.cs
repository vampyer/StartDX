using System.Runtime.InteropServices;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Native;
using StartDX.Shared;

namespace StartDX.Dock.Services;

/// <summary>
/// Registers a window as a desktop appbar (SHAppBarMessage) so Windows reserves screen space for it,
/// positions it on any of the four edges, and keeps it at the top of the Z-order.
///
/// This class is deliberately WPF-free: it works purely on an HWND and physical-pixel rectangles.
/// Lifecycle (per Microsoft's appbar contract):
///   ABM_NEW  -> [ABM_QUERYPOS -> adjust rc -> ABM_SETPOS -> SetWindowPos]* -> ABM_REMOVE
/// and the callback message delivers ABN_POSCHANGED / ABN_FULLSCREENAPP / ABN_STATECHANGE.
/// </summary>
internal sealed class AppBarService : IDisposable
{
    private IntPtr _hwnd;
    private uint _callbackMessage;
    private bool _registered;
    private bool _positioning;

    public DockEdge Edge { get; private set; } = DockEdge.Bottom;
    public int ThicknessPx { get; private set; }
    public MonitorInfo Monitor { get; private set; }

    /// <summary>The rectangle Explorer granted us, in physical screen pixels.</summary>
    public RECT Bounds { get; private set; }

    public bool IsRegistered => _registered;
    public bool FullscreenAppActive { get; private set; }

    /// <summary>
    /// When true we stay topmost even while a full-screen app is active (ABN_FULLSCREENAPP). When false we
    /// behave like the stock taskbar and drop to the bottom of the Z-order until the app closes.
    /// </summary>
    public bool KeepOnTopOverFullscreen { get; set; } = true;

    /// <summary>
    /// Optional wrapper executed around every SHAppBarMessage call. When we host our own Shell_TrayWnd
    /// (takeover mode) SHAppBarMessage would otherwise be delivered to *us*; the wrapper temporarily
    /// makes Explorer's tray window the first match for FindWindow("Shell_TrayWnd").
    /// </summary>
    public Func<IDisposable?>? RouteScope { get; set; }

    /// <summary>ABN_POSCHANGED etc.: the host should call <see cref="Dock"/> again.</summary>
    public event Action? LayoutInvalidated;
    public event Action<bool>? FullscreenChanged;

    private uint Send(uint message, ref APPBARDATA abd)
    {
        using (RouteScope?.Invoke())
            return Shell32.SHAppBarMessage(message, ref abd);
    }

    private APPBARDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
        uCallbackMessage = _callbackMessage,
    };

    // ── Registration ────────────────────────────────────────────────────────────────────────

    public bool Register(IntPtr hwnd)
    {
        if (_registered) return true;
        _hwnd = hwnd;
        _callbackMessage = User32.RegisterWindowMessage("StartDX.AppBarCallback");

        var abd = NewData();
        _registered = Send(ABM.NEW, ref abd) != 0;
        if (!_registered) Log.Warn("ABM_NEW failed - the window is not an appbar.");
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        var abd = NewData();
        Send(ABM.REMOVE, ref abd);   // releases the reserved work area
        _registered = false;
    }

    // ── Positioning ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Docks to <paramref name="edge"/> of <paramref name="monitor"/> with the given thickness (physical px).
    /// Runs the canonical QUERYPOS/SETPOS negotiation and then moves the window.
    /// </summary>
    public RECT Dock(DockEdge edge, MonitorInfo monitor, int thicknessPx)
    {
        Edge = edge;
        Monitor = monitor;
        ThicknessPx = thicknessPx;

        var abd = NewData();
        abd.uEdge = (uint)edge;
        abd.rc = monitor.Bounds;         // start from the FULL monitor rect (not the work area)

        _positioning = true;
        try
        {
            if (_registered)
            {
                // 1) Ask Explorer where we may sit: it trims rc so we don't overlap other appbars.
                Send(ABM.QUERYPOS, ref abd);
            }

            // 2) Claim exactly `thickness` pixels along the chosen edge of whatever rc remains.
            var rc = abd.rc;
            switch (edge)
            {
                case DockEdge.Left: rc.Right = rc.Left + thicknessPx; break;
                case DockEdge.Right: rc.Left = rc.Right - thicknessPx; break;
                case DockEdge.Top: rc.Bottom = rc.Top + thicknessPx; break;
                default: rc.Top = rc.Bottom - thicknessPx; break;
            }
            abd.rc = rc;

            // 3) Commit. Explorer shrinks every maximised window's work area accordingly.
            if (_registered) Send(ABM.SETPOS, ref abd);

            Bounds = abd.rc;

            // 4) Physically place the window and pin it to the top of the topmost band.
            User32.SetWindowPos(_hwnd, User32.HWND_TOPMOST,
                Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height,
                SWP.NOACTIVATE | SWP.SHOWWINDOW);
        }
        finally { _positioning = false; }

        Log.Info($"Docked {edge} on {monitor.DeviceName}: {Bounds}");
        return Bounds;
    }

    /// <summary>Re-assert HWND_TOPMOST if anything has slipped in front of us.</summary>
    public void ReassertTopmost()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (FullscreenAppActive && !KeepOnTopOverFullscreen) return;
        // GW_HWNDPREV == 0 means nothing is above us; skip the call to avoid needless z-order churn.
        if (User32.GetWindow(_hwnd, GW.HWNDPREV) == IntPtr.Zero) return;
        User32.SetWindowPos(_hwnd, User32.HWND_TOPMOST, 0, 0, 0, 0, SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE);
    }

    // ── Window-message plumbing (call from the HwndSource hook) ─────────────────────────────

    public void OnWndProc(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (!_registered) return;

        if (msg == _callbackMessage)
        {
            switch ((int)wParam)
            {
                case ABN.POSCHANGED:
                    if (!_positioning) LayoutInvalidated?.Invoke();
                    break;

                case ABN.FULLSCREENAPP:
                    FullscreenAppActive = lParam != IntPtr.Zero;
                    ApplyFullscreenPolicy();
                    FullscreenChanged?.Invoke(FullscreenAppActive);
                    break;

                case ABN.STATECHANGE:
                    ReassertTopmost();
                    break;
            }
            return;
        }

        switch (msg)
        {
            case WM.ACTIVATE:
                var a = NewData();
                Send(ABM.ACTIVATE, ref a);
                break;
            case WM.WINDOWPOSCHANGED:
                var p = NewData();
                Send(ABM.WINDOWPOSCHANGED, ref p);
                break;
        }
    }

    private void ApplyFullscreenPolicy()
    {
        if (FullscreenAppActive && !KeepOnTopOverFullscreen)
        {
            // Mimic the stock taskbar: hide behind the full-screen app.
            User32.SetWindowPos(_hwnd, User32.HWND_BOTTOM, 0, 0, 0, 0, SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE);
        }
        else
        {
            User32.SetWindowPos(_hwnd, User32.HWND_TOPMOST, 0, 0, 0, 0, SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE);
        }
    }

    public void Dispose() => Unregister();
}
