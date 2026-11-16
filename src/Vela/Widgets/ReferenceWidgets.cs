using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vela.Core;
using Vela.Services;
using Vela.Shell;
using Vela.UI;
using Vela.Views;
using static Vela.UI.UI;

namespace Vela.Widgets;

// Widgets modelled on the reference app's catalogue renders (weather, clocks, top processes).

/// <summary>Condition + city over a sky-tinted gradient, with a big thin temperature (reference "Weather Status").</summary>
public sealed class WeatherStatusWidget : WidgetBase
{
    TextBlock _cond = null!, _city = null!, _temp = null!;
    WeatherIcon _icon = null!;
    Brush _fill = Sky(1, true);
    protected override Brush? TileFill => _fill;

    static Brush Sky(int code, bool day)
    {
        var kind = WeatherService.Kind(code);
        var (a, b) = !day ? ("#2A3550", "#141A2B")
            : kind switch
            {
                WeatherKind.Clear => ("#4AA3F5", "#2E7DE0"),
                WeatherKind.PartlyCloudy => ("#5B9BE0", "#3E73C2"),
                WeatherKind.Rain or WeatherKind.Drizzle or WeatherKind.Storm => ("#5D6B80", "#3D4757"),
                WeatherKind.Snow => ("#8FA7C4", "#6D84A3"),
                _ => ("#7B8BA3", "#56647A"),
            };
        return Gradient(a, b);
    }

