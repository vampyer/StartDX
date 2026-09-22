using StartDX.Dock.Infrastructure;
using StartDX.Dock.Theming;
using StartDX.Shared;

namespace StartDX.Dock.Services;

/// <summary>
/// Composition root: owns every long-lived service and translates the single "settings changed" event into
/// behaviour. Deliberately a plain object (no DI container) - a shell has ~10 singletons and one wiring point.
/// </summary>
internal sealed class AppServices : IDisposable
{
    private bool _restored;

    public AppServices(DockSettingsService settings)
    {
        Settings = settings;

        Notifications = new NotificationService();
        Usage = new UsageTracker();
        Tasks = new TaskListService();
        Tray = new TrayService();
        SystemStatus = new SystemStatusService();
        NativeTaskbar = new NativeTaskbarService(Tray.Host);
        AppBar = new AppBarService { KeepOnTopOverFullscreen = settings.Current.AlwaysOnTopOverFullscreen };
        AppBar.RouteScope = Tray.Host.RouteToExplorer;      // keep SHAppBarMessage pointed at Explorer in take-over mode
        WinKey = new WinKeyHook();
        ToastListener = new SystemNotificationListener(Notifications);
        Ipc = new IpcBridge(settings, Notifications);

        // Tray balloons (NIF_INFO) become notification-centre entries.
        Tray.BalloonReceived += (title, body, source) =>
            Notifications.Post(string.IsNullOrWhiteSpace(title) ? (source ?? "Notification") : title, body, source);

        settings.Changed += OnSettingsChanged;
    }

    public DockSettingsService Settings { get; }
    public NotificationService Notifications { get; }
    public UsageTracker Usage { get; }
    public TaskListService Tasks { get; }
    public TrayService Tray { get; }
    public SystemStatusService SystemStatus { get; }
    public NativeTaskbarService NativeTaskbar { get; }
    public AppBarService AppBar { get; }
    public WinKeyHook WinKey { get; }
    public SystemNotificationListener ToastListener { get; }
    public IpcBridge Ipc { get; }

    /// <summary>Called once the main window exists.</summary>
    public void Start()
    {
        var s = Settings.Current;
        NativeTaskbarService.RecoverFromCrash();                    // repair a taskbar left hidden by a killed previous session
        if (Environment.GetCommandLineArgs().Contains(StartupService.AutostartArgument, StringComparer.OrdinalIgnoreCase))
            Log.Info("Launched by Windows at sign-in (--autostart).");
        ReconcileStartup(s);
        ApplyReplacement(s.ReplaceNativeTaskbar);
        if (!s.ReplaceNativeTaskbar) Tray.Start(takeOver: false);   // only succeeds when Explorer's tray is absent
        WinKey.Enabled = WantsStartInterception(s);
        ToastListener.Start();
        Ipc.Start();
    }

    private void OnSettingsChanged(AppSettings old, AppSettings cur)
    {
        if (old.Theme != cur.Theme) ThemeManager.Instance.Apply(cur.Theme);
        if (old.ReplaceNativeTaskbar != cur.ReplaceNativeTaskbar) ApplyReplacement(cur.ReplaceNativeTaskbar);
        WinKey.Enabled = WantsStartInterception(cur);
        AppBar.KeepOnTopOverFullscreen = cur.AlwaysOnTopOverFullscreen;
        if (old.RunAtStartup != cur.RunAtStartup) ApplyStartup(cur.RunAtStartup);
    }

    // ── Start with Windows ──────────────────────────────────────────────────────────────────

    /// <summary>Make the registry match the setting - except never override the user's own "Disable" in Task Manager.</summary>
    private void ReconcileStartup(AppSettings s)
    {
        try
        {
            var state = StartupService.GetState();
            if (s.RunAtStartup)
            {
                if (state == StartupState.DisabledInTaskManager)
                {
                    Log.Info("Start with Windows was disabled in Task Manager; turning the setting off to match.");
                    Settings.Update(x => x.RunAtStartup = false);
                }
                else if (state == StartupState.NotRegistered || StartupService.IsStale())
                {
                    StartupService.Register();          // also heals the entry after the exe was moved / republished elsewhere
                }
            }
            else if (state != StartupState.NotRegistered)
            {
                StartupService.Unregister();            // setting is off (e.g. edited while the dock was closed): drop the entry
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            Log.Warn($"Could not sync the startup entry: {ex.Message}");
        }
    }

    private void ApplyStartup(bool enable)
    {
        try
        {
            if (enable) StartupService.Register();
            else StartupService.Unregister();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            Log.Warn($"Could not {(enable ? "enable" : "disable")} Start with Windows: {ex.Message}");
            if (enable) Settings.Update(x => x.RunAtStartup = false);   // don't claim it is on when it is not
        }
    }

    /// <summary>Replacing the taskbar implies replacing its Start menu: Win / Ctrl+Esc must then open StartDX, not Windows'.</summary>
    private static bool WantsStartInterception(AppSettings s) => s.WinKeyOpensStartMenu || s.ReplaceNativeTaskbar;

    /// <summary>Full replacement = tray take-over + auto-hidden Explorer taskbar. Fully reversible.</summary>
    private void ApplyReplacement(bool enable)
    {
        if (enable)
        {
            Tray.Start(takeOver: true);
            NativeTaskbar.Hide();
        }
        else
        {
            NativeTaskbar.Restore();          // must run while the host can still route to Explorer
            if (Tray.Host.IsTakeOver) Tray.Stop();
        }
    }

    /// <summary>Undo every system-wide change. Safe to call repeatedly (exit, session end, crash handler).</summary>
    public void RestoreSystem()
    {
        if (_restored) return;
        _restored = true;
        try { WinKey.Enabled = false; } catch { }
        try { NativeTaskbar.Restore(); } catch { }
        try { Tray.Stop(); } catch { }
        try { AppBar.Unregister(); } catch { }
    }

    public void Dispose()
    {
        RestoreSystem();
        Ipc.Dispose();
        ToastListener.Dispose();
        SystemStatus.Dispose();
        Tasks.Dispose();
        Tray.Dispose();
    }
}
