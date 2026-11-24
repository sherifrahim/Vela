using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vela.Core;
using Vela.Services;
using Vela.Shell;
using Vela.UI;
using Vela.Views;
using static Vela.UI.UI;

namespace Vela.Widgets;

// More reference widgets that need no account: World Time, Day Timeline, Recently Opened, Search, Commands,
// Screenshot Capture, and two more Now Playing layouts.

/// <summary>One city's time and its offset from here. Right-click to pick the city.</summary>
public sealed class WorldTimeWidget : WidgetBase
{
    static readonly Dictionary<string, string> Names = new()
    {
        ["NYC"] = "New York", ["SF"] = "San Francisco", ["LON"] = "London", ["PAR"] = "Paris", ["DXB"] = "Dubai",
        ["IND"] = "India", ["SGP"] = "Singapore", ["TYO"] = "Tokyo", ["SYD"] = "Sydney",
    };
    TextBlock _city = null!, _time = null!, _offset = null!;
    protected override int TickSeconds => 15;
    string Code => Cfg("city", "LON");

    protected override UIElement BuildContent()
    {
        _city = Text("", 6.75, FontWeights.Bold, "Text.Secondary");
        _time = Text("", 13, FontWeights.SemiBold);
        _offset = Text("", 7, FontWeights.SemiBold, "Text.Secondary");
        return V(_city, _time.Margin(0, -1, 0, 0), _offset.Margin(0, -1, 0, 0)).Margin(2, 0);
    }

    protected override void OnTick(DateTime now)
    {
        var zoneId = WorldClocksWidget.Choices.FirstOrDefault(c => c.Code == Code).Zone ?? "GMT Standard Time";
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(zoneId); } catch { tz = TimeZoneInfo.Utc; }
        var there = TimeZoneInfo.ConvertTime(now, tz);
        _city.Text = Names.GetValueOrDefault(Code, Code).ToUpperInvariant();
        _time.Text = there.ToString(Settings.Current.Use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
        var diff = there - now;
        int mins = (int)Math.Round(diff.TotalMinutes);
        string sign = mins >= 0 ? "+" : "−";
        string off = mins == 0 ? "same time" : mins % 60 == 0 ? $"{sign}{Math.Abs(mins / 60)}h" : $"{sign}{Math.Abs(mins) / 60}h {Math.Abs(mins) % 60}m";
        string day = there.Date == now.Date ? "today" : there.Date > now.Date ? "tomorrow" : "yesterday";
        _offset.Text = $"{off} · {day}";
    }

    protected override void AddMenuItems(ContextMenu menu)
    {
        foreach (var (code, _) in WorldClocksWidget.Choices)
        {
            var c = code;
            menu.Items.Add(Item(Names.GetValueOrDefault(c, c), () => { SetCfg("city", c); OnTick(DateTime.Now); }, c == Code));
        }
    }
    protected override void OnClick() => WindowTracker.OpenUri("ms-clock:");
}

/// <summary>Today as a bar from midnight to midnight: daylight band (when a city is set) and a "now" marker.</summary>
public sealed class DayTimelineWidget : WidgetBase
{
    const double BarWidth = 150;
    TextBlock _now = null!, _left = null!;
    Border _done = null!, _sun = null!, _marker = null!;
    protected override int TickSeconds => 30;

