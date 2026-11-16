using System.Diagnostics;
using Vela.Connectors;
using Vela.Core;
using Vela.Shell;

namespace Vela.Services;

/// <summary>
/// Service locator. Everything except the ticker is created lazily the first time a widget asks,
/// so services for widgets that aren't on the dock cost no memory.
/// </summary>
public static class Services
{
    public static Ticker Ticker { get; } = new();

    static readonly Lazy<SystemMonitor> _sys = new(() => new SystemMonitor());
    static readonly Lazy<TopProcessService> _topProcesses = new(() => new TopProcessService());
    static readonly Lazy<GpuMonitor> _gpu = new(() => new GpuMonitor());
    static readonly Lazy<AudioEndpoint> _speakers = new(() => new AudioEndpoint(0));
    static readonly Lazy<AudioEndpoint> _mic = new(() => new AudioEndpoint(1));
    static readonly Lazy<MediaService> _media = new(() => new MediaService());
    static readonly Lazy<WeatherService> _weather = new(() => new WeatherService());
    static readonly Lazy<ScreenTimeService> _screenTime = new(() => new ScreenTimeService());
    static readonly Lazy<BrightnessService> _brightness = new(() => new BrightnessService());
    static readonly Lazy<RadioService> _radios = new(() => new RadioService());
    static readonly Lazy<WifiService> _wifi = new(() => new WifiService());
    static readonly Lazy<BluetoothService> _bt = new(() => new BluetoothService());
    static readonly Lazy<QuickSettingsService> _quick = new(() => new QuickSettingsService());
    static readonly Lazy<DataStore> _data = new(() => new DataStore());
    static readonly Lazy<IconCache> _icons = new(() => new IconCache());
    static readonly Lazy<ApplicationJumpListService> _jump = new(() => new ApplicationJumpListService());
    static readonly Lazy<WindowTracker> _windows = new(() => new WindowTracker());
    static readonly Lazy<TaskbarController> _taskbar = new(() => new TaskbarController());
    static readonly Lazy<ClaudeConnector> _claude = new(() => new ClaudeConnector());
    static readonly Lazy<CodexConnector> _codex = new(() => new CodexConnector());
    static readonly Lazy<GitHubConnector> _github = new(() => new GitHubConnector());
    static readonly Lazy<StripeConnector> _stripe = new(() => new StripeConnector());
    // Account widgets (finance, analytics, developer, online, stocks): cheap to create; they only poll while a widget listens.
    static readonly Lazy<MetricConnector[]> _accounts = new(() => new MetricConnector[]
    {
        new PolarConnector(), new LemonSqueezyConnector(), new PaddleConnector(), new GumroadConnector(), new ShopifyConnector(), new DodoConnector(),
        new DeepSeekConnector(), new PlausibleConnector(), new DubConnector(), new LinearConnector(), new SupabaseConnector(), new ResendConnector(),
        new YouTubeConnector(), new LeetCodeConnector(), new GitHubActivityConnector(), new GitHubOverviewConnector(), new FinnhubConnector(),
    });

    public static SystemMonitor Sys => _sys.Value;
    public static TopProcessService TopProcesses => _topProcesses.Value;
    public static GpuMonitor Gpu => _gpu.Value;
    public static AudioEndpoint Speakers => _speakers.Value;
    public static AudioEndpoint Microphone => _mic.Value;
    public static MediaService Media { get { _media.Value.EnsureStarted(); return _media.Value; } }
    public static WeatherService Weather => _weather.Value;
    public static ScreenTimeService ScreenTime => _screenTime.Value;
    public static BrightnessService Brightness => _brightness.Value;
    public static RadioService Radios => _radios.Value;
    public static WifiService Wifi => _wifi.Value;
    public static BluetoothService Bluetooth => _bt.Value;
    public static QuickSettingsService Quick => _quick.Value;
    public static DataStore Data => _data.Value;
    public static IconCache Icons => _icons.Value;
    public static ApplicationJumpListService JumpLists => _jump.Value;
    public static WindowTracker Windows => _windows.Value;
    public static TaskbarController Taskbar => _taskbar.Value;
    public static ClaudeConnector Claude => _claude.Value;
    public static CodexConnector Codex => _codex.Value;
    public static GitHubConnector GitHub => _github.Value;
    public static StripeConnector Stripe => _stripe.Value;
    public static IReadOnlyList<MetricConnector> Accounts => _accounts.Value;
    public static MetricConnector Account(string id) => Accounts.First(a => a.Id == id);

    // GitHub Activity shares the GitHub token, so it has no row of its own on the Connections page.
    public static IEnumerable<IConnector> AllConnectors => new IConnector[] { Claude, Codex, GitHub, Stripe }.Concat(Accounts.Where(a => a.Id is not ("githubactivity" or "githuboverview")));

