using Vela.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFi;
using Windows.Security.Credentials;

namespace Vela.Services;

/// <summary>One Wi-Fi network as shown in the Wi-Fi card (strongest BSSID per SSID).</summary>
public sealed record WifiNetwork(string Ssid, int Bars, bool Secured, bool Connected, WiFiAvailableNetwork Raw);

/// <summary>
/// Wi-Fi list / connect / disconnect (Windows.Devices.WiFi). Used only while the Wi-Fi card is open:
/// nothing polls in the background. Scanning needs Windows' location permission for desktop apps on
/// Windows 11 24H2+ (Settings → Privacy → Location → "Let desktop apps access your location"); without it
/// the list comes back empty and the card says so.
/// </summary>
public sealed class WifiService
{
    WiFiAdapter? _adapter;
    public string? Problem { get; private set; }

    async Task<WiFiAdapter?> AdapterAsync()
    {
        if (_adapter != null) return _adapter;
        try
        {
            var access = await WiFiAdapter.RequestAccessAsync();
            if (access != WiFiAccessStatus.Allowed) { Problem = "Windows didn't allow Wi-Fi access (" + access + ")."; return null; }
            var all = await WiFiAdapter.FindAllAdaptersAsync();
            _adapter = all.FirstOrDefault();
            if (_adapter == null) Problem = "No Wi-Fi adapter found.";
            return _adapter;
        }
        catch (Exception ex) { Problem = "Wi-Fi unavailable: " + ex.Message; Log.Warn(Problem); return null; }
    }

    public async Task<string?> ConnectedSsidAsync()
    {
        var a = await AdapterAsync();
        if (a == null) return null;
        try
        {
            var p = await a.NetworkAdapter.GetConnectedProfileAsync();
            if (p == null) return null;
            return p.IsWlanConnectionProfile ? p.WlanConnectionProfileDetails.GetConnectedSsid() : p.ProfileName;
        }
        catch { return null; }
    }

    /// <summary>Scan and return networks, connected first, then by signal. Empty list + <see cref="Problem"/> on failure.</summary>
    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync()
    {
        Problem = null;
        var a = await AdapterAsync();
        if (a == null) return Array.Empty<WifiNetwork>();
        try
        {
            await a.ScanAsync();
            var connected = await ConnectedSsidAsync();
            var list = a.NetworkReport.AvailableNetworks
                .Where(n => !string.IsNullOrWhiteSpace(n.Ssid))
                .GroupBy(n => n.Ssid)
                .Select(g => g.OrderByDescending(n => n.SignalBars).First())
                .Select(n => new WifiNetwork(n.Ssid, n.SignalBars, n.SecuritySettings.NetworkAuthenticationType is not
                    (Windows.Networking.Connectivity.NetworkAuthenticationType.Open80211 or Windows.Networking.Connectivity.NetworkAuthenticationType.None),
                    n.Ssid == connected, n))
                .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Bars).ThenBy(n => n.Ssid)
                .ToList();
            if (list.Count == 0) Problem = "No networks visible. On Windows 11, Wi-Fi scanning needs location access for desktop apps.";
            return list;
        }
        catch (UnauthorizedAccessException)
        {
            Problem = "Wi-Fi scanning needs location access for desktop apps.";
            return Array.Empty<WifiNetwork>();
        }
        catch (Exception ex) { Problem = "Scan failed: " + ex.Message; Log.Warn(Problem); return Array.Empty<WifiNetwork>(); }
    }

    /// <summary>Connect; password null = use the saved profile (or an open network).</summary>
    public async Task<string?> ConnectAsync(WifiNetwork n, string? password)
    {
        var a = await AdapterAsync();
        if (a == null) return Problem;
        try
        {
            var result = password is { Length: > 0 }
                ? await a.ConnectAsync(n.Raw, WiFiReconnectionKind.Automatic, new PasswordCredential { Password = password })
                : await a.ConnectAsync(n.Raw, WiFiReconnectionKind.Automatic);
            return result.ConnectionStatus == WiFiConnectionStatus.Success ? null : result.ConnectionStatus switch
            {
                WiFiConnectionStatus.InvalidCredential => "Wrong password.",
                WiFiConnectionStatus.NetworkNotAvailable => "Network not available.",
                WiFiConnectionStatus.Timeout => "Timed out.",
                WiFiConnectionStatus.AccessRevoked => "Access revoked.",
                _ => "Couldn't connect.",
            };
        }
        catch (Exception ex) { Log.Warn("Wi-Fi connect: " + ex.Message); return "Couldn't connect: " + ex.Message; }
    }

    public async Task DisconnectAsync()
    {
        var a = await AdapterAsync();
        try { a?.Disconnect(); } catch (Exception ex) { Log.Warn("Wi-Fi disconnect: " + ex.Message); }
    }
}

