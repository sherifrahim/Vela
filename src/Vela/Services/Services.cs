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
    static readonly Lazy<AudioEndpoint> _speakers = new(() => new AudioEndpoint(0));
    static readonly Lazy<AudioEndpoint> _mic = new(() => new AudioEndpoint(1));
    static readonly Lazy<MediaService> _media = new(() => new MediaService());
    static readonly Lazy<WeatherService> _weather = new(() => new WeatherService());
    static readonly Lazy<ScreenTimeService> _screenTime = new(() => new ScreenTimeService());
    static readonly Lazy<BrightnessService> _brightness = new(() => new BrightnessService());
    static readonly Lazy<RadioService> _radios = new(() => new RadioService());
    static readonly Lazy<DataStore> _data = new(() => new DataStore());
    static readonly Lazy<IconCache> _icons = new(() => new IconCache());
    static readonly Lazy<WindowTracker> _windows = new(() => new WindowTracker());
    static readonly Lazy<TaskbarController> _taskbar = new(() => new TaskbarController());
    static readonly Lazy<ClaudeConnector> _claude = new(() => new ClaudeConnector());
    static readonly Lazy<CodexConnector> _codex = new(() => new CodexConnector());
    static readonly Lazy<GitHubConnector> _github = new(() => new GitHubConnector());
    static readonly Lazy<StripeConnector> _stripe = new(() => new StripeConnector());

    public static SystemMonitor Sys => _sys.Value;
    public static AudioEndpoint Speakers => _speakers.Value;
    public static AudioEndpoint Microphone => _mic.Value;
    public static MediaService Media { get { _media.Value.EnsureStarted(); return _media.Value; } }
    public static WeatherService Weather => _weather.Value;
    public static ScreenTimeService ScreenTime => _screenTime.Value;
    public static BrightnessService Brightness => _brightness.Value;
    public static RadioService Radios => _radios.Value;
    public static DataStore Data => _data.Value;
    public static IconCache Icons => _icons.Value;
    public static WindowTracker Windows => _windows.Value;
    public static TaskbarController Taskbar => _taskbar.Value;
    public static ClaudeConnector Claude => _claude.Value;
    public static CodexConnector Codex => _codex.Value;
    public static GitHubConnector GitHub => _github.Value;
    public static StripeConnector Stripe => _stripe.Value;

    public static IEnumerable<IConnector> AllConnectors => new IConnector[] { Claude, Codex, GitHub, Stripe };

    public static void FlushAll()
    {
        if (_screenTime.IsValueCreated) _screenTime.Value.Flush();
        if (_data.IsValueCreated) _data.Value.Flush();
        Settings.Flush();
    }
}
