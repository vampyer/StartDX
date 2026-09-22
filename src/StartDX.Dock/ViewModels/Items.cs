using System.Windows.Media;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Services;
using StartDX.Shared;

namespace StartDX.Dock.ViewModels;

/// <summary>A window shown on the task switcher.</summary>
public sealed class TaskItem : ObservableObject
{
    private string _title = "";
    private ImageSource? _icon;
    private bool _isActive;
    private bool _isMinimized;

    public required IntPtr Handle { get; init; }
    public uint ProcessId { get; set; }
    public string? ExePath { get; set; }

    public string Title { get => _title; set => Set(ref _title, value); }
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public bool IsMinimized { get => _isMinimized; set => Set(ref _isMinimized, value); }
}

/// <summary>An icon registered in the notification area via NIM_ADD.</summary>
public sealed class TrayIconItem : ObservableObject
{
    private ImageSource? _icon;
    private string _tooltip = "";

    public required string Key { get; init; }
    public IntPtr HWnd { get; set; }
    public uint UId { get; set; }
    public Guid Guid { get; set; }
    public uint CallbackMessage { get; set; }
    public uint Version { get; set; }
    public uint State { get; set; }

    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }
    public string Tooltip { get => _tooltip; set => Set(ref _tooltip, value); }
    public bool IsHidden => (State & 0x1) != 0;   // NIS_HIDDEN
}

/// <summary>A launchable application (packaged or classic) from shell:AppsFolder, or a pinned file.</summary>
public sealed class AppEntry : ObservableObject
{
    private ImageSource? _smallIcon, _largeIcon;
    private bool _smallRequested, _largeRequested;

    public required string Name { get; init; }

    /// <summary>What ShellExecute is given: "shell:AppsFolder\{id}" or a file path.</summary>
    public required string Target { get; init; }

    /// <summary>Stable usage-tracking key.</summary>
    public string Key => Target;

    /// <summary>64px icon for list rows and dock buttons; loaded lazily on first bind.</summary>
    public ImageSource? SmallIcon
    {
        get
        {
            if (!_smallRequested) { _smallRequested = true; _ = LoadAsync(64, i => { _smallIcon = i; OnPropertyChanged(nameof(SmallIcon)); }); }
            return _smallIcon;
        }
    }

    /// <summary>256px icon for the 128x128 (@2x) tiles; loaded lazily on first bind.</summary>
    public ImageSource? LargeIcon
    {
        get
        {
            if (!_largeRequested) { _largeRequested = true; _ = LoadAsync(256, i => { _largeIcon = i; OnPropertyChanged(nameof(LargeIcon)); }); }
            return _largeIcon;
        }
    }

    private async Task LoadAsync(int px, Action<ImageSource?> assign)
    {
        var icon = await IconService.Instance.GetAsync(Target, px);
        System.Windows.Application.Current?.Dispatcher.Invoke(() => assign(icon));
    }
}

/// <summary>An entry in the notification centre.</summary>
public sealed class NotificationItem
{
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string? Source { get; init; }
    public DateTime Time { get; init; } = DateTime.Now;
    public string TimeText => Time.ToString("t");
}

public sealed class PinnedAppViewModel(PinnedApp model) : ObservableObject
{
    public PinnedApp Model { get; } = model;
    public string Name => Model.Name;
    public string Target => Model.Target;

    private ImageSource? _icon;
    private bool _requested;
    public ImageSource? Icon
    {
        get
        {
            if (!_requested)
            {
                _requested = true;
                _ = Task.Run(async () =>
                {
                    var i = await IconService.Instance.GetAsync(Target, 64);
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => { _icon = i; OnPropertyChanged(nameof(Icon)); });
                });
            }
            return _icon;
        }
    }
}
