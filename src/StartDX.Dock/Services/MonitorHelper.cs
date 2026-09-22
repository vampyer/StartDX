using StartDX.Dock.Native;
using StartDX.Shared;

namespace StartDX.Dock.Services;

/// <summary>Snapshot of one display, all rectangles in physical (virtual-screen) pixels.</summary>
internal readonly record struct MonitorInfo(IntPtr Handle, RECT Bounds, RECT WorkArea, string DeviceName, bool IsPrimary, uint Dpi)
{
    public double Scale => Dpi / 96.0;
}

internal static class MonitorHelper
{
    public static MonitorInfo FromHandle(IntPtr hMonitor)
    {
        var mi = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
        User32.GetMonitorInfo(hMonitor, ref mi);
        uint dpi = 96;
        if (ShCore.GetDpiForMonitor(hMonitor, ShCore.MDT_EFFECTIVE_DPI, out var dx, out _) == 0 && dx != 0) dpi = dx;
        return new MonitorInfo(hMonitor, mi.rcMonitor, mi.rcWork, mi.szDevice ?? "", (mi.dwFlags & 1) != 0, dpi);
    }

    public static MonitorInfo Primary() =>
        FromHandle(User32.MonitorFromPoint(new POINT(0, 0), User32.MONITOR_DEFAULTTOPRIMARY));

    public static MonitorInfo FromPoint(int x, int y) =>
        FromHandle(User32.MonitorFromPoint(new POINT(x, y), User32.MONITOR_DEFAULTTONEAREST));

    public static MonitorInfo FromWindow(IntPtr hwnd) =>
        FromHandle(User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST));

    public static IReadOnlyList<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) =>
        {
            list.Add(FromHandle(h));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Resolve the configured monitor, falling back to primary when it is unplugged.</summary>
    public static MonitorInfo Resolve(string? deviceName)
    {
        if (!string.IsNullOrEmpty(deviceName))
            foreach (var m in All())
                if (string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) return m;
        return Primary();
    }

    /// <summary>The screen edge closest to a point (used for drag-to-dock snapping).</summary>
    public static DockEdge NearestEdge(RECT bounds, int x, int y)
    {
        var left = x - bounds.Left;
        var right = bounds.Right - x;
        var top = y - bounds.Top;
        var bottom = bounds.Bottom - y;
        var min = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        if (min == bottom) return DockEdge.Bottom;
        if (min == top) return DockEdge.Top;
        if (min == left) return DockEdge.Left;
        return DockEdge.Right;
    }
}