    protected override UIElement BuildContent()
    {
        _now = Text("", 9, FontWeights.SemiBold);
        _left = Text("", 7, FontWeights.SemiBold, "Text.Secondary");
        var track = new Border { Width = BarWidth, Height = 6, CornerRadius = new CornerRadius(3) }.Bg("Fill.Track");
        _sun = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.35, Visibility = Visibility.Collapsed }.Bg("Sys.Yellow");
        _done = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left }.Bg("Accent");
        _marker = new Border { Width = 2.5, Height = 12, CornerRadius = new CornerRadius(1.25), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center }.Bg("Text.Primary");
        var bar = new Grid { Width = BarWidth, Height = 12 };
        foreach (var e in new UIElement[] { track, _sun, _done, _marker }) { if (e is FrameworkElement f && f != _marker) f.VerticalAlignment = VerticalAlignment.Center; bar.Children.Add(e); }
        var ticks = new Grid { Width = BarWidth, Margin = new Thickness(0, 1, 0, 0) };
        foreach (var h in new[] { 0, 6, 12, 18, 24 })
        {
            var t = Text(h == 24 ? "" : $"{h}", 6, FontWeights.SemiBold, "Text.Tertiary");
            t.HorizontalAlignment = HorizontalAlignment.Left;
            t.Margin = new Thickness(Math.Max(0, BarWidth * h / 24 - 2), 0, 0, 0);
            ticks.Children.Add(t);
        }
        return V(Columns("*,auto", _now, _left), bar.Margin(0, 2, 0, 0), ticks);
    }

    protected override void OnAttach() { if (Svc.Weather.Configured) Svc.Weather.Changed += Sun; Sun(); }
    protected override void OnDetach() => Svc.Weather.Changed -= Sun;

    void Sun()
    {
        var w = Svc.Weather;
        if (!w.HasData || w.Sunrise == default || w.Sunset <= w.Sunrise) { _sun.Visibility = Visibility.Collapsed; return; }
        _sun.Visibility = Visibility.Visible;
        double a = w.Sunrise.TimeOfDay.TotalHours / 24, b = w.Sunset.TimeOfDay.TotalHours / 24;
        _sun.Margin = new Thickness(BarWidth * a, 0, 0, 0);
        _sun.Width = Math.Max(0, BarWidth * (b - a));
    }

    protected override void OnTick(DateTime now)
    {
        double f = now.TimeOfDay.TotalHours / 24;
        _done.Width = Math.Max(3, BarWidth * f);
        _marker.Margin = new Thickness(Math.Clamp(BarWidth * f - 1.25, 0, BarWidth - 2.5), 0, 0, 0);
        _now.Text = now.ToString(Settings.Current.Use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
        var left = now.Date.AddDays(1) - now;
        _left.Text = $"{(int)left.TotalHours}h {left.Minutes}m left";
    }

    protected override UIElement BuildCard() => new MonthCalendar();
    protected override double CardWidth => 320;
}

/// <summary>Files and folders opened lately (the Windows "Recent" list). Card: the last 12, click to open.</summary>
public sealed class RecentFilesWidget : WidgetBase
{
    const string History = "";
    TextBlock _name = null!, _sub = null!;
    List<FileInfo> _items = new();
    int _loading;
    protected override int TickSeconds => 60;
    static string RecentDir => Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    protected override UIElement BuildContent()
    {
        _name = Text("Recently opened", 10, FontWeights.SemiBold);
        _name.MaxWidth = 120;
        _sub = Text("", 7, FontWeights.SemiBold, "Text.Secondary");
        var badge = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Child = Icon(History, 12) }.Bg("Fill.Control");
        return H(badge, V(_name, _sub).Margin(9, 0, 3, 0));
    }

    protected override void OnTick(DateTime now)
    {
        if (Interlocked.Exchange(ref _loading, 1) == 1) return;
        Task.Run(() =>
        {
            // File I/O off the UI thread; the folder holds shortcuts (.lnk), newest written = most recently opened.
            try
            {
                return new DirectoryInfo(RecentDir).EnumerateFiles("*.lnk")
                    .Where(f => !IsProtocolEntry(f.Name))
                    .OrderByDescending(f => f.LastWriteTime).Take(12).ToList();
            }
            catch { return new List<FileInfo>(); }
        }).ContinueWith(t =>
        {
            Interlocked.Exchange(ref _loading, 0);
            _items = t.Result;
            var first = _items.FirstOrDefault();
            _name.Text = first != null ? Path.GetFileNameWithoutExtension(first.Name) : "Nothing yet";
            _sub.Text = first != null ? $"recent · {Ago(first.LastWriteTime)}" : "recently opened";
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Windows also records app-protocol launches ("ms-screenclip---source=…", "ms-gamingoverlay---"): not files.</summary>
    static bool IsProtocolEntry(string name) => name.StartsWith("ms-", StringComparison.OrdinalIgnoreCase) || name.Contains("---");

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes}m ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours}h ago" : $"{(int)d.TotalDays}d ago";
    }

    protected override double CardWidth => 340;
    protected override UIElement BuildCard()
    {
        var list = new StackPanel();
        foreach (var f in _items)
        {
            var file = f;
            var img = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0) };
            Svc.Icons.Get(f.FullName, s => img.Dispatcher.BeginInvoke(() => img.Source = s), 48);
            var name = Text(Path.GetFileNameWithoutExtension(f.Name), 12.5, FontWeights.SemiBold);
            var when = Text(Ago(f.LastWriteTime), 11, null, "Text.Secondary").Margin(8, 0, 0, 0);
            var b = GhostButton(Columns("auto,*,auto", img, name, when), () => { FlyoutWindow.CloseCurrent(); WindowTracker.Launch(file.FullName); });
            b.Padding = new Thickness(6, 5, 6, 5);
            b.Margin = new Thickness(-6, 0, -6, 0);
            list.Children.Add(b);
        }
        if (_items.Count == 0) list.Children.Add(Text("Nothing opened lately.", 12.5, null, "Text.Secondary"));
        return V(Card.Header("Recently opened"), list,
            Card.Footer("From Windows' recent items.", "Open folder", () => { FlyoutWindow.CloseCurrent(); WindowTracker.Launch(RecentDir); }));
    }
}

