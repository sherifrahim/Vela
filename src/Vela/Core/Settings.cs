using Microsoft.Win32;

namespace Vela.Core;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    // Appearance
    public string Theme { get; set; } = "glass";      // see Theming/Palettes.cs
    public string Size { get; set; } = "M";           // S | M | L
    public string Accent { get; set; } = "#0A84FF";
    public string WidgetStyle { get; set; } = "auto"; // auto | black | white | glass  (tile fill override)

    // Dock
    public string Edge { get; set; } = "bottom";      // bottom | top
    public double EdgeMargin { get; set; } = 4;
    public double Spacing { get; set; } = 5;
    public bool ReserveSpace { get; set; } = true;
    public string TaskbarMode { get; set; } = "hidden"; // hidden | autohide | keep
    public bool ShowAppNames { get; set; } = true;
    public bool Use24Hour { get; set; }
    public bool TrackScreenTime { get; set; } = true;

    public List<WidgetInstance> Widgets { get; set; } = new();
    public List<PinnedApp> PinnedApps { get; set; } = new();
    public bool PinnedImported { get; set; }
    public bool FirstRunDone { get; set; }

    public WeatherSettings Weather { get; set; } = new();
}

public sealed class WidgetInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Type { get; set; } = "";
    public Dictionary<string, string> Config { get; set; } = new();
}

public sealed class PinnedApp
{
    public string Name { get; set; } = "";
    /// <summary>What we pass to ShellExecute: a .lnk, an .exe, or shell:AppsFolder\AUMID.</summary>
    public string LaunchPath { get; set; } = "";
    public string? TargetPath { get; set; }
    public string? Aumid { get; set; }
}

public sealed class WeatherSettings
{
    public string? City { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public string Unit { get; set; } = "C";
}

public static class Settings
{
    static readonly string FilePath = Paths.File("settings.json");
    static Debouncer? _saver;

    public static AppSettings Current { get; private set; } = new();

    /// <summary>Raised (UI thread) after a setting changes. Argument names what changed, e.g. "theme".</summary>
    public static event Action<string>? Changed;

    public static void Load()
    {
        Current = Json.Load<AppSettings>(FilePath);
        if (!Current.FirstRunDone)
        {
            Current.Widgets = DefaultLayout();
            Current.FirstRunDone = true;
            SaveNow();
        }
    }

    /// <summary>Default dock, modelled on the reference screenshot layout.</summary>
    public static List<WidgetInstance> DefaultLayout() => new[]
    {
        "battery", "nowplaying.mini", "volume", "quicktoggles", "apps", "screentime", "claude", "clock",
    }.Select(t => new WidgetInstance { Type = t }).ToList();

    public static void Save(string what = "")
    {
        _saver ??= new Debouncer(TimeSpan.FromMilliseconds(400), SaveNow);
        _saver.Trigger();
        if (what.Length > 0) Changed?.Invoke(what);
    }

    public static void SaveNow() => Json.SaveAtomic(FilePath, Current);

    public static void Flush() => _saver?.Flush();

    // ---------- launch at startup (HKCU Run key, no admin) ----------
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool LaunchAtStartup
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("Vela") is string;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("Vela", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("Vela", false);
        }
    }
}