    protected override UIElement BuildContent()
    {
        Tile.BorderThickness = new Thickness(0);
        Tile.Padding = new Thickness(11, 0, 8, 0);
        _cond = Text("", 9.5, FontWeights.SemiBold, "OnAccent");
        _city = Text("", 8, FontWeights.Medium, "OnAccent");
        _city.Opacity = 0.85;
        _temp = Text("—", 24, FontWeights.Light, "OnAccent");
        _temp.Margin = new Thickness(14, -2, 0, 0);
        _icon = new WeatherIcon { Width = 18, Height = 18, Margin = new Thickness(-4, 3, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var g = Columns("*,auto,auto", V(_cond, _city), _temp, _icon);
        g.MinWidth = 150;
        return g;
    }

    protected override void OnAttach() { Svc.Weather.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Weather.Changed -= Render;

    void Render()
    {
        var w = Svc.Weather;
        _city.Text = Settings.Current.Weather.City ?? "Set a city";
        if (!w.HasData) { _cond.Text = w.Configured ? "Loading…" : "Weather"; _temp.Text = "—"; return; }
        _cond.Text = WeatherService.Describe(w.Code);
        _temp.Text = $"{w.Temp:0}°";
        _icon.Set(WeatherService.Kind(w.Code), !w.IsDay);
        _fill = Sky(w.Code, w.IsDay);
        InvalidateTile();
    }

    protected override UIElement BuildCard() => WeatherWidget.WeatherCard();
    protected override double CardWidth => 380;
}

/// <summary>Next six hours: time, icon, temperature; "Now" highlighted (reference "Hourly").</summary>
public sealed class HourlyWidget : WidgetBase
{
    readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    protected override bool Clickable => false;

    protected override UIElement BuildContent() { Tile.Padding = new Thickness(4, 0, 4, 0); return _row; }
    protected override void OnAttach() { Svc.Weather.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Weather.Changed -= Render;

    void Render()
    {
        _row.Children.Clear();
        var w = Svc.Weather;
        if (!w.HasData) { _row.Children.Add(Text(w.Configured ? "Loading…" : "Set a city in Settings", 8, FontWeights.Medium, "Text.Secondary").Margin(6, 0)); return; }
        int i = 0;
        foreach (var h in w.Hours.Take(6))
        {
            var ic = new WeatherIcon { Width = 10, Height = 10, Margin = new Thickness(0, 2, 0, 2) };
            ic.Set(WeatherService.Kind(h.Code), !h.IsDay);
            var col = V(Text(i == 0 ? "Now" : h.Time.ToString(Settings.Current.Use24Hour ? "HH" : "htt", CultureInfo.InvariantCulture), 6.75, FontWeights.SemiBold, i == 0 ? "Text.Primary" : "Text.Secondary").Center(),
                ic, Text($"{h.Temp:0}°", 7.75, FontWeights.SemiBold).Center());
            var cell = new Border { Width = 27, Padding = new Thickness(0, 3, 0, 3), CornerRadius = new CornerRadius(8), Child = col, Margin = new Thickness(1, 0, 1, 0) };
            if (i == 0) cell.Bg("Fill.Control");
            _row.Children.Add(cell);
            i++;
        }
    }
}

/// <summary>Today big + the next three days (reference "Today &amp; Forecast").</summary>
public sealed class ForecastWidget : WidgetBase
{
    readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    protected override bool Clickable => false;

    protected override UIElement BuildContent() => _row;
    protected override void OnAttach() { Svc.Weather.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Weather.Changed -= Render;

    void Render()
    {
        _row.Children.Clear();
        var w = Svc.Weather;
        if (!w.HasData) { _row.Children.Add(Text(w.Configured ? "Loading…" : "Set a city in Settings", 8, FontWeights.Medium, "Text.Secondary")); return; }
        var big = new WeatherIcon { Width = 20, Height = 20 };
        big.Set(WeatherService.Kind(w.Code), !w.IsDay);
        _row.Children.Add(big);
        _row.Children.Add(V(Text($"{w.Temp:0}°", 13, FontWeights.SemiBold), Text("Today", 6.75, FontWeights.Medium, "Text.Secondary").Margin(0, -2, 0, 0)).Margin(6, 0, 10, 0));
        foreach (var d in w.Days.Skip(1).Take(3))
        {
            var ic = new WeatherIcon { Width = 10, Height = 10, Margin = new Thickness(0, 1, 0, 1) };
            ic.Set(WeatherService.Kind(d.Code), false);
            var temps = H(Text($"{d.Max:0}°", 7.5, FontWeights.SemiBold), Text($" {d.Min:0}°", 7, FontWeights.Medium, "Text.Secondary"));
            temps.HorizontalAlignment = HorizontalAlignment.Center;
            _row.Children.Add(V(Text(d.Date.ToString("ddd", CultureInfo.CurrentCulture), 6.75, FontWeights.SemiBold, "Text.Secondary").Center(), ic, temps).Margin(5, 0, 5, 0));
        }
    }
}

/// <summary>Flip-card digits (reference "Flip Clock").</summary>
public sealed class FlipClockWidget : WidgetBase
{
    readonly TextBlock[] _d = new TextBlock[4];
    TextBlock _ampm = null!;
    protected override int TickSeconds => 1;

    protected override UIElement BuildContent()
    {
        Tile.Padding = new Thickness(6, 0, 7, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 4; i++)
        {
            _d[i] = Text("0", 17, FontWeights.SemiBold).Center();
            var card = new Grid { Width = 19, Height = 28, Margin = new Thickness(i == 2 ? 6 : 1.5, 0, 1.5, 0) };
            card.Children.Add(new Border { CornerRadius = new CornerRadius(5) }.Bg("Fill.Control"));
            card.Children.Add(_d[i]);
            card.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.55 }.Bg("Tile.Background"));
            row.Children.Add(card);
        }
        _ampm = Text("", 7, FontWeights.SemiBold, "Text.Secondary").Margin(4, 0, 0, 3);
        _ampm.VerticalAlignment = VerticalAlignment.Bottom;
        row.Children.Add(_ampm);
        return row;
    }

    protected override void OnTick(DateTime now)
    {
        var s = now.ToString(Settings.Current.Use24Hour ? "HHmm" : "hhmm", CultureInfo.InvariantCulture);
        for (int i = 0; i < 4; i++) _d[i].Text = s[i].ToString();
        _ampm.Text = Settings.Current.Use24Hour ? "" : now.ToString("tt", CultureInfo.InvariantCulture);
    }

    protected override UIElement BuildCard() => new MonthCalendar();
    protected override double CardWidth => 320;
}

/// <summary>Minimal analog face: hour ticks, two hands, an orange hub (reference "Analog Minimal").</summary>
public sealed class AnalogFace : FrameworkElement
{
    public DateTime Time { get; set; } = DateTime.Now;
    public bool Light { get; set; }
    public bool Ticks { get; set; } = true;

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight), r = s / 2;
        if (s <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var face = Light ? Brushes.White : (Brush)new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255));
        var ink = Light ? (Brush)new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)) : Brushes.White;
        dc.DrawEllipse(face, null, c, r, r);
        if (Ticks)
        {
            for (int i = 0; i < 12; i++)
            {
                double a = i * Math.PI / 6, inner = i % 3 == 0 ? r * 0.68 : r * 0.76;
                var pen = new Pen(i % 3 == 0 ? ink : new SolidColorBrush(Color.FromArgb(0x80, 0x88, 0x88, 0x8E)), i % 3 == 0 ? s * 0.035 : s * 0.025) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, new Point(c.X + Math.Sin(a) * inner, c.Y - Math.Cos(a) * inner), new Point(c.X + Math.Sin(a) * r * 0.86, c.Y - Math.Cos(a) * r * 0.86));
            }
        }
        double hA = (Time.Hour % 12 + Time.Minute / 60.0) * Math.PI / 6, mA = (Time.Minute + Time.Second / 60.0) * Math.PI / 30;
        var hp = new Pen(ink, s * 0.06) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var mp = new Pen(ink, s * 0.045) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(hp, c, new Point(c.X + Math.Sin(hA) * r * 0.45, c.Y - Math.Cos(hA) * r * 0.45));
        dc.DrawLine(mp, c, new Point(c.X + Math.Sin(mA) * r * 0.7, c.Y - Math.Cos(mA) * r * 0.7));
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), null, c, s * 0.055, s * 0.055);
    }
}