/// <summary>A search field on the dock; click opens Windows Search.</summary>
public sealed class SearchWidget : WidgetBase
{
    protected override UIElement BuildContent()
    {
        var pill = new Border { Width = 128, Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 0, 10, 0) }.Bg("Fill.Control");
        pill.Child = H(Icon(Glyphs.Search, 10.5, "Text.Secondary").Margin(0, 0, 7, 0), Text("Search", 9.5, FontWeights.SemiBold, "Text.Secondary"));
        ToolTip = Settings.Current.SearchHotkey ? "Search (Alt+Space)" : "Search";
        return pill;
    }
    protected override void OnClick() => Views.LauncherWindow.Toggle();
    protected override void AddMenuItems(ContextMenu menu) =>
        menu.Items.Add(Item("Windows Search (Win+S)", () => WinActions.SendKeys(0x5B, 0x53)));
}

/// <summary>Command palette: type a program, path, URL or settings page and press Enter, or pick a quick action.</summary>
public sealed class CommandsWidget : WidgetBase
{
    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        ToolTip = "Commands";
        return Icon(Glyphs.Lightning, 15);
    }

    internal static readonly (string Name, string Hint, Action Run)[] Quick =
    {
        ("Lock", "Lock this PC", WinActions.LockPc),
        ("Run…", "The Windows Run box", () => WinActions.SendKeys(0x5B, 0x52)),
        ("Settings", "Windows Settings", () => WinActions.OpenSettings("")),
        ("Downloads", "Open the folder", () => WindowTracker.Launch(Path.Combine(Paths.UserHome, "Downloads"))),
        ("Task Manager", "taskmgr", WinActions.OpenTaskManager),
        ("Device Manager", "devmgmt.msc", () => WindowTracker.Launch("devmgmt.msc")),
        ("Services", "services.msc", () => WindowTracker.Launch("services.msc")),
        ("Control Panel", "control", () => WindowTracker.Launch("control.exe")),
        ("Empty Recycle Bin", "Windows asks to confirm", WinActions.EmptyRecycleBin),
    };

    protected override double CardWidth => 340;
    protected override UIElement BuildCard()
    {
        var box = TextBox("", "Program, path, URL or ms-settings: …");
        var status = Text("", 11.5, null, "Sys.Red");
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var cmd = box.Text.Trim();
            if (cmd.Length == 0) return;
            if (Run(cmd)) FlyoutWindow.CloseCurrent(); else status.Text = $"Couldn't run \"{cmd}\"";
        };
        box.Loaded += (_, _) => box.Focus();
        var list = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(-4, 10, -4, 0) };
        foreach (var (name, hint, run) in Quick)
        {
            var r = run;
            var b = GhostButton(V(Text(name, 12.5, FontWeights.SemiBold), Text(hint, 10.5, null, "Text.Secondary")), () => { FlyoutWindow.CloseCurrent(); r(); });
            b.Padding = new Thickness(8, 6, 8, 6);
            b.Margin = new Thickness(0, 0, 0, 2);
            list.Children.Add(b);
        }
        return V(Card.Header("Commands"), box, status.Margin(0, 4, 0, 0), list);
    }

    /// <summary>Like the Run box: a whole command line, else the first word as the program and the rest as arguments.</summary>
    internal static bool Run(string cmd)
    {
        try
        {
            if (cmd.Contains("://") || cmd.EndsWith(':') || File.Exists(cmd) || Directory.Exists(cmd) || !cmd.Contains(' '))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cmd) { UseShellExecute = true });
                return true;
            }
            int sp = cmd.IndexOf(' ');
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cmd[..sp]) { UseShellExecute = true, Arguments = cmd[(sp + 1)..] });
            return true;
        }
        catch (Exception ex) { Log.Warn("Command failed: " + ex.Message); return false; }
    }
}

