using System.Runtime.InteropServices;
using System.Text;

namespace StartDX.Dock.Native;

internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
internal delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);
internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WNDCLASSEX
{
    public uint cbSize;
    public uint style;
    public IntPtr lpfnWndProc;      // from Marshal.GetFunctionPointerForDelegate; keep the delegate rooted!
    public int cbClsExtra;
    public int cbWndExtra;
    public IntPtr hInstance;
    public IntPtr hIcon;
    public IntPtr hCursor;
    public IntPtr hbrBackground;
    public string? lpszMenuName;
    public string lpszClassName;
    public IntPtr hIconSm;
}

/// <summary>user32.dll - windowing, Z-order, monitors, hooks, messaging.</summary>
internal static class User32
{
    private const string Dll = "user32.dll";

    // ── Z-order sentinels for SetWindowPos(hWndInsertAfter) ─────────────────────────────────
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public static readonly IntPtr HWND_BOTTOM = new(1);
    public static readonly IntPtr HWND_TOPMOST = new(-1);      // above every non-topmost window, permanently
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    // ── Positioning / Z-order ───────────────────────────────────────────────────────────────
    /// <summary>
    /// Sets size, position and Z-order in one atomic call. Together with WS_EX_TOPMOST this is what
    /// keeps the dock above everything: pass <see cref="HWND_TOPMOST"/> and <see cref="SWP.NOACTIVATE"/>.
    /// </summary>
    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, SWP uFlags);

    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport(Dll)] public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool BringWindowToTop(IntPtr hWnd);

    // ── Foreground handling ─────────────────────────────────────────────────────────────────
    [DllImport(Dll)] public static extern IntPtr GetForegroundWindow();
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool AllowSetForegroundWindow(uint dwProcessId);
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);
    [DllImport(Dll)] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // ── Window info ─────────────────────────────────────────────────────────────────────────
    [DllImport(Dll, EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport(Dll, EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport(Dll, EntryPoint = "GetClassLongPtrW")] public static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);
    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")] public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")] public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);
    [DllImport(Dll)] public static extern uint GetDpiForWindow(IntPtr hWnd);           // Win10 1607+
    [DllImport(Dll)] public static extern int GetSystemMetrics(int nIndex);

    // ── Monitors / cursor ───────────────────────────────────────────────────────────────────
    public const uint MONITOR_DEFAULTTONULL = 0, MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport(Dll)] public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport(Dll)] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetCursorPos(out POINT lpPoint);

    // ── Messaging ───────────────────────────────────────────────────────────────────────────
    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")] public static extern uint RegisterWindowMessage(string lpString);
    [DllImport(Dll, EntryPoint = "PostMessageW")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport(Dll, EntryPoint = "SendNotifyMessageW")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SendNotifyMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport(Dll, EntryPoint = "SendMessageTimeoutW")] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
    public const uint SMTO_ABORTIFHUNG = 0x0002, SMTO_BLOCK = 0x0001;

    /// <summary>Lets a lower-integrity sender reach us (tray apps running elevated/low can send WM_COPYDATA).</summary>
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr pChangeFilterStruct);
    public const uint MSGFLT_ALLOW = 1;

    // ── Native window class (used by the Shell_TrayWnd host) ────────────────────────────────
    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegisterClassExW")] public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);
    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "UnregisterClassW")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);
    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
    public static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
    [DllImport(Dll, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport(Dll, EntryPoint = "DefWindowProcW")] public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ── Hooks ───────────────────────────────────────────────────────────────────────────────
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017,
        EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003,
        EVENT_OBJECT_NAMECHANGE = 0x800C, EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;
    public const int OBJID_WINDOW = 0;

    [DllImport(Dll)] public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    public const int WH_KEYBOARD_LL = 13;
    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetWindowsHookExW")] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport(Dll, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport(Dll)] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport(Dll)] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    public const uint KEYEVENTF_KEYUP = 0x0002;
    [DllImport(Dll)] public static extern short GetAsyncKeyState(int vKey);

    // ── Keyboard layouts (input-language indicator) ─────────────────────────────────────────
    [DllImport(Dll)] public static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport(Dll)] public static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);
    public const byte VK_SPACE = 0x20;

    // ── Icons / misc ────────────────────────────────────────────────────────────────────────
    [DllImport(Dll, SetLastError = true)] public static extern IntPtr CopyIcon(IntPtr hIcon);
    [DllImport(Dll, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool DestroyIcon(IntPtr hIcon);

    public const uint SPI_GETWORKAREA = 0x0030;
    [DllImport(Dll, EntryPoint = "SystemParametersInfoW")][return: MarshalAs(UnmanagedType.Bool)] public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    // ── Window composition (undocumented but stable since Win10; used by acrylic/blur backdrops) ─
    [DllImport(Dll)] public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
}

internal enum AccentState
{
    Disabled = 0,
    EnableGradient = 1,
    EnableTransparentGradient = 2,
    EnableBlurBehind = 3,
    EnableAcrylicBlurBehind = 4,
}

[StructLayout(LayoutKind.Sequential)]
internal struct AccentPolicy
{
    public AccentState AccentState;
    public int AccentFlags;
    public uint GradientColor;   // 0xAABBGGRR
    public int AnimationId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowCompositionAttributeData
{
    public int Attribute;        // 19 = WCA_ACCENT_POLICY
    public IntPtr Data;
    public int SizeOfData;
}
