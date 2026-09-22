using System.Runtime.InteropServices;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Native;

namespace StartDX.Dock.Services;

/// <summary>
/// A hidden top-level window of class "Shell_TrayWnd" - the address every application uses when it calls
/// Shell_NotifyIcon. Receives the WM_COPYDATA tray protocol and surfaces NIM_ADD / NIM_MODIFY / NIM_DELETE /
/// NIM_SETVERSION as managed events.
///
/// Two ownership modes:
///  * Exclusive  - Explorer's taskbar is not running (shell replacement / crashed shell): we simply are the tray.
///  * Take-over  - Explorer is running. We create our window ABOVE Explorer's in the Z-order (so FindWindow
///                 returns ours first), push Explorer's tray window to HWND_BOTTOM, and broadcast "TaskbarCreated"
///                 so well-behaved apps re-register their icons with us. On Stop() everything is reversed and the
///                 broadcast is repeated so apps re-register with Explorer.
///
/// Consequence of take-over: SHAppBarMessage (which uses FindWindow("Shell_TrayWnd") too) would be delivered to
/// this window. <see cref="RouteToExplorer"/> temporarily reorders the two windows so OUR appbar calls still reach
/// Explorer. Appbar traffic from *third-party* programs (dwData==0) is not brokered - see README "Known limits".
/// </summary>
internal sealed class ShellTrayHost : IDisposable
{
    public const string ClassName = "Shell_TrayWnd";
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;

    private WndProcDelegate? _proc;           // must stay rooted while the native class exists
    private IntPtr _hwnd;
    private IntPtr _hInstance;
    private IntPtr _explorerTray;             // only set in take-over mode
    private bool _classRegistered;

    public bool IsRunning => _hwnd != IntPtr.Zero;
    public bool IsTakeOver { get; private set; }
    public IntPtr Handle => _hwnd;

    /// <summary>Raised on the UI thread for every tray request. Args: NIM_* code, the NOTIFYICONDATA.</summary>
    public event Action<uint, NOTIFYICONDATA>? TrayMessage;

    /// <summary>Answers Shell_NotifyIconGetRect. Args: hWnd, uID, guid. Returns the icon's screen rect if known.</summary>
    public Func<uint, uint, Guid, RECT?>? IconRectResolver { get; set; }

    /// <summary>First Shell_TrayWnd that is not <paramref name="exclude"/> (i.e. Explorer's).</summary>
    public static IntPtr FindExplorerTray(IntPtr exclude)
    {
        var h = IntPtr.Zero;
        while ((h = User32.FindWindowEx(IntPtr.Zero, h, ClassName, null)) != IntPtr.Zero)
            if (h != exclude) return h;
        return IntPtr.Zero;
    }

    public bool ExplorerTrayPresent => FindExplorerTray(_hwnd) != IntPtr.Zero;

    // ── Start / Stop ────────────────────────────────────────────────────────────────────────

    public bool Start(bool takeOver)
    {
        if (IsRunning) return true;

        var explorer = FindExplorerTray(IntPtr.Zero);
        if (explorer != IntPtr.Zero && !takeOver)
        {
            Log.Info("Explorer already owns Shell_TrayWnd; tray host not started (take-over not requested).");
            return false;
        }

        _hInstance = Kernel32.GetModuleHandle(null);
        _proc = WndProc;

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = _hInstance,
            lpszClassName = ClassName,
        };

        if (User32.RegisterClassEx(ref wc) == 0)
        {
            var err = Marshal.GetLastWin32Error();
            if (err != ERROR_CLASS_ALREADY_EXISTS)
            {
                Log.Error($"RegisterClassEx({ClassName}) failed: {err}");
                _proc = null;
                return false;
            }
        }
        _classRegistered = true;

        _hwnd = User32.CreateWindowEx((uint)(WSEX.TOPMOST | WSEX.TOOLWINDOW), ClassName, "",
            WS.POPUP | WS.CLIPCHILDREN | WS.CLIPSIBLINGS, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, _hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            Log.Error($"CreateWindowEx({ClassName}) failed: {Marshal.GetLastWin32Error()}");
            Cleanup();
            return false;
        }

        // Tray apps running at another integrity level must still be able to reach us.
        User32.ChangeWindowMessageFilterEx(_hwnd, WM.COPYDATA, User32.MSGFLT_ALLOW, IntPtr.Zero);

        IsTakeOver = explorer != IntPtr.Zero;
        if (IsTakeOver)
        {
            _explorerTray = explorer;
            // Explorer's window goes to the very bottom; ours is topmost => FindWindow finds ours first.
            User32.SetWindowPos(_explorerTray, User32.HWND_BOTTOM, 0, 0, 0, 0, SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE);
        }
        User32.SetWindowPos(_hwnd, User32.HWND_TOPMOST, 0, 0, 0, 0, SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE);