/// <summary>Screenshot: click = snip a region (Windows' capture overlay); menu = whole screen to Pictures.</summary>
public sealed class ScreenshotWidget : WidgetBase
{
    const string Camera = "";
    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        ToolTip = "Screenshot";
        return Icon(Camera, 15);
    }
    protected override void OnClick() => WindowTracker.OpenUri("ms-screenclip:");
    protected override void AddMenuItems(ContextMenu menu)
    {
        menu.Items.Add(Item("Snip a region", () => WindowTracker.OpenUri("ms-screenclip:")));
        menu.Items.Add(Item("Whole screen to Pictures (Win+PrtScn)", () => WinActions.SendKeys(0x5B, 0x2C)));
        menu.Items.Add(Item("Open Screenshots folder", () => WindowTracker.Launch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"))));
    }
}

/// <summary>Now Playing, compact: cover, title and artist. Click = play/pause; card = the player.</summary>
public sealed class NowPlayingCompactWidget : WidgetBase
{
    Action<ImageSource?> _setArt = null!;
    TextBlock _title = null!, _artist = null!;
    protected override UIElement BuildContent()
    {
        var (frame, set) = Art.Make(28, false, 7);
        _setArt = set;
        _title = Text("", 9.5, FontWeights.SemiBold);
        _artist = Text("", 7.5, FontWeights.SemiBold, "Text.Secondary");
        _title.MaxWidth = _artist.MaxWidth = 110;
        return H(frame, V(_title, _artist).Margin(9, 0, 3, 0));
    }
    protected override void OnAttach() { Svc.Media.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Media.Changed -= Render;
    void Render()
    {
        var m = Svc.Media;
        _setArt(m.Art);
        _title.Text = m.HasSession ? m.Title! : "Not playing";
        _artist.Text = m.HasSession ? (m.Artist ?? "") + (m.IsPlaying ? "" : " · paused") : "Play something";
    }
    protected override void OnClick() => Svc.Media.TogglePlayPause();
    protected override UIElement BuildCard() => MediaCard.Build();
}

/// <summary>Now Playing as a bar: cover, title / artist and the track's progress.</summary>
public sealed class NowPlayingBarWidget : WidgetBase
{
    Action<ImageSource?> _setArt = null!;
    TextBlock _title = null!, _time = null!;
    Action<double> _setProgress = null!;
    protected override int TickSeconds => 1;
    protected override UIElement BuildContent()
    {
        var (frame, set) = Art.Make(30, false, 7);
        _setArt = set;
        _title = Text("", 9, FontWeights.SemiBold);
        _title.MaxWidth = 150;
        _time = Text("", 7, FontWeights.SemiBold, "Text.Secondary");
        var bar = Progress(out _setProgress, "Text.Primary", 3);
        bar.Width = 150;
        return H(frame, V(Columns("*,auto", _title, _time.Margin(6, 0, 0, 0)), bar.Margin(0, 5, 0, 0)).Margin(9, 0, 3, 0));
    }
    protected override void OnAttach() { Svc.Media.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Media.Changed -= Render;
    void Render()
    {
        var m = Svc.Media;
        _setArt(m.Art);
        _title.Text = m.HasSession ? $"{m.Title}{(string.IsNullOrEmpty(m.Artist) ? "" : " — " + m.Artist)}" : "Not playing";
        OnTick(DateTime.Now);
    }
    protected override void OnTick(DateTime now)
    {
        var m = Svc.Media;
        if (!m.HasSession || m.Duration <= TimeSpan.Zero) { _setProgress(0); _time.Text = ""; return; }
        var pos = m.Position + (m.IsPlaying ? now - m.PositionUpdated : TimeSpan.Zero);
        if (pos > m.Duration) pos = m.Duration;
        _setProgress(pos.TotalSeconds / m.Duration.TotalSeconds);
        _time.Text = $"{(int)pos.TotalMinutes}:{pos.Seconds:00} / {(int)m.Duration.TotalMinutes}:{m.Duration.Seconds:00}";
    }
    protected override void OnClick() => Svc.Media.TogglePlayPause();
    protected override UIElement BuildCard() => MediaCard.Build();
}
