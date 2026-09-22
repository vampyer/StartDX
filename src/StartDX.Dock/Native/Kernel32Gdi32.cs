using System.Runtime.InteropServices;
using System.Text;

namespace StartDX.Dock.Native;

internal static class Kernel32
{
    private const string Dll = "kernel32.dll";
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport(Dll, SetLastError = true)] public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);
    [DllImport(Dll, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool CloseHandle(IntPtr hObject);
    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)] public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);
    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")] public static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport(Dll)] public static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;          // 0 offline, 1 online, 255 unknown
        public byte BatteryFlag;           // bit 8 (128) = no system battery; bit 3 (8) = charging; 255 unknown
        public byte BatteryLifePercent;    // 0-100, 255 unknown
        public byte SystemStatusFlag;      // 1 = battery saver on
        public int BatteryLifeTime;        // seconds remaining, -1 unknown
        public int BatteryFullLifeTime;
    }

    [DllImport(Dll, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    /// <summary>Full image path of a process, or null if it is protected / gone.</summary>
    public static string? GetProcessImagePath(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }
}

internal static class Gdi32
{
    private const string Dll = "gdi32.dll";
    [DllImport(Dll)][return: MarshalAs(UnmanagedType.Bool)] public static extern bool DeleteObject(IntPtr hObject);
    [DllImport(Dll, EntryPoint = "GetObjectW")] public static extern int GetObject(IntPtr hObject, int nCount, ref BITMAP lpObject);
}

internal static class ShCore
{
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    public const int MDT_EFFECTIVE_DPI = 0;
}