        BroadcastTaskbarCreated();
        Log.Info($"Shell_TrayWnd host started (takeOver={IsTakeOver}).");
        return true;
    }

    public void Stop()
    {
        if (!IsRunning) return;

        var hadExplorer = IsTakeOver && _explorerTray != IntPtr.Zero && User32.IsWindow(_explorerTray);
        Cleanup();

        if (hadExplorer)
        {
            // Give Explorer's window its normal always-on-top band back.
            User32.SetWindowPos(_explorerTray, User32.HWND_TOPMOST, 0, 0, 0, 0, SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE);
        }
        _explorerTray = IntPtr.Zero;
        IsTakeOver = false;

        // Apps re-add their icons to whoever now owns Shell_TrayWnd (Explorer).
        BroadcastTaskbarCreated();
        Log.Info("Shell_TrayWnd host stopped.");
    }

    private void Cleanup()
    {
        if (_hwnd != IntPtr.Zero) { User32.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        if (_classRegistered) { User32.UnregisterClass(ClassName, _hInstance); _classRegistered = false; }
        _proc = null;
    }

    private static void BroadcastTaskbarCreated()
    {
        var msg = User32.RegisterWindowMessage("TaskbarCreated");
        User32.SendNotifyMessage(User32.HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
    }

    // ── Appbar routing ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a scope during which FindWindow("Shell_TrayWnd") resolves to Explorer's window, so a
    /// SHAppBarMessage issued inside it reaches Explorer instead of this host. Null when not needed.
    /// </summary>
    public IDisposable? RouteToExplorer()
    {
        if (!IsRunning || !IsTakeOver || _explorerTray == IntPtr.Zero || !User32.IsWindow(_explorerTray)) return null;

        const SWP Flags = SWP.NOMOVE | SWP.NOSIZE | SWP.NOACTIVATE;
        User32.SetWindowPos(_hwnd, User32.HWND_BOTTOM, 0, 0, 0, 0, Flags);
        User32.SetWindowPos(_explorerTray, User32.HWND_TOPMOST, 0, 0, 0, 0, Flags);

        return new Scope(() =>
        {
            User32.SetWindowPos(_explorerTray, User32.HWND_BOTTOM, 0, 0, 0, 0, Flags);
            if (_hwnd != IntPtr.Zero)
                User32.SetWindowPos(_hwnd, User32.HWND_TOPMOST, 0, 0, 0, 0, Flags);
        });
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        private Action? _action = onDispose;
        public void Dispose() { _action?.Invoke(); _action = null; }
    }

    // ── Window procedure ────────────────────────────────────────────────────────────────────

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM.COPYDATA)
        {
            try { return HandleCopyData(lParam); }
            catch (Exception ex)
            {
                // Never let a managed exception unwind into user32.
                Log.Error("Shell_TrayWnd WM_COPYDATA handler failed", ex);
                return IntPtr.Zero;
            }
        }
        return User32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private IntPtr HandleCopyData(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero) return IntPtr.Zero;
        var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);

        return cds.dwData.ToInt64() switch
        {
            TNM.NOTIFYICON => HandleNotifyIcon(cds),
            TNM.ICONIDENTIFIER => HandleIconIdentifier(cds),
            _ => IntPtr.Zero,   // TNM.APPBAR and unknown: not brokered (returns "failed" to the caller)
        };
    }

    private unsafe IntPtr HandleNotifyIcon(COPYDATASTRUCT cds)
    {
        // Header (8) + NOTIFYICONDATA up to and including hIcon (24) is the minimum we can interpret.
        const int MinBytes = 8 + 24;
        var full = Marshal.SizeOf<SHELLTRAYDATA>();
        if (cds.lpData == IntPtr.Zero || cds.cbData < MinBytes) return IntPtr.Zero;

        // Any process can WM_COPYDATA us: never trust cbData. Copy into a zeroed, correctly-sized buffer so
        // shorter (older-cbSize) payloads and hostile ones can neither over-read nor leave garbage.
        var copy = Math.Min(cds.cbData, full);
        var buffer = Marshal.AllocHGlobal(full);
        try
        {
            new Span<byte>((void*)buffer, full).Clear();
            Buffer.MemoryCopy((void*)cds.lpData, (void*)buffer, full, copy);
            var data = Marshal.PtrToStructure<SHELLTRAYDATA>(buffer);
            if (data.dwMessage > NIM.SETVERSION) return IntPtr.Zero;

            TrayMessage?.Invoke(data.dwMessage, data.nid);
            return (IntPtr)1;   // TRUE - Shell_NotifyIcon returns this to the caller
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private IntPtr HandleIconIdentifier(COPYDATASTRUCT cds)
    {
        if (cds.lpData == IntPtr.Zero || cds.cbData < Marshal.SizeOf<WINNOTIFYICONIDENTIFIER>()) return IntPtr.Zero;
        var id = Marshal.PtrToStructure<WINNOTIFYICONIDENTIFIER>(cds.lpData);
        if (IconRectResolver?.Invoke(id.hWnd, id.uID, id.guidItem) is not { } r) return IntPtr.Zero;

        // The LRESULT packs two 16-bit coordinates; dwMessage selects which corner (1 = top-left, 2 = bottom-right).
        return id.dwMessage == 1 ? Win32Ex.MakeLong(r.Left, r.Top) : Win32Ex.MakeLong(r.Right, r.Bottom);
    }

    public void Dispose() => Stop();
}
