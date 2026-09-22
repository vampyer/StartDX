using System.Text.Json;
using System.Text.Json.Serialization;

namespace StartDX.Shared;

/// <summary>Screen edge. Numeric values intentionally match the Win32 ABE_* constants.</summary>
public enum DockEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

public sealed class PinnedApp
{
    public string Name { get; set; } = "";

    /// <summary>A file path, or a shell:AppsFolder\... parsing name. Both are ShellExecute-able.</summary>
    public string Target { get; set; } = "";
}

/// <summary>Everything persisted to %APPDATA%\StartDX\settings.json and mirrored over IPC.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = ThemeCatalog.Default;
    public DockEdge Edge { get; set; } = DockEdge.Bottom;

    /// <summary>Dock thickness in device-independent pixels.</summary>
    public int ThicknessDip { get; set; } = 56;

    /// <summary>GDI device name of the monitor to dock on (e.g. \\.\DISPLAY2). Null = primary.</summary>
    public string? MonitorDevice { get; set; }

    /// <summary>Keep the dock above borderless full-screen apps (see ABN_FULLSCREENAPP handling).</summary>
    public bool AlwaysOnTopOverFullscreen { get; set; } = true;

    /// <summary>
    /// Replace the Windows shell UI: hide Explorer's taskbar, take over the Shell_TrayWnd notification-area host, and route
    /// the Win key / Ctrl+Esc to StartDX instead of the stock Start menu. Fully reversible - everything is undone on exit,
    /// and <c>StartDX.Dock.exe --restore-taskbar</c> repairs a session that was killed.
    /// </summary>
    public bool ReplaceNativeTaskbar { get; set; } = true;

    public bool WinKeyOpensStartMenu { get; set; } = true;
    public bool ShowTaskLabels { get; set; } = true;

    /// <summary>
    /// Launch the dock when the user signs in (per-user HKCU Run entry - no admin rights needed). Off by default; the dock
    /// keeps the registry in step with this value, and turns it back off if the user disables the entry in Task Manager.
    /// </summary>
    public bool RunAtStartup { get; set; } = false;

    public List<PinnedApp> Pinned { get; set; } = [];

    public const int MinThickness = 36;
    public const int MaxThickness = 160;

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(
        JsonSerializer.Serialize(this, SettingsJson.Options), SettingsJson.Options)!;

    /// <summary>Clamp/normalise anything that arrived from disk or the pipe.</summary>
    public void Sanitize()
    {
        Theme = ThemeCatalog.Normalize(Theme);
        if (!Enum.IsDefined(Edge)) Edge = DockEdge.Bottom;
        ThicknessDip = Math.Clamp(ThicknessDip, MinThickness, MaxThickness);
        Pinned ??= [];
        Pinned.RemoveAll(p => string.IsNullOrWhiteSpace(p.Target));
    }
}

/// <summary>
/// A sparse update pushed by the settings app: only non-null members are applied. Pins are
/// deliberately absent - only the dock mutates them.
/// </summary>
public sealed class SettingsPatch
{
    public string? Theme { get; set; }
    public DockEdge? Edge { get; set; }
    public int? ThicknessDip { get; set; }
    public string? MonitorDevice { get; set; }
    public bool? ClearMonitorDevice { get; set; }
    public bool? AlwaysOnTopOverFullscreen { get; set; }
    public bool? ReplaceNativeTaskbar { get; set; }
    public bool? WinKeyOpensStartMenu { get; set; }
    public bool? ShowTaskLabels { get; set; }
    public bool? RunAtStartup { get; set; }

    public void ApplyTo(AppSettings s)
    {
        if (Theme is not null) s.Theme = Theme;
        if (Edge is not null) s.Edge = Edge.Value;
        if (ThicknessDip is not null) s.ThicknessDip = ThicknessDip.Value;
        if (ClearMonitorDevice == true) s.MonitorDevice = null;
        else if (MonitorDevice is not null) s.MonitorDevice = MonitorDevice;
        if (AlwaysOnTopOverFullscreen is not null) s.AlwaysOnTopOverFullscreen = AlwaysOnTopOverFullscreen.Value;
        if (ReplaceNativeTaskbar is not null) s.ReplaceNativeTaskbar = ReplaceNativeTaskbar.Value;
        if (WinKeyOpensStartMenu is not null) s.WinKeyOpensStartMenu = WinKeyOpensStartMenu.Value;
        if (ShowTaskLabels is not null) s.ShowTaskLabels = ShowTaskLabels.Value;
        if (RunAtStartup is not null) s.RunAtStartup = RunAtStartup.Value;
    }
}

public static class SettingsJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Reads/writes settings atomically. Set STARTDX_DATA_DIR to redirect (tests, portable installs).</summary>
public static class SettingsStore
{
    public static string DataDirectory
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("STARTDX_DATA_DIR");
            var dir = !string.IsNullOrWhiteSpace(overridePath)
                ? overridePath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StartDX");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), SettingsJson.Options);
                if (s is not null) { s.Sanitize(); return s; }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or locked file: fall through to defaults rather than failing to start the shell.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        var tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, SettingsJson.Options));
        File.Move(tmp, SettingsPath, overwrite: true); // atomic replace: a crash never leaves half a file
    }
}