/// <summary>A Bluetooth device row in the Bluetooth card.</summary>
public sealed class BtDevice
{
    public required string Id { get; init; }
    public string Name { get; set; } = "";
    public bool Paired { get; set; }
    public bool Connected { get; set; }
    public bool LowEnergy { get; init; }
    public string Glyph { get; set; } = "";
    /// <summary>Battery % reported by the device (hands-free / HID battery), or null.</summary>
    public int? Battery { get; set; }
    public Guid? Container { get; set; }
    public DeviceInformation? Info { get; set; }
}

/// <summary>
/// Paired devices (with connection state) and discovery of new ones, via association endpoints.
/// Discovery runs only while the Bluetooth card is open (<see cref="StartDiscovery"/> / <see cref="StopDiscovery"/>).
/// </summary>
public sealed class BluetoothService
{
    const string IsConnected = "System.Devices.Aep.IsConnected";
    const string Category = "System.Devices.Aep.Category";
    const string ContainerId = "System.Devices.Aep.ContainerId";
    /// <summary>DEVPKEY_Bluetooth_Battery-style key Windows sets on the device node of headsets / HID devices.</summary>
    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    static readonly string[] Props = { IsConnected, Category, ContainerId, "System.Devices.Aep.SignalStrength" };

    DeviceWatcher? _watcher;
    readonly Dictionary<string, BtDevice> _found = new();
    public event Action? DiscoveryChanged;
    public IReadOnlyCollection<BtDevice> Discovered => _found.Values;

