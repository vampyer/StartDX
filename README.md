# StartDX

A Start-menu-and-taskbar replacement for Windows 10/11, built with **WPF on .NET 10**. It registers as a real desktop
appbar, hosts the notification area, restyles itself at runtime from swappable theme dictionaries, and is configured by a
separate settings app over a named-pipe protocol.

```
.\run.ps1              # build + launch the dock
.\run.ps1 -Settings    # ...and the settings app
.\run.ps1 -Stop        # graceful exit (restores the native taskbar)
```

Requires the **.NET 10 SDK** to build and the **.NET 10 Desktop Runtime** to run. x64/ARM64 only.

### Start with Windows
Off by default. Turn it on with the **"Start StartDX when I sign in to Windows"** checkbox in Settings, or **Start with Windows** in the
dock's right-click menu (`runAtStartup` over IPC). It writes a per-user entry -
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\StartDX` = `"<path>\StartDX.Dock.exe" --autostart` - so it needs no admin rights and
affects only your account.
* If you move or replace the exe, the next launch rewrites the entry with the new path.
* If you disable it under **Task Manager > Startup apps**, StartDX respects that: it turns its own setting off and removes the entry
  rather than fighting you. Re-enabling from StartDX overrides an earlier Task Manager "Disable".
* Windows runs Run-key programs a moment after Explorer starts, so at sign-in you may briefly see the stock taskbar before StartDX
  hides it (with *Replace Windows taskbar* on).

### Icons
`StartDX.Dock.exe` uses the 2x2 Start grid over a dock bar; `StartDX.Settings.exe` (and its window) uses a gear - one visual
family (obsidian tile, glassy highlight, cyan -> neon-green art). Both are multi-resolution `.ico` files (16-256 px, each frame drawn
natively; small frames drop fine detail for legibility). Regenerate with `tools\generate-app-icons.ps1`; 256 px previews are in `docs\icons\`.

### Self-contained executables (no .NET needed on the target PC)

```
.\publish.ps1                 # -> publish\StartDX.Dock.exe (~65 MB) + publish\StartDX.Settings.exe (~59 MB), win-x64
.\publish.ps1 -Runtime win-arm64
.\publish.ps1 -ReadyToRun     # pre-compiled: faster cold start, larger files
```

Single-file, compressed, with the .NET runtime bundled. Copy **both** exes to the same folder (the dock launches the settings app
from beside itself). On first launch each exe unpacks WPF's native libraries to `%TEMP%\.net\` (about 6 s once; ~1 s afterwards).
The exes are unsigned, so SmartScreen may warn on a downloaded copy.

## Solution layout

| Project | Type | Role |
|---|---|---|
| `src/StartDX.Shared` | `net10.0` library | `AppSettings`, `ThemeCatalog`, and the IPC protocol/server/client. No UI or Win32 dependency. |
| `src/StartDX.Dock` | `net10.0-windows` WPF exe | The dock, Start menu, notification centre, tray host, theme engine. Hosts the pipe **server**. |
| `src/StartDX.Settings` | `net10.0-windows` WPF exe | Theme picker + dock options. Pipe **client**; works offline by writing `settings.json`. |

Both exes build into `artifacts/<Config>/` so the dock can launch `StartDX.Settings.exe` beside itself.

```
StartDX.Dock/
  Native/       P/Invoke: Win32Types, User32, Shell32 (SHAppBarMessage + tray structs), Dwm, Kernel32Gdi32, ShellCom, CoreAudio
  Services/     AppBarService, ShellTrayHost, TrayService, NativeTaskbarService, TaskListService, IconService,
                AppCatalogService, SystemStatusService, NotificationService, WinKeyHook, IpcBridge, AppServices (composition root)
  Theming/      ThemeManager, WindowBackdrop (DWM blur/acrylic/mica), Motion (theme-driven hover), ThemeKeys
  Themes/       Theme.Contract.xaml, Controls.xaml, Glassmorphism.xaml, PremiumDark.xaml, RetroUpgrade.xaml, NeonGreen.xaml
  Views/        MainWindow (the dock), FlyoutWindow (base), StartMenuWindow, NotificationCenterWindow
  ViewModels/   DockViewModel, StartMenuViewModel, Items