    public static void FlushAll()
    {
        if (_screenTime.IsValueCreated) _screenTime.Value.Flush();
        if (_data.IsValueCreated) _data.Value.Flush();
        Settings.Flush();
    }
}

public sealed record TopProcess(string Name, string? Path, double Cpu, long Memory);

/// <summary>
/// One process snapshot shared by every Top CPU / Top RAM widget on every display.
/// Sampling runs off the UI thread every 10 seconds and only while at least one widget listens.
/// </summary>
public sealed class TopProcessService
{
    sealed record PreviousProcess(long Started, string ProcessName, TimeSpan Cpu, string? Path);
    sealed record Snapshot(DateTime At, Dictionary<int, PreviousProcess> Processes,
        IReadOnlyList<TopProcess> Cpu, IReadOnlyList<TopProcess> Ram);

    readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<int, PreviousProcess> _previous = new();
    DateTime _lastAt;
    int _seconds;
    int _generation;
    bool _busy;
    Action? _updated;

    public IReadOnlyList<TopProcess> ByCpu { get; private set; } = Array.Empty<TopProcess>();
    public IReadOnlyList<TopProcess> ByRam { get; private set; } = Array.Empty<TopProcess>();

    public event Action? Updated
    {
        add
        {
            bool first = _updated == null;
            _updated += value;
            if (first)
            {
                _generation++;
                _seconds = 0;
                Services.Ticker.Tick += OnTick;
                Refresh();
            }
        }
        remove
        {
            _updated -= value;
            if (_updated == null)
            {
                _generation++;
                Services.Ticker.Tick -= OnTick;
                _previous = new();
                _lastAt = default;
            }
        }
    }

    void OnTick(DateTime _)
    {
        if (++_seconds < 10) return;
        _seconds = 0;
        Refresh();
    }

    async void Refresh()
    {
        if (_busy) return;
        _busy = true;
        int generation = _generation;
        try
        {
            var previous = _previous;
            var lastAt = _lastAt;
            var snapshot = await Task.Run(() => Capture(previous, lastAt));
            if (generation != _generation || _updated == null) return;
            _previous = snapshot.Processes;
            _lastAt = snapshot.At;
            ByCpu = snapshot.Cpu;
            ByRam = snapshot.Ram;
            _updated?.Invoke();
        }
        catch (Exception ex) { Log.Warn("Top processes: " + ex.Message); }
        finally
        {
            _busy = false;
            // If the last listener detached and a new one attached during Capture, discard the
            // stale snapshot above and immediately seed the new subscription.
            if (generation != _generation && _updated != null) Refresh();
        }
    }

    Snapshot Capture(Dictionary<int, PreviousProcess> previous, DateTime lastAt)
    {
        var now = DateTime.UtcNow;
        double wall = Math.Max(1, (now - lastAt).TotalMilliseconds) * Environment.ProcessorCount;
        var current = new Dictionary<int, PreviousProcess>();
        var rows = new List<TopProcess>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == 0) continue;
                var cpu = process.TotalProcessorTime;
                string processName = process.ProcessName;
                long started = 0;
                try { started = process.StartTime.ToUniversalTime().Ticks; } catch { }
                bool same = previous.TryGetValue(process.Id, out var old) && old.ProcessName == processName
                    && (started == 0 || old.Started == 0 || started == old.Started) && cpu >= old.Cpu;
                string? path = same ? old!.Path : null;
                if (!same)
                {
                    try { path = process.MainModule?.FileName; } catch { }
                }
                double percent = same && lastAt != default ? (cpu - old!.Cpu).TotalMilliseconds / wall * 100 : 0;
                current[process.Id] = new PreviousProcess(started, processName, cpu, path);
                rows.Add(new TopProcess(Friendly(processName, path), path, Math.Max(0, percent), process.WorkingSet64));
            }
            catch { }
            finally { process.Dispose(); }
        }

        var grouped = rows.GroupBy(r => r.Name).Select(g => new TopProcess(
            g.Key, g.Select(x => x.Path).FirstOrDefault(x => x != null), g.Sum(x => x.Cpu), g.Sum(x => x.Memory))).ToList();
        return new Snapshot(now, current,
            grouped.OrderByDescending(x => x.Cpu).Take(3).ToList(),
            grouped.OrderByDescending(x => x.Memory).Take(3).ToList());
    }

    string Friendly(string processName, string? path)
    {
        if (path == null) return processName;
        if (_names.TryGetValue(path, out var name)) return name;
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            name = string.IsNullOrWhiteSpace(description) ? processName : description.Trim();
        }
        catch { name = processName; }
        return _names[path] = name;
    }
}
