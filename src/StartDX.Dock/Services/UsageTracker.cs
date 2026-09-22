using System.Text.Json;
using StartDX.Dock.Infrastructure;
using StartDX.Shared;

namespace StartDX.Dock.Services;

/// <summary>Persists launch counts so the Start menu can show a "Frequently used" section.</summary>
internal sealed class UsageTracker
{
    private sealed class Record { public int Count { get; set; } public DateTime Last { get; set; } }

    private readonly string _path = Path.Combine(SettingsStore.DataDirectory, "usage.json");
    private readonly Dictionary<string, Record> _data;

    public UsageTracker()
    {
        try
        {
            _data = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, Record>>(File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { _data = new(); }
    }

    public void RecordLaunch(string key)
    {
        if (!_data.TryGetValue(key, out var r)) _data[key] = r = new Record();
        r.Count++;
        r.Last = DateTime.UtcNow;
        Save();
    }

    /// <summary>Keys ordered by launch count (ties: most recent first).</summary>
    public IEnumerable<string> TopKeys(int count) =>
        _data.OrderByDescending(kv => kv.Value.Count).ThenByDescending(kv => kv.Value.Last)
             .Take(count).Select(kv => kv.Key);

    private void Save()
    {
        try
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_data));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (IOException ex) { Log.Warn($"usage.json not saved: {ex.Message}"); }
    }
}
