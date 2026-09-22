using StartDX.Dock.Infrastructure;
using StartDX.Shared;

namespace StartDX.Dock.Services;

/// <summary>
/// Single owner of <see cref="AppSettings"/>. Every mutation - a context-menu click, an IPC patch, a pin/unpin -
/// funnels through <see cref="Update"/>, which sanitises, persists and raises <see cref="Changed"/>. The App wires
/// that one event to every subsystem, so there is exactly one place where "settings changed" is turned into behaviour.
/// </summary>
internal sealed class DockSettingsService
{
    public DockSettingsService(AppSettings initial)
    {
        initial.Sanitize();
        Current = initial;
    }

    public AppSettings Current { get; private set; }

    /// <summary>(old, new). Raised on the caller's thread - call <see cref="Update"/> from the UI thread.</summary>
    public event Action<AppSettings, AppSettings>? Changed;

    public bool Update(Action<AppSettings> mutate)
    {
        var next = Current.Clone();
        mutate(next);
        next.Sanitize();

        if (SettingsJsonEquals(Current, next)) return false;

        var old = Current;
        Current = next;
        try { SettingsStore.Save(next); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"settings.json not saved: {ex.Message}");
        }
        Changed?.Invoke(old, next);
        return true;
    }

    public bool ApplyPatch(SettingsPatch patch) => Update(patch.ApplyTo);

    private static bool SettingsJsonEquals(AppSettings a, AppSettings b) =>
        System.Text.Json.JsonSerializer.Serialize(a, SettingsJson.Options) ==
        System.Text.Json.JsonSerializer.Serialize(b, SettingsJson.Options);
}