```

## How the pieces work

### Docking (`AppBarService`)
`ABM_NEW` -> `[ABM_QUERYPOS -> claim <thickness> px on the edge -> ABM_SETPOS -> SetWindowPos(HWND_TOPMOST)]*` -> `ABM_REMOVE`.
Windows shrinks every maximised window's work area to fit. Callbacks: `ABN_POSCHANGED` re-docks, `ABN_FULLSCREENAPP` keeps the
bar topmost (or lowers it, per setting). The window is a `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, and topmost is re-asserted on
foreground changes. Dock by dragging any empty part of the bar to another edge/monitor.

### Notification area (`ShellTrayHost`, `TrayService`)
Apps call `Shell_NotifyIcon`, which `FindWindow("Shell_TrayWnd")`s and sends `WM_COPYDATA` (`dwData==1`, a `SHELLTRAYDATA`).
`ShellTrayHost` is a native window of that class: it parses `NIM_ADD/MODIFY/DELETE/SETVERSION`, handles balloon tips
(`NIF_INFO` -> notification centre) and `Shell_NotifyIconGetRect`, and forwards clicks back using each icon's callback message
(both the legacy and `NOTIFYICON_VERSION_4` conventions). Payloads are size-checked and copied into a zeroed buffer - any
process can `WM_COPYDATA` us.

**Replacing the Windows shell UI is on by default** (`replaceNativeTaskbar`, toggle in Settings or the dock's context menu):
Explorer's taskbar is auto-hidden *and* its windows are hidden on every monitor (a 1 s keeper re-hides them if Explorer shows them
again), and the Win key / Ctrl+Esc open StartDX instead of the stock Start menu (Win+E, Win+R, Win+D, Ctrl+Shift+Esc etc. are
untouched). The original taskbar state is saved to `%APPDATA%\StartDX\taskbar-backup.json` *before* anything is changed:
* graceful exit / session end / crash handler restore everything;
* if the process is killed hard, the next start repairs it automatically;
* or run `StartDX.Dock.exe --restore-taskbar` (`.\run.ps1 -RestoreTaskbar`) at any time.

Not touched: the Start menu *process* keeps running (it is just never shown), and other native entry points such as Win+X, Win+S
search, the Xbox button and touch/pen gestures still open their stock UI.

