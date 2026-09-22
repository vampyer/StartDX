using System.Runtime.InteropServices;

namespace StartDX.Dock.Native;

// ═════════════════════════════════════════════════════════════════════════════════════════════
//  1) APPBAR  -  SHAppBarMessage
//     https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shappbarmessage
// ═════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>APPBARDATA. On x64 the Sequential layout inserts the 4 bytes of padding after cbSize for us (48 bytes total).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct APPBARDATA
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uCallbackMessage;   // message Explorer posts to hWnd for ABN_* notifications
    public uint uEdge;              // ABE_*
    public RECT rc;                 // in/out: screen coordinates (physical pixels)
    public IntPtr lParam;           // ABM_SETAUTOHIDEBAR / ABM_SETSTATE payload
}

/// <summary>ABM_* command ids (first argument to SHAppBarMessage).</summary>
internal static class ABM
{
    public const uint NEW = 0x00000000;                 // register the appbar + callback message
    public const uint REMOVE = 0x00000001;              // unregister (releases the reserved screen area)
    public const uint QUERYPOS = 0x00000002;            // ask where we may sit (Explorer adjusts rc for other bars)
    public const uint SETPOS = 0x00000003;              // commit the rectangle -> shrinks the work area
    public const uint GETSTATE = 0x00000004;            // ABS_AUTOHIDE / ABS_ALWAYSONTOP of the *taskbar*
    public const uint GETTASKBARPOS = 0x00000005;
    public const uint ACTIVATE = 0x00000006;            // call on WM_ACTIVATE
    public const uint GETAUTOHIDEBAR = 0x00000007;
    public const uint SETAUTOHIDEBAR = 0x00000008;
    public const uint WINDOWPOSCHANGED = 0x00000009;    // call on WM_WINDOWPOSCHANGED
    public const uint SETSTATE = 0x0000000A;            // set taskbar autohide / always-on-top
}

/// <summary>ABE_* - edge the appbar is docked to. Same numeric values as <c>StartDX.Shared.DockEdge</c>.</summary>
internal static class ABE
{
    public const uint LEFT = 0, TOP = 1, RIGHT = 2, BOTTOM = 3;
}

/// <summary>ABN_* - notification codes delivered in wParam of the appbar callback message.</summary>
internal static class ABN
{
    public const int STATECHANGE = 0x0000000;   // taskbar autohide / topmost state changed
    public const int POSCHANGED = 0x0000001;    // another bar or the work area changed: re-run QUERYPOS/SETPOS
    public const int FULLSCREENAPP = 0x0000002; // lParam != 0: a full-screen app opened; lParam == 0: it closed
    public const int WINDOWARRANGE = 0x0000003; // lParam != 0 at the start of cascade/tile, 0 at the end
}

/// <summary>ABS_* flags for ABM_GETSTATE / ABM_SETSTATE.</summary>
internal static class ABS
{
    public const uint MANUAL = 0x0, AUTOHIDE = 0x1, ALWAYSONTOP = 0x2;
}

// ═════════════════════════════════════════════════════════════════════════════════════════════
//  2) NOTIFICATION AREA (system tray)  -  the Shell_TrayWnd WM_COPYDATA protocol
//
//  When an application calls Shell_NotifyIcon, shell32 does roughly:
//      hwnd = FindWindow("Shell_TrayWnd");
//      COPYDATASTRUCT { dwData = 1 (TNM_NOTIFYICON), cbData = sizeof(SHELLTRAYDATA), lpData = &SHELLTRAYDATA };
//      SendMessage(hwnd, WM_COPYDATA, senderHwnd, &cds);
//  So a tray host is simply a top-level window of class "Shell_TrayWnd" that answers WM_COPYDATA.
//  Layouts below match the on-the-wire format (handles are 32-bit significant, even from 64-bit apps).
// ═════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>dwData discriminator of the COPYDATASTRUCT sent to Shell_TrayWnd.</summary>
internal static class TNM
{
    public const long APPBAR = 0;            // SHAppBarMessage traffic (shared-memory based; not handled by our host)
    public const long NOTIFYICON = 1;        // Shell_NotifyIcon  -> SHELLTRAYDATA
    public const long ICONIDENTIFIER = 3;    // Shell_NotifyIconGetRect -> WINNOTIFYICONIDENTIFIER
}

/// <summary>NIM_* - the dwMessage of a tray request (Shell_NotifyIcon's first parameter).</summary>
internal static class NIM
{
    public const uint ADD = 0, MODIFY = 1, DELETE = 2, SETFOCUS = 3, SETVERSION = 4;
}

/// <summary>NIF_* - which NOTIFYICONDATA members are valid in this request.</summary>
[Flags]
internal enum NIF : uint
{
    MESSAGE = 0x01,
    ICON = 0x02,
    TIP = 0x04,
    STATE = 0x08,
    INFO = 0x10,
    GUID = 0x20,
    REALTIME = 0x40,
    SHOWTIP = 0x80,
}

/// <summary>NIS_* - dwState bits.</summary>
internal static class NIS
{
    public const uint HIDDEN = 0x1, SHAREDICON = 0x2;
}

/// <summary>NIIF_* - balloon icon kind (low byte of dwInfoFlags).</summary>
internal static class NIIF
{
    public const uint NONE = 0, INFO = 1, WARNING = 2, ERROR = 3, USER = 4, ICON_MASK = 0xF;
}

/// <summary>
/// NOTIFYICONDATAW as marshalled inside SHELLTRAYDATA (964 bytes total with the header).
/// hWnd/hIcon/hBalloonIcon are 32-bit on the wire; use <see cref="Win32Ex.FromHandle32"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NOTIFYICONDATA
{
    public uint cbSize;
    public uint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public uint hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
    public uint dwState;
    public uint dwStateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
    public uint uVersion;                      // union with uTimeout
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
    public uint dwInfoFlags;
    public Guid guidItem;
    public uint hBalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SHELLTRAYDATA
{
    public int dwUnknown;      // signature/magic, ignored
    public uint dwMessage;     // NIM_*
    public NOTIFYICONDATA nid;
}

/// <summary>Payload of the dwData==3 request (Shell_NotifyIconGetRect).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WINNOTIFYICONIDENTIFIER
{
    public int dwMagic;
    public int dwMessage;      // 1 = return top-left, 2 = return bottom-right (packed MAKELONG(x,y) in the LRESULT)
    public int cbSize;
    public int dwPadding;
    public uint hWnd;
    public uint uID;
    public Guid guidItem;
}

// ═════════════════════════════════════════════════════════════════════════════════════════════
//  3) Shell entry points
// ═════════════════════════════════════════════════════════════════════════════════════════════

internal static class Shell32
{
    private const string Dll = "shell32.dll";

    /// <summary>
    /// The one appbar entry point. Returns the message-specific result (non-zero = success for ABM_NEW;
    /// ABM_GETSTATE returns ABS_* flags). Explorer answers it by receiving a WM_COPYDATA on Shell_TrayWnd -
    /// which is why hosting our own Shell_TrayWnd requires routing these calls back to Explorer
    /// (see ShellTrayHost.RouteToExplorer).
    /// </summary>
    [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
    public static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport(Dll, CharSet = CharSet.Unicode, PreserveSig = false, EntryPoint = "SHCreateItemFromParsingName")]
    public static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
}
