using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Services;
using StartDX.Shared;

namespace StartDX.Dock.ViewModels;

/// <summary>State the docked bar binds to. Orientation-dependent layout knobs live here so XAML stays declarative.</summary>
internal sealed class DockViewModel : ObservableObject
{
    private readonly DockSettingsService _settings;
    private readonly DispatcherTimer _clock;
    private DockEdge _edge;
    private string _clockTime = "", _clockDate = "";

    public DockViewModel(DockSettingsService settings, TaskListService tasks, TrayService tray, NotificationService notifications, SystemStatusService systemStatus)
    {
        _settings = settings;
        Tasks = tasks.Tasks;
        TrayIcons = tray.Icons;
        Notifications = notifications;
        SystemStatus = systemStatus;

        SyncFromSettings(settings.Current);

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateClock(), Dispatcher.CurrentDispatcher);
        UpdateClock();
        _clock.Start();
    }

    public ObservableCollection<TaskItem> Tasks { get; }
    public ObservableCollection<TrayIconItem> TrayIcons { get; }
    public ObservableCollection<PinnedAppViewModel> Pinned { get; } = new();
    public NotificationService Notifications { get; }
    public SystemStatusService SystemStatus { get; }

    public DockEdge Edge { get => _edge; private set { if (Set(ref _edge, value)) RaiseLayoutProperties(); } }
    public bool IsHorizontal => Edge is DockEdge.Top or DockEdge.Bottom;

    public Orientation Orientation => IsHorizontal ? Orientation.Horizontal : Orientation.Vertical;
    public Visibility LabelVisibility => _settings.Current.ShowTaskLabels && IsHorizontal ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DateVisibility => IsHorizontal ? Visibility.Visible : Visibility.Collapsed;
    public double IconSize => 28;

    // Active-window indicator: hugs the edge of the item that faces away from the screen edge.
    public HorizontalAlignment IndicatorH => IsHorizontal ? HorizontalAlignment.Center : Edge == DockEdge.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    public VerticalAlignment IndicatorV => !IsHorizontal ? VerticalAlignment.Center : Edge == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
    public double IndicatorWidth => IsHorizontal ? 18 : 3;
    public double IndicatorHeight => IsHorizontal ? 3 : 18;

    public string ClockTime { get => _clockTime; private set => Set(ref _clockTime, value); }
    public string ClockDate { get => _clockDate; private set => Set(ref _clockDate, value); }

    public void SyncFromSettings(AppSettings s)
    {
        Edge = s.Edge;
        OnPropertyChanged(nameof(LabelVisibility));

        // Reconcile the pinned collection in place (keeps icon caches and avoids flicker).
        for (var i = Pinned.Count - 1; i >= 0; i--)
            if (!s.Pinned.Any(p => p.Target == Pinned[i].Target)) Pinned.RemoveAt(i);
        foreach (var p in s.Pinned)
            if (!Pinned.Any(v => v.Target == p.Target)) Pinned.Add(new PinnedAppViewModel(p));
    }

    /// <summary>Wide enough for "12:59 PM" in a horizontal bar; unconstrained in a narrow vertical one.</summary>
    public double ClockMinWidth => IsHorizontal ? 60 : 0;

    private void RaiseLayoutProperties()
    {
        UpdateClock();                       // vertical bars stack "1:51" over "PM"
        OnPropertyChanged(nameof(ClockMinWidth));
        OnPropertyChanged(nameof(IsHorizontal));
        OnPropertyChanged(nameof(Orientation));
        OnPropertyChanged(nameof(LabelVisibility));
        OnPropertyChanged(nameof(DateVisibility));
        OnPropertyChanged(nameof(IndicatorH));
        OnPropertyChanged(nameof(IndicatorV));
        OnPropertyChanged(nameof(IndicatorWidth));
        OnPropertyChanged(nameof(IndicatorHeight));
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var time = now.ToString("t");
        ClockTime = IsHorizontal ? time : time.Replace(' ', '\n');
        ClockDate = now.ToString("d");
    }
}