* **Exclusive** (Explorer's taskbar not running): the host simply is the tray.
* **Take-over** (*Replace Windows taskbar*): the host is created above Explorer's `Shell_TrayWnd` in Z-order, Explorer's is sent to
  `HWND_BOTTOM`, `TaskbarCreated` is broadcast so apps re-register, and the native taskbar is set to auto-hide with
  `ABM_SETSTATE`. Because `SHAppBarMessage` also resolves `Shell_TrayWnd`, the dock's own appbar calls are wrapped in
  `ShellTrayHost.RouteToExplorer()`. Everything is reversed on exit (graceful, `ProcessExit`, session end).

### System indicators (`SystemStatusService`)
Volume, network, battery and input language are **not** tray icons on Windows 10/11 (Explorer draws them from its own shell
components, so no third party ever receives them). The dock renders them from the underlying APIs: Core Audio
(`IAudioEndpointVolume`: click = mute, wheel = volume), `NetworkInterface`/`NetworkChange`, `GetSystemPowerStatus`, and
`GetKeyboardLayout`. Battery and input-language hide themselves when not applicable.

### Start menu
Search box, an all-apps list from the virtual `shell:AppsFolder` (classic + packaged apps, launched with ShellExecute), and
*Frequently used* / *Pinned* sections as **128x128 DIP tiles** with 256 px icons from `IShellItemImageFactory`. The Windows key
opens it (`WH_KEYBOARD_LL`, opt-out), Enter launches the top match or runs the typed text.

### Notifications
Three feeds into one list: tray balloons, `notify` IPC messages, and Windows toasts via `UserNotificationListener`
(availability depends on package identity/permission - it logs and stays idle when denied).

## Theming

`Application.Resources.MergedDictionaries` is exactly three slots:

| Slot | File | Swapped? |
|---|---|---|
| 0 | `Theme.Contract.xaml` - a default for **every** token | never |
| 1 | the active theme, e.g. `Glassmorphism.xaml` | **yes** - one indexer assignment |
| 2 | `Controls.xaml` - templates that use tokens only via `DynamicResource` | never |

`ThemeManager.Apply(id)` parses the new dictionary first, then replaces slot 1. Every `DynamicResource` re-resolves in place: no
restart, no flicker, and a broken theme file can never leave the UI unstyled. Window-level effects (acrylic/blur/mica, corners,
DWM border) are applied natively by `WindowBackdrop` and re-applied on `ThemeChanged`; hover motion is read from
`Theme.Motion.*` at animation time.

**Write a theme:** copy `Theme.Contract.xaml`, change values, keep the keys, register it with
`ThemeManager.Instance.Register(descriptor, packUri)`. Omitted keys fall back to the contract (the log lists what a theme inherits).
Tile art: author 256x256 px PNGs (= 128 DIP @2x) and point `Theme.Tile.ArtOverlay` at them; `tools/generate-tile-art.ps1` shows how
the bundled overlays are produced.

> Gotcha found the hard way: don't alias resources with `<StaticResource x:Key="A" ResourceKey="B"/>` entries inside a dictionary -
> the WPF parser silently drops the entry that precedes each one. Define each value explicitly.

## IPC protocol (`StartDX.Shared/Ipc`)

Per-user named pipe `StartDX.Ipc.v1.<UserName>` (`PipeOptions.CurrentUserOnly`), **one JSON document per line**:
`{ "v":1, "type":"...", "id":"...", "payload":{...} }`

| Client -> dock | Payload | Reply |
|---|---|---|
| `getState` | - | `state` |
| `setTheme` | `{ "theme":"PremiumDark" }` | `state` (broadcast to **all** clients) then `ack` |
| `patchSettings` | any subset of `edge, thicknessDip, monitorDevice, alwaysOnTopOverFullscreen, replaceNativeTaskbar, winKeyOpensStartMenu, showTaskLabels, runAtStartup, theme` | `state` (broadcast) + `ack` |
| `notify` | `{ "title","body","source" }` | `ack` |
| `command` | `{ "name": "toggleStart" \| "toggleNotifications" \| "exit" }` | `ack` |
| `ping` | - | `pong` |

Errors come back as `{ "type":"error", "payload":{ "code","message" } }`. Every accepted change is re-broadcast as a full `state`, so
the settings app also follows changes made on the dock itself (drag-to-dock, context menu). `tools/send-ipc.ps1` sends one message
from PowerShell.

## Known limits (please read)

* **Take-over mode brokers only tray traffic.** Appbar messages from *other* programs (`dwData==0`, shared-memory based) that reach the
  dock's `Shell_TrayWnd` are not forwarded to Explorer, so another app registering a *new* appbar while take-over is active will fail.
  Already-registered appbars are unaffected. Tested here with the dock's own appbar and with real tray apps; not with third-party docks.
* **Above full-screen:** the bar stays above borderless full-screen windows. Exclusive-mode full-screen games and elevated windows
  can still cover it; beating that needs a `uiAccess="true"` signed build.
* **Tray icons that registered with Explorer before take-over** reappear only if the app handles `TaskbarCreated` (most do).
* Icon click forwarding follows Explorer's message conventions but depends on each app's window accepting input from us
  (elevated apps ignore messages from a non-elevated dock - normal UIPI behaviour).
* `SetWindowCompositionAttribute` (acrylic/blur) is an undocumented-but-stable API; Mica uses the documented DWM attribute.
* Windows 11's Quick Settings / calendar flyouts cannot be opened programmatically; the indicator buttons open the matching
  `ms-settings:` pages instead.