public sealed class AnalogClockWidget : WidgetBase
{
    AnalogFace _face = null!;
    protected override int TickSeconds => 15;

    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        _face = new AnalogFace { Width = 34, Height = 34 };
        return _face;
    }

    protected override void OnTick(DateTime now) { _face.Time = now; _face.InvalidateVisual(); ToolTip = now.ToString("t"); }
    protected override UIElement BuildCard() => new MonthCalendar();
    protected override double CardWidth => 320;
}

/// <summary>Three small dials with city codes (reference "World Clocks"). Right-click to change cities.</summary>
public sealed class WorldClocksWidget : WidgetBase
{
    static readonly (string Code, string Zone)[] Choices =
    {
        ("NYC", "Eastern Standard Time"), ("SF", "Pacific Standard Time"), ("LON", "GMT Standard Time"), ("PAR", "Romance Standard Time"),
        ("DXB", "Arabian Standard Time"), ("IND", "India Standard Time"), ("SGP", "Singapore Standard Time"), ("TYO", "Tokyo Standard Time"), ("SYD", "AUS Eastern Standard Time"),
    };
    readonly List<(AnalogFace face, TextBlock label, string zone)> _clocks = new();
    readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    protected override int TickSeconds => 15;

    string[] Selected => Cfg("cities", "NYC,LON,TYO").Split(',', StringSplitOptions.RemoveEmptyEntries);

    protected override UIElement BuildContent() { BuildClocks(); return _row; }

    void BuildClocks()
    {
        _row.Children.Clear(); _clocks.Clear();
        foreach (var code in Selected.Take(4))
        {
            var zone = Choices.FirstOrDefault(c => c.Code == code).Zone ?? "UTC";
            var face = new AnalogFace { Width = 24, Height = 24, Ticks = false };
            var label = Text(code, 6.5, FontWeights.Bold, "Text.Secondary").Center();
            _clocks.Add((face, label, zone));
            _row.Children.Add(V(face, label.Margin(0, 1, 0, 0)).Margin(4, 0, 4, 0));
        }
        OnTick(DateTime.Now);
    }

