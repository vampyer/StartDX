using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Media;
using System.Windows.Threading;
using StartDX.Dock.Native;
using StartDX.Dock.ViewModels;

namespace StartDX.Dock.Services;

/// <summary>
/// Active-window task switcher model. Instead of polling, it subscribes to WinEvents (create / destroy / show / hide /
/// name-change / cloak / foreground / minimise) and debounces them into a single diffed refresh, so the list is
/// live yet cheap. Filtering follows the Alt-Tab rules: visible, un-cloaked, un-owned (or WS_EX_APPWINDOW), not a
/// tool window, titled.
/// </summary>
internal sealed class TaskListService : IDisposable
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    private readonly WinEventProc _callback;          // rooted for the lifetime of the hooks
    private readonly List<IntPtr> _hooks = new();
    private readonly DispatcherTimer _debounce;
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    public TaskListService()
    {
        _callback = OnWinEvent;
        const uint Flags = User32.WINEVENT_OUTOFCONTEXT | User32.WINEVENT_SKIPOWNPROCESS;
        Hook(User32.EVENT_SYSTEM_FOREGROUND, User32.EVENT_SYSTEM_FOREGROUND, Flags);
        Hook(User32.EVENT_SYSTEM_MINIMIZESTART, User32.EVENT_SYSTEM_MINIMIZEEND, Flags);
        Hook(User32.EVENT_OBJECT_CREATE, User32.EVENT_OBJECT_HIDE, Flags);
        Hook(User32.EVENT_OBJECT_NAMECHANGE, User32.EVENT_OBJECT_NAMECHANGE, Flags);
        Hook(User32.EVENT_OBJECT_CLOAKED, User32.EVENT_OBJECT_UNCLOAKED, Flags);

        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) =>
        {
            _debounce!.Stop();
            Refresh();
        }, Dispatcher.CurrentDispatcher);

        Refresh();
    }

    public ObservableCollection<TaskItem> Tasks { get; } = new();

    /// <summary>The foreground window that is not one of ours (used to un-toggle "minimise on click").</summary>
    public IntPtr ActiveWindow { get; private set; }

    /// <summary>Fires whenever foreground changes; the dock uses it to re-assert HWND_TOPMOST.</summary>
    public event Action? ForegroundChanged;

    private void Hook(uint min, uint max, uint flags) =>
        _hooks.Add(User32.SetWinEventHook(min, max, IntPtr.Zero, _callback, 0, 0, flags));

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != User32.OBJID_WINDOW || hwnd == IntPtr.Zero) return;
        if (evt == User32.EVENT_SYSTEM_FOREGROUND) ForegroundChanged?.Invoke();
        _debounce.Stop();
        _debounce.Start();
    }

    // ── Refresh (diff) ──────────────────────────────────────────────────────────────────────

    public void Refresh()
    {
        var found = new List<IntPtr>();
        User32.EnumWindows((h, _) => { if (IsTaskWindow(h)) found.Add(h); return true; }, IntPtr.Zero);

        var foreground = User32.GetForegroundWindow();
        ActiveWindow = foreground;

        for (var i = Tasks.Count - 1; i >= 0; i--)
            if (!found.Contains(Tasks[i].Handle)) Tasks.RemoveAt(i);

        // EnumWindows is top-to-bottom Z-order; reversing yields a roughly stable oldest-first taskbar order.
        for (var i = found.Count - 1; i >= 0; i--)
        {
            var h = found[i];
            if (Tasks.Any(t => t.Handle == h)) continue;
            var item = new TaskItem { Handle = h };
            User32.GetWindowThreadProcessId(h, out var pid);
            item.ProcessId = pid;
            item.Icon = WindowIcon(h);           // instant (32px), upgraded below
            Tasks.Add(item);
            _ = UpgradeIconAsync(item);
        }

        foreach (var t in Tasks)
        {
            t.Title = GetTitle(t.Handle);
            t.IsMinimized = User32.IsIconic(t.Handle);
            t.IsActive = t.Handle == foreground;
        }
    }

    private async Task UpgradeIconAsync(TaskItem item)
    {
        var path = Kernel32.GetProcessImagePath(item.ProcessId);
        item.ExePath = path;
        // UWP windows are hosted by ApplicationFrameHost.exe; its own icon would be wrong for every one of them.
        if (path is null || path.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)) return;
        var hd = await IconService.Instance.GetAsync(path, 64);
        if (hd is not null) item.Icon = hd;
    }

    private bool IsTaskWindow(IntPtr h)
    {
        if (!User32.IsWindowVisible(h)) return false;
        var ex = (long)User32.GetWindowLongPtr(h, GWL.EXSTYLE);
        var appWindow = (ex & WSEX.APPWINDOW) != 0;
        if ((ex & WSEX.TOOLWINDOW) != 0 && !appWindow) return false;
        if ((ex & WSEX.NOACTIVATE) != 0 && !appWindow) return false;
        if (User32.GetWindow(h, GW.OWNER) != IntPtr.Zero && !appWindow) return false;
        if (User32.GetWindowTextLength(h) == 0) return false;
        if (Dwm.IsCloaked(h)) return false;

        User32.GetWindowThreadProcessId(h, out var pid);
        if (pid == _ownPid) return false;

        var cls = new StringBuilder(64);
        User32.GetClassName(h, cls, cls.Capacity);
        return !IgnoredClasses.Contains(cls.ToString());
    }

    private static string GetTitle(IntPtr h)
    {
        var len = User32.GetWindowTextLength(h);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        User32.GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static ImageSource? WindowIcon(IntPtr hwnd)
    {
        const uint WM_GETICON = WM.GETICON;
        foreach (var kind in new[] { 1 /* ICON_BIG */, 2 /* ICON_SMALL2 */, 0 /* ICON_SMALL */ })
        {
            User32.SendMessageTimeout(hwnd, WM_GETICON, (IntPtr)kind, IntPtr.Zero,
                User32.SMTO_ABORTIFHUNG, 100, out var h);
            if (h != IntPtr.Zero) return IconService.FromBorrowedIcon(h);
        }
        var cls = User32.GetClassLongPtr(hwnd, -14);   // GCLP_HICON
        return IconService.FromBorrowedIcon(cls);
    }

    // ── Actions ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Taskbar-button semantics: active -> minimise; minimised -> restore; otherwise -> bring to front.</summary>
    public void Toggle(TaskItem task)
    {
        var h = task.Handle;
        if (!User32.IsWindow(h)) { Refresh(); return; }
        if (h == User32.GetForegroundWindow() && !User32.IsIconic(h)) { User32.ShowWindow(h, SW.MINIMIZE); return; }
        Activate(h);
    }

    public static void Activate(IntPtr h)
    {
        if (User32.IsIconic(h)) User32.ShowWindow(h, SW.RESTORE);

        // Foreground-lock workaround: briefly share input state with the current foreground thread.
        var fgThread = User32.GetWindowThreadProcessId(User32.GetForegroundWindow(), out _);
        var me = Kernel32.GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != me && User32.AttachThreadInput(me, fgThread, true);
        try
        {
            User32.BringWindowToTop(h);
            User32.SetForegroundWindow(h);
        }
        finally { if (attached) User32.AttachThreadInput(me, fgThread, false); }
    }

    public static void Minimize(TaskItem t) => User32.ShowWindow(t.Handle, SW.MINIMIZE);
    public static void Maximize(TaskItem t) => User32.ShowWindow(t.Handle, SW.MAXIMIZE);
    public static void Restore(TaskItem t) => User32.ShowWindow(t.Handle, SW.RESTORE);
    public static void Close(TaskItem t) => User32.PostMessage(t.Handle, WM.CLOSE, IntPtr.Zero, IntPtr.Zero);

    public void Dispose()
    {
        _debounce.Stop();
        foreach (var h in _hooks) if (h != IntPtr.Zero) User32.UnhookWinEvent(h);
        _hooks.Clear();
    }
}
