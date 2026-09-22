using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StartDX.Shared;
using StartDX.Shared.Ipc;

namespace StartDX.Settings.Infrastructure;

public sealed class ThemeOption(ThemeDescriptor descriptor) : ObservableObject
{
    private bool _isSelected;
    public ThemeDescriptor Descriptor { get; } = descriptor;
    public string Id => Descriptor.Id;
    public string DisplayName => Descriptor.DisplayName;
    public string Description => Descriptor.Description;
    public Brush Accent { get; } = Brush(descriptor.AccentHex);
    public Brush Preview { get; } = new LinearGradientBrush(
        (Color)ColorConverter.ConvertFromString(descriptor.PreviewFromHex),
        (Color)ColorConverter.ConvertFromString(descriptor.PreviewToHex), 45);
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    private static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
}

/// <summary>
/// Talks to the dock over the pipe. Local edits are pushed as patches; anything the dock says (including changes made
/// on the dock itself - e.g. drag-to-dock or its context menu) arrives as a <c>state</c> message and overwrites the
/// UI, guarded by <c>_applying</c> so applying incoming state never echoes back out as a new edit.
/// If the dock is not running, edits are written straight to settings.json and picked up on its next start.
/// </summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IpcClient _client = new();
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _thicknessDebounce;
    private bool _applying;

    private bool _connected;
    private string? _error;
    private DockEdge _edge = DockEdge.Bottom;
    private int _thickness = 56;
    private bool _overFullscreen = true, _replaceTaskbar, _winKey = true, _showLabels = true, _runAtStartup;

    public SettingsViewModel()
    {
        foreach (var t in ThemeCatalog.BuiltIn) Themes.Add(new ThemeOption(t));
        LoadFromDisk();

        SelectThemeCommand = new RelayCommand(p => { if (p is ThemeOption o) SelectTheme(o.Id); });
        StartDockCommand = new RelayCommand(_ => StartDock());
        TestNotificationCommand = new RelayCommand(_ => _ = _client.SendAsync(IpcEnvelope.Create(IpcProtocol.Types.Notify,
            new NotifyPayload("Hello from StartDX Settings", "This notification travelled over the IPC pipe to the dock.", "StartDX Settings"))));

        _thicknessDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) =>
        {
            _thicknessDebounce!.Stop();
            Push(new SettingsPatch { ThicknessDip = _thickness });
        }, _ui);

        _client.Connected += () => _ui.InvokeAsync(() => { IsConnected = true; Error = null; });
        _client.Disconnected += () => _ui.InvokeAsync(() => IsConnected = false);
        _client.MessageReceived += env => _ui.InvokeAsync(() => OnMessage(env));
        _client.Start();
    }

    public ObservableCollection<ThemeOption> Themes { get; } = new();
    public ICommand SelectThemeCommand { get; }
    public ICommand StartDockCommand { get; }
    public ICommand TestNotificationCommand { get; }

    public bool IsConnected
    {
        get => _connected;
        private set { if (Set(ref _connected, value)) { Raise(nameof(StatusText)); Raise(nameof(NotConnected)); } }
    }
    public bool NotConnected => !_connected;
    public string StatusText => _connected ? "Connected to the dock" : "Dock not running – changes are saved and applied on next start";
    public string? Error { get => _error; private set => Set(ref _error, value); }

    public DockEdge Edge { get => _edge; set { if (Set(ref _edge, value) && !_applying) Push(new SettingsPatch { Edge = value }); } }

    public int Thickness
    {
        get => _thickness;
        set
        {
            if (!Set(ref _thickness, value) || _applying) return;
            _thicknessDebounce.Stop();      // slider drags fire continuously: send once the user pauses
            _thicknessDebounce.Start();
        }
    }

    public bool OverFullscreen { get => _overFullscreen; set { if (Set(ref _overFullscreen, value) && !_applying) Push(new SettingsPatch { AlwaysOnTopOverFullscreen = value }); } }
    public bool ReplaceTaskbar { get => _replaceTaskbar; set { if (Set(ref _replaceTaskbar, value) && !_applying) Push(new SettingsPatch { ReplaceNativeTaskbar = value }); } }
    public bool WinKey { get => _winKey; set { if (Set(ref _winKey, value) && !_applying) Push(new SettingsPatch { WinKeyOpensStartMenu = value }); } }
    public bool ShowLabels { get => _showLabels; set { if (Set(ref _showLabels, value) && !_applying) Push(new SettingsPatch { ShowTaskLabels = value }); } }
    public bool RunAtStartup { get => _runAtStartup; set { if (Set(ref _runAtStartup, value) && !_applying) Push(new SettingsPatch { RunAtStartup = value }); } }

    // ── Theme selection: the headline feature ──────────────────────────────────────────────

    private void SelectTheme(string id)
    {
        MarkSelected(id);   // optimistic; the dock's state broadcast will confirm
        if (_connected)
            _ = _client.SendAsync(IpcEnvelope.Create(IpcProtocol.Types.SetTheme, new SetThemePayload(id)));
        else
            Push(new SettingsPatch { Theme = id });
    }

    private void MarkSelected(string id)
    {
        foreach (var t in Themes) t.IsSelected = t.Id == id;
    }

    // ── Wire handling ───────────────────────────────────────────────────────────────────────

    private void OnMessage(IpcEnvelope env)
    {
        switch (env.Type)
        {
            case IpcProtocol.Types.State when env.ReadPayload<StatePayload>() is { } state:
                Apply(state.Settings);
                break;
            case IpcProtocol.Types.Error when env.ReadPayload<ErrorPayload>() is { } err:
                Error = err.Message;
                break;
        }
    }

    private void Apply(AppSettings s)
    {
        _applying = true;
        try
        {
            MarkSelected(s.Theme);
            Edge = s.Edge;
            Thickness = s.ThicknessDip;
            OverFullscreen = s.AlwaysOnTopOverFullscreen;
            ReplaceTaskbar = s.ReplaceNativeTaskbar;
            WinKey = s.WinKeyOpensStartMenu;
            ShowLabels = s.ShowTaskLabels;
            RunAtStartup = s.RunAtStartup;
        }
        finally { _applying = false; }
    }

    private void LoadFromDisk() => Apply(SettingsStore.Load());

    private void Push(SettingsPatch patch)
    {
        if (_connected)
        {
            _ = _client.SendAsync(IpcEnvelope.Create(IpcProtocol.Types.PatchSettings, patch));
            return;
        }

        // Offline: persist directly so the dock picks it up on next launch.
        try
        {
            var s = SettingsStore.Load();
            patch.ApplyTo(s);
            s.Sanitize();
            SettingsStore.Save(s);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Could not save settings: " + ex.Message;
        }
    }

    private static void StartDock()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "StartDX.Dock.exe");
        if (System.IO.File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void Dispose() => _client.Dispose();
}
