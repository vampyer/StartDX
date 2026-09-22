using System.Runtime.InteropServices;

namespace StartDX.Dock.Native;

/// <summary>dwmapi.dll - Desktop Window Manager attributes (corners, borders, dark mode, cloaking).</summary>
internal static class Dwm
{
    private const string Dll = "dwmapi.dll";

    public const int DWMWA_CLOAKED = 14;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;   // Win11
    public const int DWMWA_BORDER_COLOR = 34;               // Win11
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;        // Win11 22H2+

    public const int DWMWCP_DEFAULT = 0, DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2, DWMWCP_ROUNDSMALL = 3;
    public const int DWMSBT_AUTO = 0, DWMSBT_NONE = 1, DWMSBT_MAINWINDOW = 2 /* Mica */, DWMSBT_TRANSIENTWINDOW = 3 /* Acrylic */, DWMSBT_TABBEDWINDOW = 4;
    public const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF, DWMWA_COLOR_NONE = 0xFFFFFFFE;

    [DllImport(Dll)] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    [DllImport(Dll)] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);
    [DllImport(Dll)] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    [DllImport(Dll)] public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    public static void SetInt(IntPtr hwnd, int attr, int value) => DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int));
    public static void SetUInt(IntPtr hwnd, int attr, uint value) => DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(uint));

    public static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
}
