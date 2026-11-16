using Vela.Core;

namespace Vela.Widgets;

public sealed record WidgetDescriptor(string Type, string Name, string Category, Func<WidgetBase> Factory, string? Connector = null, bool Hidden = false);

/// <summary>
/// The catalogue shown in the widget picker. To add a widget: write a WidgetBase subclass and add
/// one line here. Type ids are persisted in settings.json — never rename an existing one.
/// </summary>
public static class WidgetRegistry
{
    public static readonly string[] Categories =
    {
        "Clocks", "Productivity", "System", "Apps & Files", "Media & Audio", "AI", "Finance", "Stocks", "Weather", "Online & Mail",
    };

    public static readonly IReadOnlyList<WidgetDescriptor> All = new List<WidgetDescriptor>
    {
        // Clocks
        new("clock", "Clock", "Clocks", () => new ClockWidget()),
        new("clock.digital", "Big Clock", "Clocks", () => new BigClockWidget()),
        new("date", "Day Number", "Clocks", () => new DayNumberWidget()),
        new("calendar", "Calendar", "Clocks", () => new CalendarWidget()),
        new("dayprogress", "Day Progress", "Clocks", () => new ProgressRingWidget(ProgressRingWidget.Span.Day)),
        new("monthprogress", "Month Progress", "Clocks", () => new ProgressRingWidget(ProgressRingWidget.Span.Month)),
        new("yearprogress", "Year Progress", "Clocks", () => new YearDotsWidget()),
        new("clock.flip", "Flip Clock", "Clocks", () => new FlipClockWidget()),
        new("clock.analog", "Analog Minimal", "Clocks", () => new AnalogClockWidget()),
        new("clock.world", "World Clocks", "Clocks", () => new WorldClocksWidget()),

        // Productivity
        new("screentime", "Screen Time", "Productivity", () => new ScreenTimeWidget()),
        new("pomodoro", "Pomodoro", "Productivity", () => new PomodoroWidget()),
        new("timer", "Timer", "Productivity", () => new TimerWidget()),
        new("stopwatch", "Stopwatch", "Productivity", () => new StopwatchWidget()),
        new("todos", "Todos", "Productivity", () => new TodosWidget()),
        new("notes", "Notes", "Productivity", () => new NotesWidget()),
        new("currency", "Currency", "Productivity", () => new CurrencyWidget()),

        // System
        new("sysstats", "System Stats", "System", () => new SystemStatsWidget()),
        new("sysmon", "System Monitor", "System", () => new SystemMonitorWidget()),
        new("topcpu", "Top CPU", "System", () => new TopProcessesWidget(true)),
        new("topram", "Top RAM", "System", () => new TopProcessesWidget(false)),
        new("cpu", "CPU", "System", () => new SingleStatWidget(SingleStatWidget.Stat.Cpu)),
        new("ram", "RAM", "System", () => new SingleStatWidget(SingleStatWidget.Stat.Ram)),
        new("network", "Network", "System", () => new NetworkWidget()),
        new("battery", "Battery", "System", () => new BatteryWidget()),
        new("battery.dots", "Battery Dots", "System", () => new BatteryDotsWidget()),
        new("battery.ring", "Battery Ring", "System", () => new BatteryRingWidget()),
        new("quicktoggles", "Quick Toggles", "System", () => new QuickTogglesWidget()),
        new("brightness", "Brightness", "System", () => new BrightnessWidget()),
        new("themetoggle", "Theme Toggle", "System", () => new ActionWidget(ActionWidget.Kind.Theme)),
        new("notifications", "Notifications", "System", () => new ActionWidget(ActionWidget.Kind.Notifications)),
        new("showdesktop", "Show Desktop", "System", () => new ActionWidget(ActionWidget.Kind.ShowDesktop)),
        new("taskmanager", "Task Manager", "System", () => new ActionWidget(ActionWidget.Kind.TaskManager)),
        new("controlcenter", "Control Centre", "System", () => new ControlCenterWidget()),

        // Apps & Files
        new("apps", "Apps", "Apps & Files", () => new AppsWidget()),
        new("storage", "Storage", "Apps & Files", () => new StorageWidget()),
        new("recyclebin", "Recycle Bin", "Apps & Files", () => new RecycleBinWidget()),
        new("downloads", "Downloads Stack", "Apps & Files", () => new DownloadsWidget()),

        // Media & Audio
        new("nowplaying", "Now Playing", "Media & Audio", () => new NowPlayingWidget()),
        new("nowplaying.mini", "Now Playing Mini", "Media & Audio", () => new NowPlayingMiniWidget()),
        new("nowplaying.art", "Album Art", "Media & Audio", () => new AlbumArtWidget()),
        new("volume", "Audio Output", "Media & Audio", () => new VolumeWidget()),
        new("microphone", "Microphone", "Media & Audio", () => new MicrophoneWidget()),

        // AI
        new("claude", "Claude Plan Usage", "AI", () => new ClaudeWidget(), "claude"),
        new("codex", "ChatGPT Usage", "AI", () => new CodexWidget(), "codex"),
        new("ailimits", "AI Limits", "AI", () => new AiLimitsWidget()),

        // Finance / Stocks
        new("stripe", "Stripe", "Finance", () => new StripeWidget(), "stripe"),
        new("crypto", "Crypto", "Stocks", () => new CryptoWidget()),

        // Weather
        new("weather", "Weather", "Weather", () => new WeatherWidget()),
        new("weather.sun", "Sunrise & Sunset", "Weather", () => new SunWidget()),
        new("weather.status", "Weather Status", "Weather", () => new WeatherStatusWidget()),
        new("weather.hourly", "Hourly", "Weather", () => new HourlyWidget()),
        new("weather.forecast", "Today & Forecast", "Weather", () => new ForecastWidget()),

        // Online
        new("github", "GitHub", "Online & Mail", () => new GitHubWidget(), "github"),

        // Containers (not in the picker; created from widget menus)
        new("stack", "Stack", "Layout", () => new StackWidget(), Hidden: true),
    };

    public static WidgetDescriptor? Get(string type) => All.FirstOrDefault(d => d.Type == type);

    public static WidgetBase? Create(WidgetInstance instance, bool preview = false)
    {
        var d = Get(instance.Type);
        if (d == null) { Log.Warn($"Unknown widget type '{instance.Type}' skipped"); return null; }
        try
        {
            var w = d.Factory();
            w.Instance = instance;
            w.Descriptor = d;
            w.IsPreview = preview;
            w.Build();
            return w;
        }
        catch (Exception ex)
        {
            Log.Error($"Widget '{instance.Type}' failed to build", ex);
            return null;
        }
    }
}
