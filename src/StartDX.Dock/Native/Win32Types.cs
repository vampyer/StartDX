using System.Runtime.InteropServices;

namespace StartDX.Dock.Native;

// ─────────────────────────────────────────────────────────────────────────────────────────────
//  Basic Win32 value types + constants shared by every P/Invoke group.
// ─────────────────────────────────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;

    public RECT(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public override readonly string ToString() => $"[{Left},{Top} - {Right},{Bottom}] ({Width}x{Height})";
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X, Y;
    public POINT(int x, int y) { X = x; Y = y; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int cx, cy;
    public SIZE(int x, int y) { cx = x; cy = y; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct COPYDATASTRUCT
{
    public IntPtr dwData;   // ULONG_PTR
    public int cbData;      // DWORD
    public IntPtr lpData;   // PVOID
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor;   // full monitor rectangle, virtual-screen physical pixels
    public RECT rcWork;      // monitor minus appbars / taskbar
    public uint dwFlags;     // MONITORINFOF_PRIMARY = 1
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDevice;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MARGINS
{
    public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KBDLLHOOKSTRUCT
{
    public uint vkCode, scanCode, flags, time;
    public UIntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAP
{
    public int bmType, bmWidth, bmHeight, bmWidthBytes;
    public ushort bmPlanes, bmBitsPixel;
    public IntPtr bmBits;
}

/// <summary>SetWindowPos flags.</summary>
[Flags]
internal enum SWP : uint
{
    NOSIZE = 0x0001,
    NOMOVE = 0x0002,
    NOZORDER = 0x0004,
    NOREDRAW = 0x0008,
    NOACTIVATE = 0x0010,
    FRAMECHANGED = 0x0020,
    SHOWWINDOW = 0x0040,
    HIDEWINDOW = 0x0080,
    NOCOPYBITS = 0x0100,
    NOOWNERZORDER = 0x0200,
    NOSENDCHANGING = 0x0400,
    ASYNCWINDOWPOS = 0x4000,
}

internal static class SW
{
    public const int HIDE = 0, SHOWNORMAL = 1, SHOWMINIMIZED = 2, MAXIMIZE = 3, SHOWNOACTIVATE = 4,
        SHOW = 5, MINIMIZE = 6, SHOWMINNOACTIVE = 7, SHOWNA = 8, RESTORE = 9;
}

internal static class GWL
{
    public const int STYLE = -16, EXSTYLE = -20;
}

internal static class WS
{
    public const uint POPUP = 0x80000000, VISIBLE = 0x10000000, CLIPSIBLINGS = 0x04000000, CLIPCHILDREN = 0x02000000;
}

internal static class WSEX
{
    public const long TOOLWINDOW = 0x00000080, APPWINDOW = 0x00040000, NOACTIVATE = 0x08000000,
        TOPMOST = 0x00000008, LAYERED = 0x00080000;
}

internal static class GW
{
    public const uint HWNDNEXT = 2, HWNDPREV = 3, OWNER = 4;
}

/// <summary>Window message ids used by this app.</summary>
internal static class WM
{
    public const uint DESTROY = 0x0002, ACTIVATE = 0x0006, CLOSE = 0x0010, DISPLAYCHANGE = 0x007E,
        SETTINGCHANGE = 0x001A, GETICON = 0x007F, COPYDATA = 0x004A, WINDOWPOSCHANGED = 0x0047,
        WINDOWPOSCHANGING = 0x0046, DPICHANGED = 0x02E0, CONTEXTMENU = 0x007B, USER = 0x0400,
        MOUSEMOVE = 0x0200, LBUTTONDOWN = 0x0201, LBUTTONUP = 0x0202, LBUTTONDBLCLK = 0x0203,
        RBUTTONDOWN = 0x0204, RBUTTONUP = 0x0205, RBUTTONDBLCLK = 0x0206,
        KEYDOWN = 0x0100, KEYUP = 0x0101, SYSKEYDOWN = 0x0104, SYSKEYUP = 0x0105;
}

internal static class VK
{
    public const int LWIN = 0x5B, RWIN = 0x5C, UNASSIGNED = 0xE8, ESCAPE = 0x1B, SHIFT = 0x10, CONTROL = 0x11, MENU = 0x12;
}

internal static class Win32Ex
{
    public static int LoWord(nint v) => unchecked((short)((long)v & 0xFFFF));
    public static int HiWord(nint v) => unchecked((short)(((long)v >> 16) & 0xFFFF));
    public static nint MakeLong(int lo, int hi) => unchecked((nint)(((hi & 0xFFFF) << 16) | (lo & 0xFFFF)));

    /// <summary>NOTIFYICONDATA carries 32-bit handle values (handles are 32-bit-significant); sign-extend to IntPtr.</summary>
    public static IntPtr FromHandle32(uint h) => new(unchecked((int)h));
}
