using System.Collections.ObjectModel;
using System.Windows;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.ViewModels;

namespace StartDX.Dock.Services;

/// <summary>
/// The notification centre's model. Three feeds converge here:
///  1. Balloon tips from tray icons (NIF_INFO) delivered through <see cref="TrayService"/>,
///  2. <c>notify</c> messages arriving over the IPC pipe (scripts, the settings app, other local tools),
///  3. Windows toast notifications, when <see cref="SystemNotificationListener"/> is granted access.
/// </summary>
internal sealed class NotificationService : ObservableObject
{
    private const int MaxItems = 100;
    private int _unread;

    public ObservableCollection<NotificationItem> Items { get; } = new();

    public int Unread { get => _unread; private set { if (Set(ref _unread, value)) OnPropertyChanged(nameof(HasUnread)); } }
    public bool HasUnread => _unread > 0;

    /// <summary>Raised after an item is added (UI thread).</summary>
    public event Action<NotificationItem>? Posted;

    /// <summary>Thread-safe: marshals to the UI thread.</summary>
    public void Post(string title, string body, string? source)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.InvokeAsync(() =>
        {
            var item = new NotificationItem { Title = title, Body = body, Source = source };
            Items.Insert(0, item);
            while (Items.Count > MaxItems) Items.RemoveAt(Items.Count - 1);
            Unread++;
            Posted?.Invoke(item);
        });
    }

    public void MarkAllRead() => Unread = 0;
    public void Clear() { Items.Clear(); Unread = 0; }
    public void Remove(NotificationItem item) { Items.Remove(item); if (Unread > Items.Count) Unread = Items.Count; }
}
