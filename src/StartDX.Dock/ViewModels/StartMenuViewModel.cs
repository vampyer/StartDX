using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Services;
using StartDX.Shared;

namespace StartDX.Dock.ViewModels;

internal sealed class StartMenuViewModel : ObservableObject
{
    private readonly UsageTracker _usage;
    private readonly DockSettingsService _settings;
    private List<AppEntry> _all = new();
    private string _search = "";

    public StartMenuViewModel(UsageTracker usage, DockSettingsService settings)
    {
        _usage = usage;
        _settings = settings;
        UserName = Environment.UserName;
        _settings.Changed += (_, _) => RefreshPinned();
        _ = LoadAsync();
    }

    public string UserName { get; }

    /// <summary>The "All apps" list, filtered by the search box.</summary>
    public ObservableCollection<AppEntry> Apps { get; } = new();
    public ObservableCollection<AppEntry> Frequent { get; } = new();
    public ObservableCollection<AppEntry> PinnedTiles { get; } = new();

    public string SearchText
    {
        get => _search;
        set { if (Set(ref _search, value)) { ApplyFilter(); OnPropertyChanged(nameof(NoResults)); OnPropertyChanged(nameof(RunHint)); } }
    }

    public bool NoResults => Apps.Count == 0 && _all.Count > 0 && !string.IsNullOrWhiteSpace(_search);
    public string RunHint => $"No matching apps. Press Enter to run “{_search.Trim()}”.";
    public Visibility FrequentVisibility => Frequent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PinnedVisibility => PinnedTiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private async Task LoadAsync()
    {
        _all = (await AppCatalogService.LoadAsync()).ToList();
        ApplyFilter();
        RefreshFrequent();
        RefreshPinned();
    }

    private void ApplyFilter()
    {
        var q = _search.Trim();
        IEnumerable<AppEntry> results = _all;
        if (q.Length > 0)
        {
            // Prefix matches first, then substring matches; alphabetical within each group.
            results = _all
                .Where(a => a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(a => a.Name.StartsWith(q, StringComparison.CurrentCultureIgnoreCase))
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);
        }
        Apps.Clear();
        foreach (var a in results) Apps.Add(a);
        OnPropertyChanged(nameof(NoResults));
    }

    public void RefreshFrequent()
    {
        Frequent.Clear();
        foreach (var key in _usage.TopKeys(6))
            if (_all.FirstOrDefault(a => a.Key == key) is { } app) Frequent.Add(app);
        OnPropertyChanged(nameof(FrequentVisibility));
    }

    public void RefreshPinned()
    {
        PinnedTiles.Clear();
        foreach (var p in _settings.Current.Pinned)
            PinnedTiles.Add(_all.FirstOrDefault(a => a.Key == p.Target) ?? new AppEntry { Name = p.Name, Target = p.Target });
        OnPropertyChanged(nameof(PinnedVisibility));
    }

    // ── Actions ─────────────────────────────────────────────────────────────────────────────

    public void Launch(AppEntry app)
    {
        if (ShellLauncher.Launch(app.Target)) { _usage.RecordLaunch(app.Key); RefreshFrequent(); }
    }

    /// <summary>Enter in the search box: launch the top match, or run the raw text as a command.</summary>
    public bool ActivateTopResult()
    {
        if (Apps.Count > 0) { Launch(Apps[0]); return true; }
        var text = _search.Trim();
        return text.Length > 0 && ShellLauncher.Launch(text);
    }

    public bool IsPinned(AppEntry app) => _settings.Current.Pinned.Any(p => p.Target == app.Target);

    public void TogglePin(AppEntry app) => _settings.Update(s =>
    {
        var existing = s.Pinned.FirstOrDefault(p => p.Target == app.Target);
        if (existing is not null) s.Pinned.Remove(existing);
        else s.Pinned.Add(new PinnedApp { Name = app.Name, Target = app.Target });
    });

    public void Reset() => SearchText = "";

    // Power actions all go through the OS; the shell never implements them itself.
    public static void Lock() => Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { UseShellExecute = false });
    public static void Sleep() => Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") { UseShellExecute = false });
    public static void Restart() => Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false });
    public static void Shutdown() => Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { UseShellExecute = false });
}
