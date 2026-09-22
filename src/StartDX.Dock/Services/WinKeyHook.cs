using System.Runtime.InteropServices;
using System.Windows;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Native;

namespace StartDX.Dock.Services;

/// <summary>
/// Low-level keyboard hook that routes Windows' two "open Start" gestures to StartDX and keeps the stock Start menu from
/// opening:
///   * a lone tap of the Windows key  (Win+E, Win+R, Win+D... are untouched: they set the "another key was pressed" flag)
///   * Ctrl+Esc                       (swallowed outright; Ctrl+Shift+Esc = Task Manager is left alone)
///
/// Win-tap suppression: just before the Win key-up is forwarded we inject an unassigned virtual key (0xE8). Windows then sees
/// "Win + something" instead of a bare Win press and does not open Start. Our own injected keys carry a marker in
/// dwExtraInfo so the hook ignores exactly those - and still sees keys synthesized by other tools (remappers, AutoHotkey).
/// The callback does nothing slow: hooks that stall are silently removed by the OS.
/// </summary>
internal sealed class WinKeyHook : IDisposable
{
    private static readonly UIntPtr OwnMarker = (UIntPtr)0x53445830; // "SDX0"

    private readonly HookProc _proc;      // rooted delegate
    private IntPtr _hook;
    private bool _winDown, _otherKey;

    public WinKeyHook() => _proc = Callback;

    /// <summary>Raised on the UI thread when Start should open (Win tap or Ctrl+Esc).</summary>
    public event Action? WinKeyTapped;

    public bool Enabled
    {
        get => _hook != IntPtr.Zero;
        set
        {
            if (value == Enabled) return;
            if (value)
            {
                _hook = User32.SetWindowsHookEx(User32.WH_KEYBOARD_LL, _proc, Kernel32.GetModuleHandle(null), 0);
                if (_hook == IntPtr.Zero) Log.Warn($"Keyboard hook failed: {Marshal.GetLastWin32Error()}");
            }
            else
            {
                User32.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
                _winDown = _otherKey = false;
            }
        }
    }

    private static bool IsDown(int vk) => (User32.GetAsyncKeyState(vk) & 0x8000) != 0;

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (kb.dwExtraInfo != OwnMarker)
            {
                var msg = (uint)wParam;
                var down = msg is WM.KEYDOWN or WM.SYSKEYDOWN;
                var up = msg is WM.KEYUP or WM.SYSKEYUP;

                if (kb.vkCode is VK.LWIN or VK.RWIN)
                {
                    if (down && !_winDown) { _winDown = true; _otherKey = false; }
                    else if (up)
                    {
                        var tap = _winDown && !_otherKey;
                        _winDown = false;
                        if (tap)
                        {
                            User32.keybd_event(VK.UNASSIGNED, 0, 0, OwnMarker);
                            User32.keybd_event(VK.UNASSIGNED, 0, User32.KEYEVENTF_KEYUP, OwnMarker);
                            Application.Current?.Dispatcher.BeginInvoke(() => WinKeyTapped?.Invoke());
                        }
                    }
                }
                else if (down)
                {
                    if (_winDown) _otherKey = true;

                    // Ctrl+Esc (no Shift/Alt/Win): the legacy "open Start" chord. Block it so the stock menu never appears.
                    if (kb.vkCode == VK.ESCAPE && IsDown(VK.CONTROL) && !IsDown(VK.SHIFT) && !IsDown(VK.MENU) && !_winDown)
                    {
                        Application.Current?.Dispatcher.BeginInvoke(() => WinKeyTapped?.Invoke());
                        return (IntPtr)1;
                    }
                }
            }
        }
        return User32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Enabled = false;
}
