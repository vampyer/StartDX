using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Native;
using StartDX.Dock.ViewModels;

namespace StartDX.Dock.Services;

public enum TrayClick { Left, LeftDouble, Right }

/// <summary>
/// Notification-area model. Consumes the raw NIM_* stream from <see cref="ShellTrayHost"/>, maintains the icon list the
/// UI binds to, forwards balloon tips (NIF_INFO) to the notification centre, and posts mouse input back to the owning app
/// using the callback message it registered - exactly what Explorer does.
/// </summary>
internal sealed class TrayService : IDisposable
{
    private readonly Dictionary<string, TrayIconItem> _byKey = new();
    private readonly DispatcherTimer _reaper;

    public TrayService()
    {
        Host = new ShellTrayHost();
        Host.TrayMessage += OnTrayMessage;
        Host.IconRectResolver = ResolveRect;

        // Apps that crash never send NIM_DELETE. Explorer prunes dead owners; so do we.
        _reaper = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Background, (_, _) => PruneDead(), Dispatcher.CurrentDispatcher);
        _reaper.Start();
    }

    public ShellTrayHost Host { get; }

    /// <summary>Visible (non-NIS_HIDDEN) icons in registration order.</summary>
    public ObservableCollection<TrayIconItem> Icons { get; } = new();

    /// <summary>Title, body, source (tooltip of the sending icon).</summary>
    public event Action<string, string, string?>? BalloonReceived;

    /// <summary>Set by the view: gives the on-screen rect of an icon for Shell_NotifyIconGetRect.</summary>
    public Func<TrayIconItem, RECT?>? RectProvider { get; set; }

    public bool Start(bool takeOver) => Host.Start(takeOver);

    public void Stop()
    {
        Host.Stop();
        _byKey.Clear();
        Icons.Clear();
    }

    // ── NIM_* handling ──────────────────────────────────────────────────────────────────────

    private static string KeyOf(in NOTIFYICONDATA nid, NIF flags) =>
        flags.HasFlag(NIF.GUID) && nid.guidItem != Guid.Empty ? $"G:{nid.guidItem:N}" : $"H:{nid.hWnd:X8}:{nid.uID}";

    private void OnTrayMessage(uint nim, NOTIFYICONDATA nid)
    {
        var flags = (NIF)nid.uFlags;
        var key = KeyOf(nid, flags);
        _byKey.TryGetValue(key, out var item);

        switch (nim)
        {
            case NIM.ADD:
            case NIM.MODIFY:
                if (item is null)
                {
                    // NIM_MODIFY for an unknown icon normally fails, but apps that registered with Explorer before our
                    // take-over keep sending MODIFYs and never re-add - adopt them when they carry usable data.
                    if (nim == NIM.MODIFY && !flags.HasFlag(NIF.ICON) && !flags.HasFlag(NIF.TIP)) return;
                    item = new TrayIconItem { Key = key };
                    _byKey[key] = item;
                }
                Apply(item, nid, flags);
                SyncVisibility(item);
                break;

            case NIM.DELETE:
                if (item is not null) { _byKey.Remove(key); Icons.Remove(item); }
                break;

            case NIM.SETVERSION:
                if (item is not null) item.Version = nid.uVersion <= 4 ? nid.uVersion : 0;
                break;
        }
    }

    private void Apply(TrayIconItem item, in NOTIFYICONDATA nid, NIF flags)
    {
        item.HWnd = Win32Ex.FromHandle32(nid.hWnd);
        item.UId = nid.uID;
        if (flags.HasFlag(NIF.GUID)) item.Guid = nid.guidItem;
        if (flags.HasFlag(NIF.MESSAGE)) item.CallbackMessage = nid.uCallbackMessage;
        if (flags.HasFlag(NIF.TIP)) item.Tooltip = nid.szTip ?? "";
        if (flags.HasFlag(NIF.STATE)) item.State = (item.State & ~nid.dwStateMask) | (nid.dwState & nid.dwStateMask);
        if (flags.HasFlag(NIF.ICON)) item.Icon = ConvertIcon(Win32Ex.FromHandle32(nid.hIcon)) ?? item.Icon;

        if (flags.HasFlag(NIF.INFO) && !string.IsNullOrWhiteSpace(nid.szInfo))
            BalloonReceived?.Invoke(nid.szInfoTitle ?? "", nid.szInfo, item.Tooltip);
    }

    private void SyncVisibility(TrayIconItem item)
    {
        var listed = Icons.Contains(item);
        if (item.IsHidden && listed) Icons.Remove(item);
        else if (!item.IsHidden && !listed) Icons.Add(item);
    }

    /// <summary>The sender owns the HICON and may destroy it right after Shell_NotifyIcon returns: copy, convert, release.</summary>
    private static System.Windows.Media.ImageSource? ConvertIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        var copy = User32.CopyIcon(hIcon);
        if (copy == IntPtr.Zero) return null;
        try { return IconService.FromBorrowedIcon(copy); }
        finally { User32.DestroyIcon(copy); }
    }

    private void PruneDead()
    {
        foreach (var kv in _byKey.Where(k => !User32.IsWindow(k.Value.HWnd)).ToList())
        {
            _byKey.Remove(kv.Key);
            Icons.Remove(kv.Value);
        }
    }

    private RECT? ResolveRect(uint hwnd32, uint uid, Guid guid)
    {
        var item = _byKey.Values.FirstOrDefault(i =>
            (guid != Guid.Empty && i.Guid == guid) ||
            (Win32Ex.FromHandle32(hwnd32) == i.HWnd && i.UId == uid));
        return item is null ? null : RectProvider?.Invoke(item);
    }

    // ── Input forwarding ────────────────────────────────────────────────────────────────────

    /// <summary>Deliver a click to the icon's owner the way Explorer does (both callback conventions).</summary>
    public void SendClick(TrayIconItem item, TrayClick click, POINT screenPoint)
    {
        if (item.CallbackMessage == 0 || !User32.IsWindow(item.HWnd)) return;

        // Let the owner steal foreground so its popup menu / window actually shows.
        User32.GetWindowThreadProcessId(item.HWnd, out var pid);
        User32.AllowSetForegroundWindow(pid);

        switch (click)
        {
            case TrayClick.Left:
                Post(item, WM.LBUTTONDOWN, screenPoint);
                Post(item, WM.LBUTTONUP, screenPoint);
                if (item.Version >= 4) Post(item, WM.USER + 0 /* NIN_SELECT */, screenPoint);
                break;
            case TrayClick.LeftDouble:
                Post(item, WM.LBUTTONDBLCLK, screenPoint);
                Post(item, WM.LBUTTONUP, screenPoint);
                break;
            case TrayClick.Right:
                Post(item, WM.RBUTTONDOWN, screenPoint);
                Post(item, WM.RBUTTONUP, screenPoint);
                if (item.Version >= 4) Post(item, WM.CONTEXTMENU, screenPoint);
                break;
        }
    }

    private static void Post(TrayIconItem item, uint mouseMessage, POINT pt)
    {
        IntPtr wParam, lParam;
        if (item.Version >= 4)
        {
            // NOTIFYICON_VERSION_4: wParam = anchor (x,y); LOWORD(lParam) = event, HIWORD(lParam) = icon id.
            wParam = Win32Ex.MakeLong(pt.X, pt.Y);
            lParam = Win32Ex.MakeLong((int)mouseMessage, (int)item.UId);
        }
        else
        {
            // Legacy: wParam = icon id; lParam = the mouse message itself.
            wParam = (IntPtr)item.UId;
            lParam = (IntPtr)mouseMessage;
        }
        User32.PostMessage(item.HWnd, item.CallbackMessage, wParam, lParam);
    }

    public void Dispose()
    {
        _reaper.Stop();
        Stop();
    }
}
