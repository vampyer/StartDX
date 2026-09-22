using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Native;
using StartDX.Shared;

namespace StartDX.Dock.Services;

/// <summary>
/// Takes Explorer's taskbar out of the picture - and, just as importantly, always puts it back.
///
///  Hide()    1. ABM_SETSTATE + ABS_AUTOHIDE  -> releases the work area the taskbar reserved (the sanctioned way),
///            2. ShowWindow(SW_HIDE) on every Shell_TrayWnd / Shell_SecondaryTrayWnd -> it can no longer slide in when the
///               mouse touches the screen edge (auto-hide alone does not stop that),
///            3. a 1 s keeper re-hides any window Explorer shows again (it does after some shell events).
///  Restore() reverses all of it. The original taskbar state is written to taskbar-backup.json *before* anything is
///  changed, so even a hard kill (Task Manager, power loss) can be repaired: the next start calls
///  <see cref="RecoverFromCrash"/>, and <c>StartDX.Dock.exe --restore-taskbar</c> runs <see cref="EmergencyRestore"/> by hand.
/// </summary>
internal sealed class NativeTaskbarService : IDisposable
{
    private const string PrimaryClass = ShellTrayHost.ClassName;          // "Shell_TrayWnd"
    private const string SecondaryClass = "Shell_SecondaryTrayWnd";       // one per additional monitor

    private readonly ShellTrayHost _trayHost;
    private readonly DispatcherTimer _keeper;
    private uint? _originalState;

    public NativeTaskbarService(ShellTrayHost trayHost)
    {
        _trayHost = trayHost;
        _keeper = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => EnsureHidden(), Dispatcher.CurrentDispatcher);
    }

    public bool IsHidden { get; private set; }

    private static string BackupPath => Path.Combine(SettingsStore.DataDirectory, "taskbar-backup.json");

    private static APPBARDATA NewData(IntPtr hwnd) => new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };

    /// <summary>Every taskbar window Explorer owns (primary + one per extra monitor), never our own tray host.</summary>
    private static IEnumerable<IntPtr> EnumerateExplorerTrayWindows(IntPtr exclude)
    {
        foreach (var cls in new[] { PrimaryClass, SecondaryClass })
        {
            var h = IntPtr.Zero;
            while ((h = User32.FindWindowEx(IntPtr.Zero, h, cls, null)) != IntPtr.Zero)
                if (h != exclude) yield return h;
        }
    }

    // ── Hide / restore ──────────────────────────────────────────────────────────────────────

    public void Hide()
    {
        var explorer = ShellTrayHost.FindExplorerTray(_trayHost.Handle);
        if (explorer == IntPtr.Zero) { Log.Warn("No Explorer taskbar found to hide."); return; }

        if (!IsHidden)
        {
            var abd = NewData(explorer);
            uint state;
            using (_trayHost.RouteToExplorer()) state = Shell32.SHAppBarMessage(ABM.GETSTATE, ref abd);
            _originalState = state;
            WriteBackup(state);                                   // BEFORE changing anything

            using (_trayHost.RouteToExplorer())
            {
                abd.lParam = (IntPtr)(state | ABS.AUTOHIDE);
                Shell32.SHAppBarMessage(ABM.SETSTATE, ref abd);
            }
        }

        IsHidden = true;
        EnsureHidden();
        _keeper.Start();
        Log.Info("Native taskbar hidden (auto-hide + windows hidden).");
    }

    public void Restore()
    {
        if (!IsHidden) return;
        _keeper.Stop();
        IsHidden = false;

        foreach (var h in EnumerateExplorerTrayWindows(_trayHost.Handle)) User32.ShowWindow(h, SW.SHOWNA);

        var explorer = ShellTrayHost.FindExplorerTray(_trayHost.Handle);
        if (explorer != IntPtr.Zero && _originalState is { } original)
        {
            var abd = NewData(explorer);
            using (_trayHost.RouteToExplorer())
            {
                abd.lParam = (IntPtr)original;
                Shell32.SHAppBarMessage(ABM.SETSTATE, ref abd);
            }
        }
        DeleteBackup();
        Log.Info("Native taskbar restored.");
    }

    /// <summary>Explorer re-shows its taskbar on some events (hover in auto-hide mode, display changes): put it back.</summary>
    private void EnsureHidden()
    {
        if (!IsHidden) return;
        foreach (var h in EnumerateExplorerTrayWindows(_trayHost.Handle))
            if (User32.IsWindowVisible(h)) User32.ShowWindow(h, SW.HIDE);
    }

    // ── Crash safety ────────────────────────────────────────────────────────────────────────

    /// <summary>Call on startup: a leftover backup file means the previous session died while the taskbar was hidden.</summary>
    public static void RecoverFromCrash()
    {
        if (!File.Exists(BackupPath)) return;
        Log.Warn("Found taskbar-backup.json from a previous session that did not exit cleanly - restoring the native taskbar.");
        EmergencyRestore();
    }

    /// <summary>
    /// Stand-alone repair used by <c>--restore-taskbar</c> and crash recovery: shows every Explorer taskbar window, restores
    /// the saved auto-hide/always-on-top state and makes apps re-register their tray icons with Explorer.
    /// </summary>
    public static void EmergencyRestore()
    {
        uint state = ABS.MANUAL;
        try
        {
            if (File.Exists(BackupPath) &&
                JsonSerializer.Deserialize<Backup>(File.ReadAllText(BackupPath)) is { } backup)
                state = backup.State;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { /* fall back to the default state */ }

        foreach (var h in EnumerateExplorerTrayWindows(IntPtr.Zero)) User32.ShowWindow(h, SW.SHOWNA);

        var explorer = ShellTrayHost.FindExplorerTray(IntPtr.Zero);
        if (explorer != IntPtr.Zero)
        {
            var abd = NewData(explorer);
            abd.lParam = (IntPtr)state;
            Shell32.SHAppBarMessage(ABM.SETSTATE, ref abd);
        }

        User32.SendNotifyMessage(User32.HWND_BROADCAST, User32.RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
        DeleteBackup();
        Log.Info($"Emergency restore done (state={state}).");
    }

    private sealed record Backup(uint State);

    private static void WriteBackup(uint state)
    {
        try { File.WriteAllText(BackupPath, JsonSerializer.Serialize(new Backup(state))); }
        catch (IOException ex) { Log.Warn($"Could not write taskbar backup: {ex.Message}"); }
    }

    private static void DeleteBackup()
    {
        try { if (File.Exists(BackupPath)) File.Delete(BackupPath); }
        catch (IOException) { /* harmless: next start just re-runs the restore */ }
    }

    public void Dispose() => Restore();
}
