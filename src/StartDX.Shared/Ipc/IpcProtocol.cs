using System.Text.Json;
using System.Text.Json.Serialization;

namespace StartDX.Shared.Ipc;

/// <summary>
/// Wire protocol between the dock (server) and any number of settings/tooling clients.
///
/// Transport : per-user named pipe, PipeOptions.CurrentUserOnly (ACL'd to the current user).
/// Framing   : one JSON document per line (UTF-8, '\n'). System.Text.Json never emits raw
///             newlines inside a compact document, so line framing is unambiguous.
/// Envelope  : { "v":1, "type":"setTheme", "id":"abc", "payload":{...} }
/// Flow      : client -> getState            server -> state
///             client -> setTheme            server -> state (broadcast to EVERY client) or error
///             client -> patchSettings       server -> state (broadcast) or error
///             client -> notify              server -> ack
/// Because every accepted change is re-broadcast as a full <c>state</c>, all connected
/// clients (and the dock UI) converge on the same truth without bespoke sync logic.
/// </summary>
public static class IpcProtocol
{
    public const int Version = 1;
    public const int MaxMessageChars = 256 * 1024;

    public static string PipeName => $"StartDX.Ipc.v{Version}.{Environment.UserName}";

    public static class Types
    {
        public const string GetState = "getState";
        public const string State = "state";
        public const string SetTheme = "setTheme";
        public const string PatchSettings = "patchSettings";
        public const string Notify = "notify";
        public const string Command = "command";
        public const string Ping = "ping";
        public const string Pong = "pong";
        public const string Ack = "ack";
        public const string Error = "error";
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false, // must stay compact: the framing relies on it
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}

public sealed class IpcEnvelope
{
    public int V { get; set; } = IpcProtocol.Version;
    public string Type { get; set; } = "";
    public string? Id { get; set; }
    public JsonElement? Payload { get; set; }

    public static IpcEnvelope Create(string type, object? payload = null, string? id = null) => new()
    {
        Type = type,
        Id = id,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload, payload.GetType(), IpcProtocol.Json),
    };

    public T? ReadPayload<T>() =>
        Payload is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } p
            ? p.Deserialize<T>(IpcProtocol.Json)
            : default;
}

public sealed record SetThemePayload(string Theme);
public sealed record StatePayload(AppSettings Settings, IReadOnlyList<ThemeDescriptor> Themes, string DockVersion);
public sealed record NotifyPayload(string Title, string Body, string? Source);

/// <summary>Remote-control verbs, e.g. for a global-hotkey daemon: see <see cref="IpcCommands"/>.</summary>
public sealed record CommandPayload(string Name);

public static class IpcCommands
{
    public const string ToggleStart = "toggleStart";
    public const string ToggleNotifications = "toggleNotifications";
    /// <summary>Graceful shutdown: restores the native taskbar / tray ownership exactly like the context-menu Exit.</summary>
    public const string Exit = "exit";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { ToggleStart, ToggleNotifications, Exit };
}
public sealed record ErrorPayload(string Code, string Message);
