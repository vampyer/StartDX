using System.Windows;
using StartDX.Dock.Infrastructure;
using StartDX.Shared;
using StartDX.Shared.Ipc;

namespace StartDX.Dock.Services;

/// <summary>
/// Connects the pipe server to the app. Pipe callbacks arrive on thread-pool threads; everything that touches state is
/// marshalled to the UI dispatcher. The flow that makes "pick a theme in Settings -> dock restyles instantly" work:
///
///   Settings --setTheme--> IpcBridge --DockSettingsService.Update--> Changed event
///        ^                                                              |
///        |                                              ThemeManager.Apply (swap ResourceDictionary)
///        +------------------ state broadcast <----------- IpcBridge (subscribed to Changed)
/// </summary>
internal sealed class IpcBridge : IDisposable
{
    private readonly IpcServer _server = new();
    private readonly DockSettingsService _settings;
    private readonly NotificationService _notifications;

    public IpcBridge(DockSettingsService settings, NotificationService notifications)
    {
        _settings = settings;
        _notifications = notifications;

        _server.MessageReceived += (session, env) =>
            Application.Current.Dispatcher.InvokeAsync(() => Handle(session, env));

        // Any settings change, whatever its origin, is pushed to every connected client.
        _settings.Changed += (_, _) => BroadcastState();
        _server.ClientConnected += _ => Application.Current.Dispatcher.InvokeAsync(BroadcastState);
    }

    public int ClientCount => _server.ClientCount;

    /// <summary>Raised on the UI thread for validated <c>command</c> messages (see <see cref="IpcCommands"/>).</summary>
    public event Action<string>? CommandReceived;

    public void Start() => _server.Start();

    private StatePayload BuildState() =>
        new(_settings.Current, ThemeCatalog.BuiltIn, typeof(IpcBridge).Assembly.GetName().Version?.ToString() ?? "1.0.0");

    private void BroadcastState() =>
        _server.Broadcast(IpcEnvelope.Create(IpcProtocol.Types.State, BuildState()));

    private void Handle(IpcSession session, IpcEnvelope env)
    {
        try
        {
            switch (env.Type)
            {
                case IpcProtocol.Types.GetState:
                    _ = session.SendAsync(IpcEnvelope.Create(IpcProtocol.Types.State, BuildState(), env.Id));
                    break;

                case IpcProtocol.Types.SetTheme:
                {
                    var p = env.ReadPayload<SetThemePayload>();
                    if (p is null || !ThemeCatalog.IsKnown(p.Theme)) { Reject(session, env, "unknown_theme", $"Unknown theme '{p?.Theme}'."); return; }
                    _settings.Update(s => s.Theme = p.Theme);   // -> Changed -> theme swap + state broadcast
                    Ack(session, env);
                    break;
                }

                case IpcProtocol.Types.PatchSettings:
                {
                    var patch = env.ReadPayload<SettingsPatch>();
                    if (patch is null) { Reject(session, env, "bad_payload", "Missing settings patch."); return; }
                    if (patch.Theme is not null && !ThemeCatalog.IsKnown(patch.Theme)) { Reject(session, env, "unknown_theme", $"Unknown theme '{patch.Theme}'."); return; }
                    _settings.ApplyPatch(patch);
                    Ack(session, env);
                    break;
                }

                case IpcProtocol.Types.Notify:
                {
                    var p = env.ReadPayload<NotifyPayload>();
                    if (p is null || string.IsNullOrWhiteSpace(p.Title)) { Reject(session, env, "bad_payload", "Notification needs a title."); return; }
                    _notifications.Post(Truncate(p.Title, 120), Truncate(p.Body ?? "", 1000), p.Source is null ? null : Truncate(p.Source, 60));
                    Ack(session, env);
                    break;
                }

                case IpcProtocol.Types.Command:
                {
                    var p = env.ReadPayload<CommandPayload>();
                    if (p is null || !IpcCommands.All.Contains(p.Name)) { Reject(session, env, "unknown_command", $"Unknown command '{p?.Name}'."); return; }
                    CommandReceived?.Invoke(p.Name);
                    Ack(session, env);
                    break;
                }

                case IpcProtocol.Types.Ping:
                    _ = session.SendAsync(IpcEnvelope.Create(IpcProtocol.Types.Pong, id: env.Id));
                    break;

                default:
                    Reject(session, env, "unknown_type", $"Unknown message type '{env.Type}'.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"IPC handler for '{env.Type}' failed", ex);
            Reject(session, env, "internal_error", "The dock could not process that message.");
        }
    }

    private static void Ack(IpcSession s, IpcEnvelope req) =>
        _ = s.SendAsync(IpcEnvelope.Create(IpcProtocol.Types.Ack, id: req.Id));

    private static void Reject(IpcSession s, IpcEnvelope req, string code, string message) =>
        _ = s.SendAsync(IpcEnvelope.Create(IpcProtocol.Types.Error, new ErrorPayload(code, message), req.Id));

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose() => _server.Dispose();
}
