using System.Diagnostics;
using StartDX.Shared;

namespace StartDX.Dock.Infrastructure;

/// <summary>
/// Tiny rolling file logger (%APPDATA%\StartDX\dock.log). A shell component has no console, so
/// when something native misbehaves this is the only breadcrumb trail.
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        Debug.WriteLine($"[StartDX] {level} {message}");
        try
        {
            lock (Gate)
            {
                _path ??= Path.Combine(SettingsStore.DataDirectory, "dock.log");
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > 512 * 1024) File.Move(_path, _path + ".old", overwrite: true);
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never throw */ }
    }
}
