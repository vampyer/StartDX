using Microsoft.Win32;
using StartDX.Dock.Infrastructure;

namespace StartDX.Dock.Services;

/// <summary>
/// "Start with Windows" via the per-user Run key: HKCU\Software\Microsoft\Windows\CurrentVersion\Run\StartDX.
/// Per-user means no elevation and no effect on other accounts.
///
/// Windows has a second switch: Task Manager > Startup apps writes HKCU\...\Explorer\StartupApproved\Run\StartDX
/// (first byte odd = disabled). We honour it: an entry the user disabled there is reported as
/// <see cref="StartupState.DisabledInTaskManager"/> so the app never silently fights the user's choice, and enabling
/// clears that flag so a re-enable from our UI actually takes effect.
/// </summary>
internal enum StartupState { NotRegistered, Enabled, DisabledInTaskManager }

internal static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "StartDX";
    public const string AutostartArgument = "--autostart";

    /// <summary>The exe to register: the real single-file exe (or dev build), never a temp extraction path.</summary>
    public static string? ExePath => Environment.ProcessPath;

    /// <summary>The exact command line Windows will run at sign-in.</summary>
    public static string? CommandLine => ExePath is { } p ? $"\"{p}\" {AutostartArgument}" : null;

    public static StartupState GetState()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        if (run?.GetValue(ValueName) is null) return StartupState.NotRegistered;

        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
        if (approved?.GetValue(ValueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1)
            return StartupState.DisabledInTaskManager;
        return StartupState.Enabled;
    }

    /// <summary>True if the stored command no longer points at the exe we are running (moved / rebuilt elsewhere).</summary>
    public static bool IsStale()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return run?.GetValue(ValueName) is string current && !string.Equals(current, CommandLine, StringComparison.OrdinalIgnoreCase);
    }

    public static void Register()
    {
        if (CommandLine is not { } command) throw new InvalidOperationException("The executable path is unknown.");

        using (var run = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            run.SetValue(ValueName, command, RegistryValueKind.String);

        // Re-enabling from our UI must override an earlier "Disable" in Task Manager.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        Log.Info($"Start with Windows enabled: {command}");
    }

    public static void Unregister()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
        using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true))
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        Log.Info("Start with Windows disabled.");
    }
}