    protected override void OnTick(DateTime now)
    {
        foreach (var (face, _, zone) in _clocks)
        {
            DateTime t;
            try { t = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, zone); } catch { t = DateTime.UtcNow; }
            face.Time = t;
            face.Light = t.Hour is >= 7 and < 19;
            face.InvalidateVisual();
            face.ToolTip = t.ToString("ddd t");
        }
    }

    protected override bool Clickable => false;

    protected override void AddMenuItems(ContextMenu menu)
    {
        var sel = Selected.ToList();
        foreach (var (code, _) in Choices)
            menu.Items.Add(Item(code, () =>
            {
                if (sel.Contains(code)) sel.Remove(code); else if (sel.Count < 4) sel.Add(code);
                SetCfg("cities", string.Join(',', sel));
                BuildClocks();
            }, sel.Contains(code)));
    }
}

/// <summary>Top three apps by CPU or memory, with their icons (reference "Top CPU" / "Top RAM"). Samples every 10 s.</summary>
public sealed class TopProcessesWidget : WidgetBase
{
    readonly bool _cpu;
    readonly StackPanel _rows = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
    public TopProcessesWidget(bool cpu) => _cpu = cpu;

    protected override UIElement BuildContent()
    {
        _rows.Children.Add(Text("Measuring…", 8, FontWeights.Medium, "Text.Secondary"));
        return _rows;
    }

    protected override void OnAttach()
    {
        Svc.TopProcesses.Updated += Render;
        Render();
    }

    protected override void OnDetach() => Svc.TopProcesses.Updated -= Render;

    void Render()
    {
        var top = _cpu ? Svc.TopProcesses.ByCpu : Svc.TopProcesses.ByRam;
        if (top.Count == 0) return;
        _rows.Children.Clear();
        foreach (var process in top)
        {
            var icon = new Image { Width = 9, Height = 9, Margin = new Thickness(0, 0, 5, 0) };
            if (process.Path != null) Svc.Icons.Get(process.Path, src => icon.Source = src, 32);
            var name = Text(process.Name, 7.75, FontWeights.SemiBold);
            name.MaxWidth = 92;
            var value = Text(_cpu ? $"{process.Cpu:0.0}%" : Fmt.Bytes(process.Memory), 7.75, FontWeights.SemiBold).Right();
            _rows.Children.Add(Columns("auto,*,auto", icon, name, value.Margin(8, 0, 0, 0)).Margin(0, 0.5, 0, 0.5));
        }
    }

    protected override void OnClick() => WinActions.OpenTaskManager();
}

sealed record DownloadEntry(string FullName, string Name, DateTime LastWriteTime);

/// <summary>One asynchronous Downloads snapshot shared by every display.</summary>
static class DownloadsSnapshot
{
    sealed record Snapshot(string Folder, IReadOnlyList<DownloadEntry> Entries, int Today);

    static Action? _changed;
    static int _seconds;
    static bool _busy;

    public static string Folder { get; private set; } = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public static IReadOnlyList<DownloadEntry> Entries { get; private set; } = Array.Empty<DownloadEntry>();
    public static int Today { get; private set; }
    public static bool Ready { get; private set; }

    public static event Action? Changed
    {
        add
        {
            bool first = _changed == null;
            _changed += value;
            if (first) { _seconds = 0; Svc.Ticker.Tick += OnTick; Refresh(); }
        }
        remove
        {
            _changed -= value;
            if (_changed == null) Svc.Ticker.Tick -= OnTick;
        }
    }

    static void OnTick(DateTime _)
    {
        if (++_seconds < 30) return;
        _seconds = 0;
        Refresh();
    }

