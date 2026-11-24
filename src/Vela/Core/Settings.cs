using Microsoft.Win32;

namespace Vela.Core;

public sealed class AppSettings
{
    public int Version { get; set; } = 2;

    // Appearance
    public string Theme { get; set; } = "glass";      // see Theming/Palettes.cs
    public string Size { get; set; } = "M";           // S | M | L
    public string Accent { get; set; } = "#0A84FF";
    public string WidgetStyle { get; set; } = "auto"; // auto | black | white | glass  (tile fill override)
    public string GlassEffect { get; set; } = "auto"; // auto (style decides) | off | frosted | liquid
    public double GlassBlur { get; set; } = 50;       // 0–100: how blurred the background under the bar is
    public bool GlassTiles { get; set; }               // tiles become frosted glass too (component blur)
    public bool LiveBlur { get; set; } = true;         // real-time blur of whatever is behind the bar (DWM Acrylic, GPU)

    // Dock
    public string Edge { get; set; } = "bottom";      // bottom | top
    public double EdgeMargin { get; set; } = 4;
    public double Spacing { get; set; } = 6;
    public string BarWidth { get; set; } = "full";     // full (edge to edge) | fit (hugs its content)
    public bool ReserveSpace { get; set; } = true;
    public string TaskbarMode { get; set; } = "hidden"; // hidden | autohide | keep
    public bool ShowAppNames { get; set; } = true;
    public bool DesktopCorner { get; set; } = true;   // peek / show-desktop sliver at the far end
    public string Displays { get; set; } = "all";     // all (a dock on every display) | primary
    public bool Magnification { get; set; }            // macOS-style icon magnification on hover (off in the reference)
    public bool LockDock { get; set; }                 // widgets, apps and the bar edge move only while the widget picker is open
    public string OpenIndicator { get; set; } = "dot"; // running-app mark under icons: dot | large | line (reference 1.2.20)
    public bool AppBadges { get; set; } = true;        // unread counts from Action Center on dock icons (reference app badges)
    public bool LiquidRefraction { get; set; } = true; // Liquid Glass style: GPU refraction of the wallpaper (falls back to live blur)
    public bool TrayHost { get; set; } = true;         // tray icons via a second Shell_TrayWnd (runs only with a Tray tile); kill switch
    public bool AdminShellHotkey { get; set; } = true; // Alt+T opens PowerShell as administrator from anywhere
    public bool AppTiles { get; set; } = false;       // each app icon in its own tile; false (default) = loose icons on the glass, like the reference and macOS
    public bool Island { get; set; } = true;          // Vela Island: Dynamic-Island-style activity pill at the top of the main display
    public double MagnifyAmount { get; set; } = 1.45; // largest icon scale (1.1–1.6)
    public bool WindowPreviews { get; set; } = true;  // live thumbnails when hovering a running app
    public bool AdaptiveTheme { get; set; } = true;   // each dock matches what is on its display (colours + light/dark)
    public string AutoHide { get; set; } = "off";     // off | smart (hide when a window overlaps) | always
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
        Migrate();
    }

    /// <summary>One-time upgrades of an existing settings.json (bump AppSettings.Version for each).</summary>
    static void Migrate()
    {
        if (Current.Version < 2)
        {
            // v2: the Control Centre (quick settings) joins the dock, just before the clock.
            if (!Current.Widgets.Any(w => w.Type == "controlcenter"))
            {
                int at = Current.Widgets.FindIndex(w => w.Type == "clock");
                Current.Widgets.Insert(at < 0 ? Current.Widgets.Count : at, new WidgetInstance { Type = "controlcenter" });
            }
            Current.Version = 2;
            SaveNow();
            Log.Info("Settings migrated to v2");
        }
    }

    /// <summary>Default dock, modelled on the reference screenshot layout.</summary>
    public static List<WidgetInstance> DefaultLayout() => new[]
    {
        "battery", "nowplaying.mini", "volume", "quicktoggles", "battery.dots", "apps", "screentime", "claude", "clock",
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
