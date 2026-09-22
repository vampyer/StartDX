using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Native;

namespace StartDX.Dock.Services;

/// <summary>
/// The system indicators next to the clock - volume, network, battery.
///
/// Why these are not "tray icons": on Windows 10/11 Explorer draws them from its own shell components; they are never sent
/// through Shell_NotifyIcon, so a Shell_TrayWnd host cannot receive them. Every third-party taskbar therefore renders them
/// itself from the underlying system APIs, as done here:
///   volume  -> Core Audio (IAudioEndpointVolume)      network -> System.Net.NetworkInformation + NetworkChange event
///   battery -> GetSystemPowerStatus
/// Glyphs come from Segoe Fluent Icons (Segoe MDL2 Assets on Windows 10).
/// </summary>
internal sealed class SystemStatusService : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _fast;      // volume + battery: cheap, poll every 1.5 s
    private readonly DispatcherTimer _slow;      // network: event-driven, plus a slow safety poll
    private string _volumeGlyph = "", _volumeText = "Volume";
    private string _networkGlyph = "", _networkText = "No network";
    private string _batteryGlyph = "", _batteryText = "";
    private bool _hasBattery;

    public SystemStatusService()
    {
        RefreshVolume();
        RefreshBattery();
        RefreshNetwork();
        RefreshInput();

        _fast = new DispatcherTimer(TimeSpan.FromMilliseconds(1500), DispatcherPriority.Background, (_, _) => { RefreshVolume(); RefreshBattery(); RefreshInput(); }, Dispatcher.CurrentDispatcher);
        _slow = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background, (_, _) => RefreshNetwork(), Dispatcher.CurrentDispatcher);
        _fast.Start();
        _slow.Start();

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    public string VolumeGlyph { get => _volumeGlyph; private set => Set(ref _volumeGlyph, value); }
    public string VolumeText { get => _volumeText; private set => Set(ref _volumeText, value); }
    public string NetworkGlyph { get => _networkGlyph; private set => Set(ref _networkGlyph, value); }
    public string NetworkText { get => _networkText; private set => Set(ref _networkText, value); }
    public string BatteryGlyph { get => _batteryGlyph; private set => Set(ref _batteryGlyph, value); }
    public string BatteryText { get => _batteryText; private set => Set(ref _batteryText, value); }
    public bool HasBattery { get => _hasBattery; private set => Set(ref _hasBattery, value); }

    // ── Input language ("ENG") ──────────────────────────────────────────────────────────────
    private string _inputText = "";
    private bool _hasMultipleInputs;
    public string InputText { get => _inputText; private set => Set(ref _inputText, value); }
    /// <summary>Only shown when more than one keyboard layout is installed, like the stock indicator.</summary>
    public bool HasMultipleInputs { get => _hasMultipleInputs; private set => Set(ref _hasMultipleInputs, value); }

    private void RefreshInput()
    {
        HasMultipleInputs = User32.GetKeyboardLayoutList(0, null) > 1;
        if (!HasMultipleInputs) return;

        // The layout is per-thread: ask for the one belonging to the window the user is typing in.
        var thread = User32.GetWindowThreadProcessId(User32.GetForegroundWindow(), out _);
        var langId = (int)((long)User32.GetKeyboardLayout(thread) & 0xFFFF);
        try { InputText = System.Globalization.CultureInfo.GetCultureInfo(langId).ThreeLetterISOLanguageName.ToUpperInvariant(); }
        catch (System.Globalization.CultureNotFoundException) { InputText = "???"; }
    }

    /// <summary>Same as the stock indicator's click: Win+Space cycles the input method.</summary>
    public static void CycleInputMethod()
    {
        User32.keybd_event((byte)VK.LWIN, 0, 0, UIntPtr.Zero);
        User32.keybd_event(User32.VK_SPACE, 0, 0, UIntPtr.Zero);
        User32.keybd_event(User32.VK_SPACE, 0, User32.KEYEVENTF_KEYUP, UIntPtr.Zero);
        User32.keybd_event((byte)VK.LWIN, 0, User32.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // ── Volume ──────────────────────────────────────────────────────────────────────────────

    public void ToggleMute()
    {
        var vol = CoreAudio.GetDefaultEndpointVolume();
        if (vol is null) return;
        try
        {
            var ctx = Guid.Empty;
            vol.GetMute(out var muted);
            vol.SetMute(!muted, ref ctx);
        }
        finally { Marshal.ReleaseComObject(vol); }
        RefreshVolume();
    }

    /// <summary>Change the master volume by <paramref name="percentPoints"/> (mouse wheel over the icon).</summary>
    public void AdjustVolume(int percentPoints)
    {
        var vol = CoreAudio.GetDefaultEndpointVolume();
        if (vol is null) return;
        try
        {
            var ctx = Guid.Empty;
            vol.GetMasterVolumeLevelScalar(out var level);
            vol.SetMasterVolumeLevelScalar(Math.Clamp(level + percentPoints / 100f, 0f, 1f), ref ctx);
            if (percentPoints > 0) vol.SetMute(false, ref ctx);   // raising the volume un-mutes, like the stock flyout
        }
        finally { Marshal.ReleaseComObject(vol); }
        RefreshVolume();
    }

    private void RefreshVolume()
    {
        var vol = CoreAudio.GetDefaultEndpointVolume();
        if (vol is null) { VolumeGlyph = ""; VolumeText = "No audio device"; return; }
        try
        {
            vol.GetMasterVolumeLevelScalar(out var level);
            vol.GetMute(out var muted);
            var pct = (int)Math.Round(level * 100);
            VolumeGlyph = muted || pct == 0 ? ""       // mute
                        : pct < 34 ? ""                 // 1 wave
                        : pct < 67 ? ""                 // 2 waves
                        : "";                           // 3 waves
            VolumeText = muted ? $"Volume: muted ({pct}%)" : $"Volume: {pct}%";
        }
        finally { Marshal.ReleaseComObject(vol); }
    }

    // ── Network ─────────────────────────────────────────────────────────────────────────────

    private void OnNetworkChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(RefreshNetwork);

    private void RefreshNetwork()
    {
        try
        {
            // "Connected" = an operational, non-virtual adapter that actually has a gateway.
            var online = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                            && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.GetAddressBytes().Any(b => b != 0)))
                .ToList();

            var pick = online.FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    ?? online.FirstOrDefault(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet)
                    ?? online.FirstOrDefault();

            if (pick is null) { NetworkGlyph = ""; NetworkText = "No network connection"; return; }

            var wifi = pick.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            NetworkGlyph = wifi ? "" : "";
            NetworkText = $"{(wifi ? "Wi-Fi" : "Ethernet")}: {pick.Name} - connected";
        }
        catch (NetworkInformationException ex)
        {
            Log.Warn($"Network refresh failed: {ex.Message}");
        }
    }

    // ── Battery ─────────────────────────────────────────────────────────────────────────────

    private void RefreshBattery()
    {
        if (!Kernel32.GetSystemPowerStatus(out var st) || (st.BatteryFlag & 128) != 0 || st.BatteryLifePercent > 100)
        {
            HasBattery = false;          // desktop PC / no battery: hide the icon entirely
            return;
        }

        HasBattery = true;
        var pct = st.BatteryLifePercent;
        var charging = (st.BatteryFlag & 8) != 0 || (st.ACLineStatus == 1 && pct < 100);
        var step = Math.Min(10, pct / 10);                                  // 0..10
        // Segoe MDL2/Fluent: Battery0-9 = E850-E859, Battery10 = E83F; BatteryCharging0-9 = E85A-E862, Charging10 = E83E.
        BatteryGlyph = char.ConvertFromUtf32(charging
            ? (step == 10 ? 0xE83E : 0xE85A + step)
            : (step == 10 ? 0xE83F : 0xE850 + step));
        BatteryText = charging ? $"Battery: {pct}% (charging)" : $"Battery: {pct}%";
    }

    public void Dispose()
    {
        _fast.Stop();
        _slow.Stop();
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
    }
}