    static async void Refresh()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var snapshot = await Task.Run(Capture);
            Folder = snapshot.Folder;
            Entries = snapshot.Entries;
            Today = snapshot.Today;
            Ready = true;
            _changed?.Invoke();
        }
        catch (Exception ex) { Log.Warn("Downloads snapshot: " + ex.Message); }
        finally { _busy = false; }
    }

    static Snapshot Capture()
    {
        string folder = ResolveFolder();
        try
        {
            var files = new DirectoryInfo(folder).EnumerateFileSystemInfos()
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0
                    && !f.Name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)
                    && !f.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                    && !f.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .ToList();
            int today = files.Count(f => f.LastWriteTime.Date == DateTime.Today);
            var latest = files.OrderByDescending(f => f.LastWriteTimeUtc).Take(12)
                .Select(f => new DownloadEntry(f.FullName, f.Name, f.LastWriteTime)).ToList();
            return new Snapshot(folder, latest, today);
        }
        catch { return new Snapshot(folder, Array.Empty<DownloadEntry>(), 0); }
    }

    static string ResolveFolder()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders");
            if (key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") is string path && Directory.Exists(path)) return path;
        }
        catch { }
        return Folder;
    }
}

/// <summary>
/// macOS-dock-style Downloads stack: the latest three files fanned on the tile; the card shows the twelve
/// newest as a grid (click to open, right-click the tile for the folder). Reads the folder only on tick / open.
/// </summary>
public sealed class DownloadsWidget : WidgetBase
{
    readonly Grid _fan = new() { Width = 30, Height = 30 };
    TextBlock _count = null!;

    protected override UIElement BuildContent()
    {
        _count = Text("", 7, FontWeights.Medium, "Text.Secondary");
        return H(_fan, V(Text("Downloads", 8.5, FontWeights.SemiBold), _count).Margin(9, 0, 2, 0));
    }

    protected override void OnAttach()
    {
        DownloadsSnapshot.Changed += RenderSnapshot;
        RenderSnapshot();
    }

    protected override void OnDetach() => DownloadsSnapshot.Changed -= RenderSnapshot;

    void RenderSnapshot()
    {
        var files = DownloadsSnapshot.Entries.Take(3).ToList();
        _fan.Children.Clear();
        for (int i = files.Count - 1; i >= 0; i--)
        {
            var img = new Image { Width = 22, Height = 22, RenderTransformOrigin = new Point(0.5, 1),
                RenderTransform = new RotateTransform((i - 1) * -9), Margin = new Thickness(i * 2, 4 - i, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            Svc.Icons.Get(files[i].FullName, src => img.Source = src, 48);
            _fan.Children.Add(img);
        }
        if (files.Count == 0) _fan.Children.Add(Icon(Glyphs.Download, 15, "Text.Secondary"));
        _count.Text = !DownloadsSnapshot.Ready ? "checking…" : DownloadsSnapshot.Today == 0 ? "nothing new today" : $"{DownloadsSnapshot.Today} today";
    }

    protected override double CardWidth => 380;
    protected override UIElement BuildCard()
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
        foreach (var f in DownloadsSnapshot.Entries)
        {
            var file = f;
            var img = new Image { Width = 40, Height = 40 };
            Svc.Icons.Get(f.FullName, src => img.Source = src, 96);
            var name = Text(f.Name, 10.5, FontWeights.Medium);
            name.TextAlignment = TextAlignment.Center;
            name.MaxWidth = 76;
            var cell = GhostButton(V(img.Center(), name.Center().Margin(0, 5, 0, 0)), () => { FlyoutWindow.CloseCurrent(); WindowTracker.Launch(file.FullName); });
            cell.Margin = new Thickness(2);
            cell.ToolTip = $"{f.Name}\n{f.LastWriteTime:g}";
            grid.Children.Add(cell);
        }
        if (grid.Children.Count == 0) grid.Children.Add(Text(DownloadsSnapshot.Ready ? "Your Downloads folder is empty." : "Checking Downloads…", 12, null, "Text.Secondary"));
        return V(Card.Header("Downloads", Icon(Glyphs.Download, 16, "Accent")), grid,
            Card.Footer(DownloadsSnapshot.Folder, "Open folder", () => { FlyoutWindow.CloseCurrent(); WindowTracker.Launch(DownloadsSnapshot.Folder); }));
    }

    protected override void AddMenuItems(ContextMenu menu) => menu.Items.Add(Item("Open Downloads folder", () => WindowTracker.Launch(DownloadsSnapshot.Folder)));
}
