using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using StartDX.Dock.Infrastructure;

namespace StartDX.Dock.Services;

/// <summary>
/// Best-effort bridge from *Windows toast notifications* into the notification centre via
/// UserNotificationListener. That WinRT API is only granted to apps with package identity (MSIX or a sparse
/// package); an unpackaged build will typically get Denied/Unspecified or an exception, in which case this class
/// logs the reason and stays idle - the tray-balloon and IPC feeds keep working regardless.
/// </summary>
internal sealed class SystemNotificationListener : IDisposable
{
    private readonly NotificationService _sink;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<uint> _seen = new();

    public SystemNotificationListener(NotificationService sink) => _sink = sink;

    public string Status { get; private set; } = "not started";

    public void Start() => _ = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        try
        {
            var listener = UserNotificationListener.Current;
            var access = await listener.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                Status = $"access {access} (package identity required)";
                Log.Info($"Toast listener: {Status}");
                return;
            }

            Status = "listening";
            while (!_cts.IsCancellationRequested)
            {
                var toasts = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                foreach (var n in toasts)
                {
                    if (!_seen.Add(n.Id)) continue;
                    var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                    var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    if (texts is not { Count: > 0 }) continue;
                    _sink.Post(texts[0], string.Join(Environment.NewLine, texts.Skip(1)), n.AppInfo?.DisplayInfo?.DisplayName);
                }
                await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Status = "unavailable: " + ex.Message;
            Log.Info($"Toast listener unavailable: {ex.Message}");
        }
    }

    public void Dispose() => _cts.Cancel();
}
