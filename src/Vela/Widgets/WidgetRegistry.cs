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
        "Clocks", "Productivity", "System", "Apps & Files", "Media & Audio", "AI", "Finance", "Analytics", "Developer", "Stocks", "Weather", "Online & Mail", "Social",
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
        new("date.full", "Date", "Clocks", () => new DateWidget()),
        new("today", "Today", "Clocks", () => new TodayWidget()),
        new("clock.week", "7 Days", "Clocks", () => new SevenDaysWidget()),
        new("clock.segment", "Segment Clock", "Clocks", () => new SegmentClockWidget()),
        new("clock.weather", "Clock & Weather", "Clocks", () => new ClockWeatherWidget()),
        new("clock.worldtime", "World Time", "Clocks", () => new WorldTimeWidget()),
        new("clock.world", "World Clocks", "Clocks", () => new WorldClocksWidget()),

        // Productivity
        new("screentime", "Screen Time", "Productivity", () => new ScreenTimeWidget()),
        new("pomodoro", "Pomodoro", "Productivity", () => new PomodoroWidget()),
        new("timer", "Timer", "Productivity", () => new TimerWidget()),
        new("stopwatch", "Stopwatch", "Productivity", () => new StopwatchWidget()),
        new("daytimeline", "Day Timeline", "Productivity", () => new DayTimelineWidget()),
        new("whatsapp", "WhatsApp", "Productivity", () => new WhatsAppWidget()),
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
        new("battery.gauge", "Battery Gauge", "System", () => new BatteryGaugeWidget()),
        new("battery.core", "Battery Core", "System", () => new BatteryCoreWidget()),
        new("keyboard", "Keyboard", "System", () => new KeyboardWidget()),
        new("language", "Language", "System", () => new LanguageWidget()),
        new("quicktoggles", "Quick Toggles", "System", () => new QuickTogglesWidget()),
        new("brightness", "Brightness", "System", () => new BrightnessWidget()),
        new("themetoggle", "Theme Toggle", "System", () => new ActionWidget(ActionWidget.Kind.Theme)),
        new("notifications", "Notifications", "System", () => new NotificationsWidget()),
        new("tray", "Tray", "System", () => new TrayWidget()),
        new("showdesktop", "Show Desktop", "System", () => new ActionWidget(ActionWidget.Kind.ShowDesktop)),
        new("taskmanager", "Task Manager", "System", () => new ActionWidget(ActionWidget.Kind.TaskManager)),
        new("terminal", "Terminal", "System", () => new TerminalWidget()),
        new("controlcenter", "Control Centre", "System", () => new ControlCenterWidget()),

        // Apps & Files
        new("apps", "Apps", "Apps & Files", () => new AppsWidget()),
        new("storage", "Storage", "Apps & Files", () => new StorageWidget()),
        new("recyclebin", "Recycle Bin", "Apps & Files", () => new RecycleBinWidget()),
        new("recent", "Recently Opened", "Apps & Files", () => new RecentFilesWidget()),
        new("search", "Search", "Apps & Files", () => new SearchWidget()),
        new("commands", "Commands", "Apps & Files", () => new CommandsWidget()),
        new("screenshot", "Screenshot Capture", "Apps & Files", () => new ScreenshotWidget()),
        new("downloads", "Downloads Stack", "Apps & Files", () => new DownloadsWidget()),

        // Media & Audio
        new("nowplaying", "Now Playing", "Media & Audio", () => new NowPlayingWidget()),
        new("nowplaying.compact", "Now Playing (compact)", "Media & Audio", () => new NowPlayingCompactWidget()),
        new("nowplaying.mini", "Now Playing Mini", "Media & Audio", () => new NowPlayingMiniWidget()),
        new("nowplaying.bar", "Now Playing Bar", "Media & Audio", () => new NowPlayingBarWidget()),
        new("nowplaying.art", "Album Art", "Media & Audio", () => new AlbumArtWidget()),
        new("volume", "Audio Output", "Media & Audio", () => new VolumeWidget()),
        new("microphone", "Microphone", "Media & Audio", () => new MicrophoneWidget()),

        // AI
        new("claude", "Claude Plan Usage", "AI", () => new ClaudeWidget(), "claude"),
        new("claude.tokens", "Claude Usage", "AI", () => new ClaudeTokensWidget()),
        new("codex", "ChatGPT Usage", "AI", () => new CodexWidget(), "codex"),
        new("deepseek", "DeepSeek Balance", "AI", () => new MetricWidget("deepseek"), "deepseek"),
        new("glm", "GLM Usage", "AI", () => new MetricWidget("glm"), "glm"),
        new("opencode", "OpenCode Usage", "AI", () => new MetricWidget("opencode", chart: true), "opencode"),
        new("ailimits", "AI Limits", "AI", () => new AiLimitsWidget()),

        // Finance / Stocks
        new("stripe", "Stripe", "Finance", () => new StripeWidget(), "stripe"),
        new("stripe.chart", "Stripe Chart", "Finance", () => new StripeWidget(chart: true), "stripe"),
        new("revenue.total", "Total Revenue", "Finance", () => new TotalRevenueWidget()),
        new("polar", "Polar", "Finance", () => new MetricWidget("polar"), "polar"),
        new("polar.chart", "Polar Chart", "Finance", () => new MetricWidget("polar", chart: true), "polar"),
        new("dodo", "Dodo Payments", "Finance", () => new MetricWidget("dodo"), "dodo"),
        new("dodo.chart", "Dodo Chart", "Finance", () => new MetricWidget("dodo", chart: true), "dodo"),
        new("shopify", "Shopify", "Finance", () => new MetricWidget("shopify"), "shopify"),
        new("shopify.chart", "Shopify Chart", "Finance", () => new MetricWidget("shopify", chart: true), "shopify"),
        new("lemonsqueezy", "Lemon Squeezy", "Finance", () => new MetricWidget("lemonsqueezy"), "lemonsqueezy"),
        new("lemonsqueezy.chart", "Lemon Squeezy Chart", "Finance", () => new MetricWidget("lemonsqueezy", chart: true), "lemonsqueezy"),
        new("paddle", "Paddle", "Finance", () => new MetricWidget("paddle"), "paddle"),
        new("paddle.chart", "Paddle Chart", "Finance", () => new MetricWidget("paddle", chart: true), "paddle"),
        new("gumroad", "Gumroad", "Finance", () => new MetricWidget("gumroad"), "gumroad"),
        new("gumroad.chart", "Gumroad Chart", "Finance", () => new MetricWidget("gumroad", chart: true), "gumroad"),
        new("crypto", "Crypto", "Stocks", () => new CryptoWidget()),
        new("stocks.watchlist", "Stock Watchlist", "Stocks", () => new MetricWidget("finnhub"), "finnhub"),
        new("searchconsole", "Search Console", "Analytics", () => new MetricWidget("searchconsole", chart: true), "searchconsole"),
        new("plausible", "Plausible", "Analytics", () => new MetricWidget("plausible", chart: true), "plausible"),
        new("dub", "Dub", "Analytics", () => new MetricWidget("dub", chart: true), "dub"),
        new("resend", "Resend", "Developer", () => new MetricWidget("resend"), "resend"),
        new("linear", "Linear", "Developer", () => new MetricWidget("linear"), "linear"),
        new("supabase", "Supabase", "Developer", () => new MetricWidget("supabase"), "supabase"),

        // Weather
        new("weather", "Weather", "Weather", () => new WeatherWidget()),
        new("weather.sun", "Sunrise & Sunset", "Weather", () => new SunWidget()),
        new("weather.status", "Weather Status", "Weather", () => new WeatherStatusWidget()),
        new("weather.details", "Weather Details", "Weather", () => new WeatherDetailsWidget()),
        new("weather.hourly", "Hourly", "Weather", () => new HourlyWidget()),
        new("weather.forecast", "Today & Forecast", "Weather", () => new ForecastWidget()),

        // Online
        new("gmail", "Gmail", "Online & Mail", () => new MetricWidget("gmail"), "gmail"),
        new("github", "GitHub", "Online & Mail", () => new GitHubWidget(), "github"),
        new("github.activity", "GitHub Activity", "Online & Mail", () => new MetricWidget("githubactivity", chart: true), "github"),
        new("github.overview", "GitHub Overview", "Online & Mail", () => new MetricWidget("githuboverview"), "github"),
        new("leetcode", "LeetCode Stats", "Online & Mail", () => new MetricWidget("leetcode"), "leetcode"),
        new("youtube", "YouTube", "Social", () => new MetricWidget("youtube"), "youtube"),

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
