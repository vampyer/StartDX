using System.Windows;
using StartDX.Dock.Infrastructure;
using StartDX.Shared;

namespace StartDX.Dock.Theming;

public sealed class ThemeChangedEventArgs(string oldThemeId, string newThemeId) : EventArgs
{
    public string OldThemeId { get; } = oldThemeId;
    public string NewThemeId { get; } = newThemeId;
}

/// <summary>
/// Runtime theme switcher built on *decoupled* ResourceDictionaries.
///
/// Application.Resources.MergedDictionaries is kept as exactly three slots:
///
///   [0] Theme.Contract.xaml  - default value for EVERY token. A theme that forgets a key still renders.
///   [1] &lt;active theme&gt;.xaml   - the ONLY slot that is ever replaced. Overrides the tokens it cares about.
///   [2] Controls.xaml        - control templates/styles. They reference tokens exclusively via DynamicResource,
///                              so they never need reloading: swapping slot [1] re-resolves every binding in place.
///
/// Swapping = build the new dictionary off to the side, and only if it parses successfully replace the slot in a single
/// indexer assignment. One change notification, no flicker, and a broken theme file can never leave the UI unstyled.
/// </summary>
public sealed class ThemeManager
{
    private const int ContractSlot = 0, ThemeSlot = 1, ControlsSlot = 2;

    private static readonly string AssemblyName = typeof(ThemeManager).Assembly.GetName().Name!;
    private readonly Dictionary<string, Uri> _themeUris = new(StringComparer.Ordinal);
    private readonly List<ThemeDescriptor> _descriptors = new();
    private Application? _app;

    public static ThemeManager Instance { get; } = new();

    private ThemeManager()
    {
        foreach (var d in ThemeCatalog.BuiltIn) Register(d, PackUri($"Themes/{d.Id}.xaml"));
    }

    public string CurrentThemeId { get; private set; } = "";
    public IReadOnlyList<ThemeDescriptor> Available => _descriptors;

    /// <summary>Raised after the new dictionary is live. Handlers may read resources immediately.</summary>
    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    private static Uri PackUri(string relative) =>
        new($"pack://application:,,,/{AssemblyName};component/{relative}", UriKind.Absolute);

    /// <summary>
    /// Extension point: register another theme by pack URI (compiled into this or a referenced assembly).
    /// Loose XAML files from disk are deliberately not supported - XAML can instantiate arbitrary types.
    /// </summary>
    public void Register(ThemeDescriptor descriptor, Uri packUri)
    {
        if (!string.Equals(packUri.Scheme, "pack", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only pack:// URIs are allowed for themes.", nameof(packUri));
        _themeUris[descriptor.Id] = packUri;
        _descriptors.RemoveAll(d => d.Id == descriptor.Id);
        _descriptors.Add(descriptor);
    }

    /// <summary>Installs the three-slot structure. Call once, on the UI thread, before any window is created.</summary>
    public void Initialize(Application app)
    {
        _app = app;
        var merged = app.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(new ResourceDictionary { Source = PackUri("Themes/Theme.Contract.xaml") });   // [0]
        merged.Add(new ResourceDictionary());                                                     // [1] placeholder
        merged.Add(new ResourceDictionary { Source = PackUri("Themes/Controls.xaml") });          // [2]
    }

    /// <summary>Switch to <paramref name="themeId"/>. Returns false (and keeps the current theme) if it cannot be loaded.</summary>
    public bool Apply(string themeId)
    {
        if (_app is null) throw new InvalidOperationException("ThemeManager.Initialize must run first.");
        if (!_app.Dispatcher.CheckAccess()) return _app.Dispatcher.Invoke(() => Apply(themeId));

        if (themeId == CurrentThemeId) return true;
        if (!_themeUris.TryGetValue(themeId, out var uri))
        {
            Log.Warn($"Unknown theme '{themeId}'.");
            return false;
        }

        ResourceDictionary next;
        try { next = new ResourceDictionary { Source = uri }; }   // parse off to the side first
        catch (Exception ex) when (ex is IOException or System.Windows.Markup.XamlParseException)
        {
            Log.Error($"Theme '{themeId}' failed to load; keeping '{CurrentThemeId}'", ex);
            return false;
        }

        var previous = CurrentThemeId;
        _app.Resources.MergedDictionaries[ThemeSlot] = next;     // the single atomic swap
        CurrentThemeId = themeId;

        Log.Info($"Theme switched: '{previous}' -> '{themeId}'");
        // A theme that omits keys still renders (contract fallbacks) - but tell the author what they are inheriting.
        var contract = _app.Resources.MergedDictionaries[ContractSlot];
        var inherited = contract.Keys.Cast<object>().Where(k => !next.Contains(k)).Select(k => k.ToString()).ToList();
        if (inherited.Count > 0) Log.Info($"  theme '{themeId}' inherits {inherited.Count} contract defaults: {string.Join(", ", inherited.Take(12))}{(inherited.Count > 12 ? ", ..." : "")}");
        ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(previous, themeId));
        return true;
    }

    // ── Typed token access for code that has to look at the active theme ────────────────────

    public T Get<T>(string key, T fallback)
    {
        var value = _app?.TryFindResource(key);
        return value is T typed ? typed : fallback;
    }
}