    public async Task<IReadOnlyList<BtDevice>> PairedAsync()
    {
        var list = new List<BtDevice>();
        try
        {
            foreach (var (sel, le) in new[] { (BluetoothDevice.GetDeviceSelectorFromPairingState(true), false), (BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), true) })
            {
                var infos = await DeviceInformation.FindAllAsync(sel, Props, DeviceInformationKind.AssociationEndpoint);
                foreach (var i in infos)
                {
                    if (string.IsNullOrWhiteSpace(i.Name)) continue;
                    list.Add(Make(i, le));
                }
            }
        }
        catch (Exception ex) { Log.Warn("Bluetooth list: " + ex.Message); }
        // Battery levels live on the device nodes inside the same container.
        foreach (var d in list.Where(d => d.Container != null))
        {
            try
            {
                var nodes = await DeviceInformation.FindAllAsync($"System.Devices.ContainerId:=\"{{{d.Container}}}\"", new[] { BatteryKey }, DeviceInformationKind.Device);
                foreach (var n in nodes)
                    if (n.Properties.TryGetValue(BatteryKey, out var v) && v is byte b && b <= 100) { d.Battery = b; break; }
            }
            catch { }
        }
        // Classic + LE entries of the same device: keep the connected/classic one.
        return list.GroupBy(d => d.Name).Select(g => g.OrderByDescending(d => d.Connected).ThenBy(d => d.LowEnergy).First())
                   .OrderByDescending(d => d.Connected).ThenBy(d => d.Name).ToList();
    }

    static BtDevice Make(DeviceInformation i, bool le) => new()
    {
        Id = i.Id, Name = i.Name, LowEnergy = le, Info = i,
        Paired = i.Pairing?.IsPaired ?? false,
        Connected = i.Properties.TryGetValue(IsConnected, out var c) && c is true,
        Glyph = GlyphFor(i),
        Container = i.Properties.TryGetValue(ContainerId, out var cid) && cid is Guid g ? g : null,
    };

    static string GlyphFor(DeviceInformation i)
    {
        var n = i.Name.ToLowerInvariant();
        string cat = i.Properties.TryGetValue(Category, out var v) && v is string[] arr ? string.Join(' ', arr).ToLowerInvariant() : "";
        if (cat.Contains("audio") || n.Contains("buds") || n.Contains("airpods") || n.Contains("headphone") || n.Contains("headset") || n.Contains("wh-") || n.Contains("wf-")) return "";
        if (cat.Contains("speaker") || n.Contains("speaker") || n.Contains("soundbar")) return "";
        if (cat.Contains("mouse") || n.Contains("mouse") || n.Contains("mx ")) return "";
        if (cat.Contains("keyboard") || n.Contains("keyboard") || n.Contains("keys")) return "";
        if (cat.Contains("phone") || n.Contains("phone") || n.Contains("galaxy") || n.Contains("pixel")) return "";
        if (cat.Contains("gamecontroller") || n.Contains("controller") || n.Contains("xbox") || n.Contains("dualsense")) return "";
        return "";
    }

    public void StartDiscovery()
    {
        StopDiscovery();
        _found.Clear();
        try
        {
            // Unpaired classic + LE devices that advertise a name.
            const string aqs = "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" OR System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\") AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#False";
            _watcher = DeviceInformation.CreateWatcher(aqs, Props, DeviceInformationKind.AssociationEndpoint);
            _watcher.Added += (_, i) => { if (!string.IsNullOrWhiteSpace(i.Name)) Post(() => { _found[i.Id] = Make(i, false); DiscoveryChanged?.Invoke(); }); };
            _watcher.Updated += (_, u) => Post(() => { if (_found.TryGetValue(u.Id, out var d) && d.Info != null) { d.Info.Update(u); d.Name = d.Info.Name; DiscoveryChanged?.Invoke(); } });
            _watcher.Removed += (_, u) => Post(() => { if (_found.Remove(u.Id)) DiscoveryChanged?.Invoke(); });
            _watcher.Start();
        }
        catch (Exception ex) { Log.Warn("Bluetooth discovery: " + ex.Message); }
    }

    public void StopDiscovery()
    {
        if (_watcher == null) return;
        try { if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) _watcher.Stop(); } catch { }
        _watcher = null;
    }

    static void Post(Action a) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(a);

    /// <summary>Pair; Windows shows its own confirmation / PIN prompt when the device needs one.</summary>
    public async Task<string?> PairAsync(BtDevice d)
    {
        try
        {
            var info = d.Info ?? await DeviceInformation.CreateFromIdAsync(d.Id);
            var r = await info.Pairing.PairAsync();
            return r.Status is DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired ? null : "Pairing failed (" + r.Status + ").";
        }
        catch (Exception ex) { Log.Warn("Bluetooth pair: " + ex.Message); return "Pairing failed: " + ex.Message; }
    }

    public async Task<string?> UnpairAsync(BtDevice d)
    {
        try
        {
            var info = d.Info ?? await DeviceInformation.CreateFromIdAsync(d.Id);
            var r = await info.Pairing.UnpairAsync();
            return r.Status is DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired ? null : "Couldn't remove (" + r.Status + ").";
        }
        catch (Exception ex) { Log.Warn("Bluetooth unpair: " + ex.Message); return "Couldn't remove: " + ex.Message; }
    }
}

/// <summary>
/// The control centre's extra switches: airplane mode (all radios), the Windows power mode
/// (the same overlay scheme as Settings → Power → Power mode) and battery-saver status.
/// </summary>
public sealed class QuickSettingsService
{
    [System.Runtime.InteropServices.DllImport("powrprof.dll")] static extern uint PowerGetEffectiveOverlayScheme(out Guid scheme);
    [System.Runtime.InteropServices.DllImport("powrprof.dll")] static extern uint PowerSetActiveOverlayScheme(Guid scheme);

    public static readonly (string Name, Guid Id)[] PowerModes =
    {
        ("Efficiency", new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a")),
        ("Balanced", Guid.Empty),
        ("Performance", new Guid("ded574b5-45a0-4f42-8737-46345c09c238")),
    };

    /// <summary>Index into <see cref="PowerModes"/>, or -1 when the API isn't available (desktop PCs on older builds).</summary>
    public int PowerMode
    {
        get
        {
            try
            {
                if (PowerGetEffectiveOverlayScheme(out var g) != 0) return -1;
                int i = Array.FindIndex(PowerModes, m => m.Id == g);
                return i < 0 ? 1 : i;
            }
            catch { return -1; }
        }
        set
        {
            try { if (value >= 0 && value < PowerModes.Length) PowerSetActiveOverlayScheme(PowerModes[value].Id); }
            catch (Exception ex) { Log.Warn("Power mode: " + ex.Message); }
        }
    }

    public bool BatterySaverOn
    {
        get { try { return Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On; } catch { return false; } }
    }
}
