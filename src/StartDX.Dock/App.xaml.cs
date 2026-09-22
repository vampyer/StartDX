using System.Windows;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Services;
using StartDX.Dock.Theming;
using StartDX.Dock.Views;
using StartDX.Shared;

namespace StartDX.Dock;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Emergency switch:  StartDX.Dock.exe --restore-taskbar
        // Stops any running dock, un-hides Explorer's taskbar and restores its saved state, then exits. Use it if the dock was
        // killed while replacing the taskbar and you want the Windows taskbar back without restarting Explorer.
        if (e.Args.Contains("--restore-taskbar", StringComparer.OrdinalIgnoreCase))
        {
            foreach (var other in System.Diagnostics.Process.GetProcessesByName("StartDX.Dock").Where(p => p.Id != Environment.ProcessId))
            {
                try { other.Kill(); other.WaitForExit(3000); } catch (Exception) { /* already gone / access denied */ }
            }
            NativeTaskbarService.EmergencyRestore();
            Shutdown();
            return;
        }

        // One dock per user session.
        _singleInstance = new Mutex(true, @"Local\StartDX.Dock.SingleInstance", out _ownsMutex);
        if (!_ownsMutex) { Shutdown(); return; }

        // A shell must not die because one handler threw: log, keep going.
        DispatcherUnhandledException += (_, ex) => { Log.Error("Unhandled UI exception", ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => { Log.Error("Fatal exception", ex.ExceptionObject as Exception); _services?.RestoreSystem(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _services?.RestoreSystem();
        SessionEnding += (_, _) => _services?.RestoreSystem();

        var settings = new DockSettingsService(SettingsStore.Load());

        // Theme dictionaries must be in place BEFORE any window's XAML is loaded (StaticResource lookups).
        ThemeManager.Instance.Initialize(this);
        if (!ThemeManager.Instance.Apply(settings.Current.Theme)) ThemeManager.Instance.Apply(ThemeCatalog.Default);

        _services = new AppServices(settings);
        var window = new MainWindow(_services);
        MainWindow = window;
        window.Show();
        _services.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();          // restores the native taskbar, tray ownership and appbar registration
        if (_ownsMutex) _singleInstance?.ReleaseMutex();   // a second instance never owned it
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
