using System.Diagnostics;
using Vela.Core;
using static Vela.Interop.Native;

namespace Vela.Services;

public sealed class ScreenTimeDay
{
    public string Date { get; set; } = "";
    public Dictionary<string, int> Apps { get; set; } = new(); // exe name -> seconds
    public int[] Hours { get; set; } = new int[24];            // seconds per hour
    public int Total => Hours.Sum();
}

/// <summary>
/// Counts active seconds per foreground app. Idle (no input for 2 min) and locked time is not counted.
/// Stored per day under %LOCALAPPDATA%\Vela\screentime\yyyy-MM-dd.json — never leaves the PC.
/// </summary>
public sealed class ScreenTimeService
{
    const uint IdleThresholdMs = 120_000;
    readonly string _dir = Paths.Dir("screentime");
    readonly Dictionary<uint, string> _names = new();
    readonly Dictionary<string, string> _friendly = new(StringComparer.OrdinalIgnoreCase);
    int _unsaved;

    public ScreenTimeDay Today { get; private set; }
    public bool Locked { get; set; }
    public event Action? Changed;

    public ScreenTimeService()
    {
        Today = LoadDay(DateTime.Today);
    }

    System.Windows.Threading.DispatcherTimer? _timer;

    /// <summary>Own 1 Hz timer (not the shared Ticker, which pauses while a fullscreen game hides the dock).</summary>
    public void Start()
    {
        _timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick(DateTime.Now);
        _timer.Start();
    }

    string PathFor(DateTime d) => Path.Combine(_dir, d.ToString("yyyy-MM-dd") + ".json");

    public ScreenTimeDay LoadDay(DateTime d)
    {
        var day = Json.Load<ScreenTimeDay>(PathFor(d));
        day.Date = d.ToString("yyyy-MM-dd");
        if (day.Hours.Length != 24) day.Hours = new int[24];
        return day;
    }

    void OnTick(DateTime now)
    {
        if (!Settings.Current.TrackScreenTime || Locked) return;
        if (now.ToString("yyyy-MM-dd") != Today.Date) { Flush(); Today = LoadDay(now.Date); }
        if (IdleMilliseconds() > IdleThresholdMs) return;

        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return;
        GetWindowThreadProcessId(fg, out uint pid);
        if (!_names.TryGetValue(pid, out var name))
        {
            if (_names.Count > 256) _names.Clear();
            var path = GetProcessPath(pid);
            name = path != null ? Path.GetFileNameWithoutExtension(path) : "Unknown";
            if (path != null && !_friendly.ContainsKey(name)) _friendly[name] = FriendlyName(path, name);
            _names[pid] = name;
        }
        if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase) && GetClassNameOf(fg) is "Progman" or "WorkerW") name = "Desktop";

        Today.Apps[name] = Today.Apps.GetValueOrDefault(name) + 1;
        Today.Hours[now.Hour]++;
        if (++_unsaved >= 60) Flush();
        Changed?.Invoke();
    }

    public void Flush()
    {
        if (_unsaved == 0) return;
        _unsaved = 0;
        Json.SaveAtomic(PathFor(DateTime.ParseExact(Today.Date, "yyyy-MM-dd", null)), Today);
    }

    public string DisplayName(string exeName) => _friendly.GetValueOrDefault(exeName) ?? exeName;

    static string FriendlyName(string path, string fallback)
    {
        try
        {
            var d = FileVersionInfo.GetVersionInfo(path).FileDescription;
            return string.IsNullOrWhiteSpace(d) ? fallback : d.Trim();
        }
        catch { return fallback; }
    }
}
