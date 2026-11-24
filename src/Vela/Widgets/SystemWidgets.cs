using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Vela.Core;
using Vela.Services;
using Vela.Shell;
using Vela.UI;
using Vela.Views;
using Windows.Devices.Radios;
using static Vela.UI.UI;

namespace Vela.Widgets;

static class Rings
{
    public static (Grid host, RingGauge ring, TextBlock value) Make(string label, string fill, double size = 34)
    {
        var ring = new RingGauge(fill) { Width = size, Height = size, Thickness = 3 };
        var value = Text("0", 10.5, FontWeights.Bold).Center();
        var lbl = Text(label, 5.5, FontWeights.Bold, "Text.Secondary").Center();
        lbl.Margin = new Thickness(0, -2, 0, 0);
        var stack = V(value, lbl);
        stack.HorizontalAlignment = HorizontalAlignment.Center;
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(ring);
        g.Children.Add(stack);
        return (g, ring, value);
    }
}

/// <summary>CPU + RAM rings side by side. Click: top processes.</summary>
public sealed class SystemStatsWidget : WidgetBase
{
    RingGauge _cpu = null!, _ram = null!;
    TextBlock _cpuV = null!, _ramV = null!;

    protected override UIElement BuildContent()
    {
        var c = Rings.Make("CPU", "Accent");
        var r = Rings.Make("RAM", "Accent");
        (_cpu, _cpuV, _ram, _ramV) = (c.ring, c.value, r.ring, r.value);
        r.host.Margin = new Thickness(6, 0, 0, 0);
        return H(c.host, r.host);
    }

    protected override void OnAttach() => Svc.Sys.Updated += Render;
    protected override void OnDetach() => Svc.Sys.Updated -= Render;

    void Render()
    {
        var s = Svc.Sys;
        _cpu.Value = s.Cpu / 100; _cpuV.Text = $"{s.Cpu:0}";
        _ram.Value = s.Ram / 100; _ramV.Text = $"{s.Ram:0}";
    }

    protected override UIElement BuildCard() => SystemCard.Build();
}

public sealed class SingleStatWidget : WidgetBase
{
    public enum Stat { Cpu, Ram }
    readonly Stat _stat;
    RingGauge _ring = null!;
    TextBlock _v = null!, _sub = null!;
    public SingleStatWidget(Stat s) => _stat = s;

    protected override UIElement BuildContent()
    {
        var r = Rings.Make(_stat == Stat.Cpu ? "CPU" : "RAM", "Accent");
        (_ring, _v) = (r.ring, r.value);
        _sub = Text("", 9, FontWeights.SemiBold);
        return H(r.host, V(Caption(_stat == Stat.Cpu ? "Processor" : "Memory"), _sub).Margin(8, 0, 2, 0));
    }

    protected override void OnAttach() => Svc.Sys.Updated += Render;
    protected override void OnDetach() => Svc.Sys.Updated -= Render;

    void Render()
    {
        var s = Svc.Sys;
        double v = _stat == Stat.Cpu ? s.Cpu : s.Ram;
        _ring.Value = v / 100; _v.Text = $"{v:0}";
        _sub.Text = _stat == Stat.Cpu ? $"{Environment.ProcessorCount} threads" : $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0} GB";
    }

    protected override UIElement BuildCard() => SystemCard.Build();
}

static class SystemCard
{
    /// <summary>Top memory users + quick link to Task Manager. (Per-process CPU% needs two samples; roadmap.)</summary>
    public static UIElement Build()
    {
        var s = Svc.Sys;
        var cpuLine = new Sparkline("Accent") { Height = 50, Margin = new Thickness(0, 4, 0, 12) };
        cpuLine.Max = 100;
        cpuLine.SetValues(s.CpuHistory);
        var list = new StackPanel();
        try
        {
            var procs = Process.GetProcesses()
                .Select(p => { try { return (name: p.ProcessName, mem: p.WorkingSet64); } catch { return (name: p.ProcessName, mem: 0L); } finally { p.Dispose(); } })
                .GroupBy(p => p.name).Select(g => (name: g.Key, mem: g.Sum(x => x.mem)))
                .OrderByDescending(p => p.mem).Take(5);
            foreach (var p in procs)
                list.Children.Add(Columns("*,auto", Text(p.name, 13, FontWeights.SemiBold), Text(Fmt.Bytes(p.mem), 12, null, "Text.Secondary")).Margin(0, 0, 0, 8));
        }
        catch { }
        return V(Card.Header("System"),
            Columns("*,auto", Label("CPU"), Text($"{s.Cpu:0}%", 13, FontWeights.Bold)), cpuLine,
            Columns("*,auto", Label("Memory"), Text($"{s.RamUsedGb:0.0} of {s.RamTotalGb:0} GB", 13, FontWeights.Bold)).Margin(0, 0, 0, 12),
            Label("Top memory"), list.Margin(0, 6, 0, 0),
            PillButton("Task Manager", WinActions.OpenTaskManager).Margin(0, 6, 0, 0));
    }
}

/// <summary>Download / upload speed with a live chart.</summary>
public sealed class NetworkWidget : WidgetBase
{
    TextBlock _down = null!, _up = null!;
    Sparkline _line = null!;
    protected override bool Clickable => false;

    protected override UIElement BuildContent()
    {
        _down = Text("", 11, FontWeights.Bold);
        _up = Text("", 8.5, FontWeights.SemiBold, "Text.Secondary");
        _line = new Sparkline("Accent") { Width = 52, Height = 20, Margin = new Thickness(10, 0, 0, 0) };
        return H(V(H(Icon(Glyphs.Download, 8.5, "Accent").Margin(0, 0, 3, 0), _down), H(Icon(Glyphs.Upload, 7.5, "Text.Secondary").Margin(0, 0, 3, 0), _up)).Margin(1, 0, 0, 0), _line);
    }

    protected override void OnAttach() => Svc.Sys.Updated += Render;
    protected override void OnDetach() => Svc.Sys.Updated -= Render;

    void Render()
    {
        var s = Svc.Sys;
        _down.Text = Fmt.Rate(s.NetDown);
        _up.Text = Fmt.Rate(s.NetUp);
        _line.SetValues(s.NetHistory);
    }
}

/// <summary>The blue battery tile: lightning + percent on an accent gradient.</summary>
public sealed class BatteryWidget : WidgetBase
{
    static readonly Brush BlueFill = Gradient("#46ACFF", "#0A72FF"), RedFill = Gradient("#FF6B60", "#E0281D");
    TextBlock _pct = null!;
    FrameworkElement _bolt = null!;
    protected override int TickSeconds => 15;
    protected override Brush? TileFill => _fill;
    Brush _fill = BlueFill;

    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        Tile.BorderThickness = new Thickness(0);
        _bolt = new BoltMark { Width = 6, Height = 9, Margin = new Thickness(0, 0, 2.5, 0) };
        _pct = Text("", 11, FontWeights.SemiBold, "OnAccent");
        var g = new Grid();
        // A few faint sparkles, like the reference tile.
        foreach (var (x, y, d) in new[] { (7.0, 6.0, 2.0), (31.0, 5.0, 1.6), (35.0, 9.5, 1.3), (20.0, 35.0, 1.6), (9.0, 31.0, 1.2) })
            g.Children.Add(new System.Windows.Shapes.Ellipse { Width = d, Height = d, Fill = Brushes.White, Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x, y, 0, 0), IsHitTestVisible = false });
        g.Children.Add(H(_bolt, _pct).Center());
        return g;
    }

    protected override void OnTick(DateTime now)
    {
        var s = Svc.Sys;
        s.SampleBattery();
        _pct.Text = s.HasBattery ? s.BatteryPercent.ToString() : "AC";
        _bolt.Visibility = s.OnAc ? Visibility.Visible : Visibility.Collapsed;
        var fill = !s.HasBattery || s.BatteryPercent > 20 || s.OnAc ? BlueFill : RedFill;
        if (!ReferenceEquals(fill, _fill)) { _fill = fill; InvalidateTile(); }
        ToolTip = s.HasBattery ? $"{s.BatteryPercent}% {(s.OnAc ? "· charging" : "· on battery")}" : "Plugged in";
    }

    protected override void OnClick() => WinActions.OpenSettings("batterysaver");
}

/// <summary>Dot-matrix battery: 3×10 dots lit to the charge, with date and percent.</summary>
public sealed class BatteryDotsWidget : WidgetBase
{
    readonly Ellipse[] _dots = new Ellipse[30];
    TextBlock _date = null!, _pct = null!;
    protected override int TickSeconds => 15;

    protected override UIElement BuildContent()
    {
        var grid = new UniformGrid { Rows = 3, Columns = 10, Width = 86, Height = 24 };
        for (int i = 0; i < 30; i++) { _dots[i] = new Ellipse { Width = 6.4, Height = 6.4 }; grid.Children.Add(_dots[i]); }
        _date = Text("", 6.5, FontWeights.SemiBold, "Text.Secondary");
        _pct = Text("", 7, FontWeights.Bold);
        return V(grid, Columns("*,auto", _date, _pct).Margin(0, 1, 0, 0));
    }

    protected override void OnTick(DateTime now)
    {
        var s = Svc.Sys;
        s.SampleBattery();
        int pct = s.HasBattery ? s.BatteryPercent : 100;
        int lit = (int)Math.Round(pct / 100.0 * 30);
        // Fill column-major from the left so it reads like a battery level.
        for (int i = 0; i < 30; i++)
        {
            int col = i % 10;
            bool on = col * 3 + (i / 10) < lit;
            _dots[i].Fg(on ? (pct <= 20 && !s.OnAc ? "Sys.Red" : "Text.Primary") : "Fill.Track");
        }
        _date.Text = now.ToString("MMM d");
        _pct.Text = $"{pct}%";
    }

    protected override void OnClick() => WinActions.OpenSettings("batterysaver");
}

public sealed class BatteryRingWidget : WidgetBase
{
    RingGauge _ring = null!;
    TextBlock _v = null!;
    protected override int TickSeconds => 15;

    protected override UIElement BuildContent()
    {
        var r = Rings.Make("%", "Sys.Green");
        (_ring, _v) = (r.ring, r.value);
        return r.host;
    }

    protected override void OnTick(DateTime now)
    {
        var s = Svc.Sys;
        s.SampleBattery();
        _ring.Value = s.BatteryPercent / 100.0;
        _v.Text = s.HasBattery ? s.BatteryPercent.ToString() : "AC";
        _ring.SetResourceReference(RingGauge.FillProperty, s.OnAc ? "Sys.Green" : s.BatteryPercent <= 20 ? "Sys.Red" : "Text.Primary");
    }

    protected override void OnClick() => WinActions.OpenSettings("batterysaver");
}

/// <summary>Wi-Fi and Bluetooth pills; the chevron opens the matching Settings page.</summary>
public sealed class QuickTogglesWidget : WidgetBase
{
    Border _wifi = null!, _bt = null!;
    TextBlock _wifiIcon = null!, _btIcon = null!;
    protected override bool Clickable => false;

    protected override UIElement BuildContent()
    {
        (_wifi, _wifiIcon) = Pill(Glyphs.Wifi, () => _ = Svc.Radios.ToggleAsync(RadioKind.WiFi), p => FlyoutWindow.Toggle(p, WifiCard.Build(), 340));
        _wifi.ToolTip = "Wi-Fi — click the arrow for networks";
        (_bt, _btIcon) = Pill(Glyphs.Bluetooth, () => _ = Svc.Radios.ToggleAsync(RadioKind.Bluetooth), p => FlyoutWindow.Toggle(p, BluetoothCard.Build(), 340));
        _bt.ToolTip = "Bluetooth — click the arrow for devices";
        _bt.Margin = new Thickness(6, 0, 0, 0);
        return H(_wifi, _bt);
    }

    static (Border, TextBlock) Pill(string glyph, Action toggle, Action<FrameworkElement> more)
    {
        var icon = Icon(glyph, 11);
        var left = new Border { Width = 30, Height = 30, Background = Brushes.Transparent, Child = icon, Cursor = System.Windows.Input.Cursors.Hand };
        left.MouseLeftButtonUp += (_, e) => { e.Handled = true; toggle(); };
        var divider = new Border { Width = 1, Height = 12 }.Bg("Divider");
        var chev = new Border { Width = 25, Height = 30, Background = Brushes.Transparent, Child = Icon(Glyphs.ChevronRight, 8.5), Cursor = System.Windows.Input.Cursors.Hand };
        Border? pillRef = null;
        chev.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (pillRef != null) more(pillRef); };
        var pill = new Border { CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(1.25), Child = H(left, divider, chev), Height = 30 };
        pillRef = pill;
        return (pill, icon);
    }

    protected override void OnAttach()
    {
        Svc.Radios.Changed += Render;
        _ = Svc.Radios.InitAsync();
        Render();
    }

    protected override void OnDetach() => Svc.Radios.Changed -= Render;

    void Render()
    {
        ApplyPill(_wifi, _wifiIcon, Svc.Radios.IsOn(RadioKind.WiFi) ?? true);
        ApplyPill(_bt, _btIcon, Svc.Radios.IsOn(RadioKind.Bluetooth) ?? false);
    }

    static void ApplyPill(Border pill, TextBlock icon, bool on)
    {
        pill.Bg(on ? "Accent.Soft" : "Fill.Control");
        pill.SetResourceReference(Border.BorderBrushProperty, on ? "Accent.Border" : "Fill.Control");
        icon.Fg(on ? "Accent" : "Text.Primary");
        foreach (var tb in FindText(pill)) if (tb != icon) tb.Fg(on ? "Accent" : "Text.Primary");
    }

    static IEnumerable<TextBlock> FindText(DependencyObject d)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is TextBlock t) yield return t;
            foreach (var x in FindText(c)) yield return x;
        }
    }
}

/// <summary>Laptop display brightness slider.</summary>
public sealed class BrightnessWidget : WidgetBase
{
    Slider _slider = null!;
    TextBlock _pct = null!;
    bool _syncing;
    protected override int TickSeconds => 20;
    protected override bool Clickable => false;

    protected override UIElement BuildContent()
    {
        _pct = Text("", 8, FontWeights.SemiBold, "Text.Secondary");
        _slider = Slider(50, v =>
        {
            _pct.Text = $"{v:0}%";
            if (!_syncing) Svc.Brightness.Set((int)v);
        });
        _slider.Width = 138;
        var head = Columns("auto,*,auto", Icon(Glyphs.Brightness, 9, "Text.Secondary").Margin(0, 0, 4, 0), Text("Brightness", 9, FontWeights.Bold), _pct);
        return V(head, _slider.Margin(0, 4, 0, 0));
    }

    protected override async void OnTick(DateTime now)
    {
        if (_slider.IsMouseCaptureWithin) return;
        int v = await Task.Run(() => Svc.Brightness.Read());
        _syncing = true;
        if (v >= 0) _slider.Value = v;
        else { _slider.IsEnabled = false; _pct.Text = "n/a"; ToolTip = "This display doesn't expose brightness to Windows (external monitors: roadmap)"; }
        _syncing = false;
    }
}

/// <summary>One-tap actions: theme toggle, notification center, show desktop, Task Manager.</summary>
public sealed class ActionWidget : WidgetBase
{
    public enum Kind { Theme, Notifications, ShowDesktop, TaskManager }
    readonly Kind _kind;
    public ActionWidget(Kind k) => _kind = k;

    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        var glyph = _kind switch
        {
            Kind.Theme => Glyphs.Contrast,
            Kind.Notifications => Glyphs.Bell,
            Kind.ShowDesktop => Glyphs.Desktop,
            _ => Glyphs.Chart,
        };
        ToolTip = _kind switch { Kind.Theme => "Light / dark mode", Kind.Notifications => "Notifications", Kind.ShowDesktop => "Show desktop", _ => "Task Manager" };
        return Icon(glyph, 14);
    }

    protected override void OnClick()
    {
        switch (_kind)
        {
            case Kind.Theme: WinActions.SetSystemLight(!WinActions.IsSystemLight()); break;
            case Kind.Notifications: WinActions.OpenNotificationCenter(); break;
            case Kind.ShowDesktop: WinActions.ShowDesktop(); break;
            case Kind.TaskManager: WinActions.OpenTaskManager(); break;
        }
    }
}

/// <summary>
/// Terminal launcher (owner's addition, not in the reference): click for a card with PowerShell / Command Prompt, normal or
/// as administrator. Middle-click = PowerShell straight away. Alt+T (Settings → Dock) = PowerShell as administrator.
/// </summary>
public sealed class TerminalWidget : WidgetBase
{
    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        ToolTip = "Terminal";
        return Icon(Glyphs.Terminal, 15);
    }

    protected override void OnMiddleClick() => WinActions.LaunchShell(WinActions.ShellKind.PowerShell, admin: false);

    protected override void AddMenuItems(ContextMenu menu)
    {
        foreach (var (name, kind, admin) in Choices)
            menu.Items.Add(Item(name + (admin ? " (admin)" : ""), () => WinActions.LaunchShell(kind, admin)));
    }

    static readonly (string Name, WinActions.ShellKind Kind, bool Admin)[] Choices =
    {
        ("PowerShell", WinActions.ShellKind.PowerShell, false),
        ("PowerShell", WinActions.ShellKind.PowerShell, true),
        ("Command Prompt", WinActions.ShellKind.Cmd, false),
        ("Command Prompt", WinActions.ShellKind.Cmd, true),
    };

    // Brand-style badges (allowed exception to theme colours): PowerShell navy, Command Prompt black.
    static readonly Brush PsBadge = Frozen(Color.FromRgb(0x01, 0x24, 0x56)), CmdBadge = Frozen(Color.FromRgb(0x1E, 0x1E, 0x1E));
    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    protected override UIElement BuildCard()
    {
        var rows = new StackPanel();
        foreach (var (name, kind, admin) in Choices)
        {
            var badge = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(9),
                Background = kind == WinActions.ShellKind.Cmd ? CmdBadge : PsBadge,
                Child = Text(kind == WinActions.ShellKind.Cmd ? "C:\\" : ">_", 12, FontWeights.Bold, "OnAccent").Center(),
            };
            var sub = admin ? "As administrator · Windows asks to confirm"
                : kind == WinActions.ShellKind.PowerShell ? (WinActions.HasPwsh ? "PowerShell 7" : "Windows PowerShell") : "cmd.exe";
            var label = V(Text(name, 13.5, FontWeights.SemiBold), Text(sub, 11.5, null, "Text.Secondary"));
            label.Margin = new Thickness(12, 0, 0, 0);
            var right = admin ? Icon(Glyphs.Shield, 13, "Sys.Orange") : new TextBlock();
            var row = Columns("auto,*,auto", badge, label, right);
            var (k, a) = (kind, admin);
            var b = GhostButton(row, () => { WinActions.LaunchShell(k, a); FlyoutWindow.CloseCurrent(); });
            b.Padding = new Thickness(6);
            b.Margin = new Thickness(-6, 0, -6, 4);
            rows.Children.Add(b);
        }
        var hint = Text(Settings.Current.AdminShellHotkey ? "Alt+T opens PowerShell as administrator from anywhere." : "Middle-click the tile for PowerShell.", 11.5, null, "Text.Tertiary");
        hint.Margin = new Thickness(0, 8, 0, 0);
        return V(Card.Header("Terminal"), rows, hint);
    }
}

/// <summary>Free space on the system drive; card lists every drive.</summary>
public sealed class StorageWidget : WidgetBase
{
    TextBlock _free = null!;
    Action<double> _set = null!;
    protected override int TickSeconds => 120;

    protected override UIElement BuildContent()
    {
        _free = Text("", 10.5, FontWeights.Bold);
        var bar = Progress(out _set, "Accent", 4);
        bar.Width = 80;
        return H(Icon(Glyphs.Storage, 14, "Text.Secondary").Margin(0, 0, 8, 0), V(_free, bar.Margin(0, 5, 0, 0)));
    }

    protected override void OnTick(DateTime now)
    {
        var sys = System.IO.Path.GetPathRoot(Environment.SystemDirectory);
        var d = SystemMonitor.FixedDrives().FirstOrDefault(x => x.Name.Equals(sys, StringComparison.OrdinalIgnoreCase));
        if (d == null) return;
        _free.Text = $"{Fmt.Bytes(d.AvailableFreeSpace)} free";
        _set(1 - (double)d.AvailableFreeSpace / d.TotalSize);
    }

    protected override UIElement BuildCard()
    {
        var list = new StackPanel();
        foreach (var d in SystemMonitor.FixedDrives())
        {
            var bar = Progress(out var set, (double)d.AvailableFreeSpace / d.TotalSize < 0.1 ? "Sys.Red" : "Accent", 6);
            bar.Loaded += (_, _) => set(1 - (double)d.AvailableFreeSpace / d.TotalSize);
            var label = string.IsNullOrEmpty(d.VolumeLabel) ? d.Name : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})";
            var row = V(Columns("*,auto", Text(label, 13, FontWeights.SemiBold), Text($"{Fmt.Bytes(d.AvailableFreeSpace)} free of {Fmt.Bytes(d.TotalSize)}", 12, null, "Text.Secondary")), bar.Margin(0, 6, 0, 0));
            var btn = GhostButton(row, () => WindowTracker.Launch(d.Name));
            btn.Margin = new Thickness(-8, 0, -8, 6);
            list.Children.Add(btn);
        }
        return V(Card.Header("Storage"), list, PillButton("Storage settings", () => WinActions.OpenSettings("storagesense")).Margin(0, 6, 0, 0));
    }
}

public sealed class RecycleBinWidget : WidgetBase
{
    TextBlock _count = null!;
    protected override int TickSeconds => 30;

    protected override UIElement BuildContent()
    {
        _count = Text("", 8.5, FontWeights.SemiBold, "Text.Secondary");
        return H(Icon(Glyphs.Recycle, 14).Margin(0, 0, 7, 0), V(Text("Bin", 10.5, FontWeights.Bold), _count));
    }

    protected override void OnTick(DateTime now)
    {
        var (items, bytes) = WinActions.RecycleBinInfo();
        _count.Text = items == 0 ? "Empty" : $"{items} · {Fmt.Bytes(bytes)}";
    }

    protected override void OnClick() => WindowTracker.Launch("shell:RecycleBinFolder");

    protected override void AddMenuItems(ContextMenu menu)
    {
        menu.Items.Add(Item("Open", () => WindowTracker.Launch("shell:RecycleBinFolder")));
        menu.Items.Add(Item("Empty Recycle Bin…", () => { WinActions.EmptyRecycleBin(); OnTick(DateTime.Now); }));
    }
}

/// <summary>Control Centre button: opens the quick-settings card (Wi-Fi, Bluetooth, airplane, dark mode, sliders…).</summary>
public sealed class ControlCenterWidget : WidgetBase
{
    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        ToolTip = "Control Centre";
        return new ToggleMark { Width = 18, Height = 16 };
    }

    protected override double CardWidth => 360;
    protected override UIElement BuildCard() => ControlCenter.Build();

    /// <summary>Two little switches, the usual "control centre" symbol.</summary>
    sealed class ToggleMark : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            var b = Res("Text.Primary");
            double w = ActualWidth, h = ActualHeight, ph = h * 0.42, r = ph / 2;
            var pen = new Pen(b, 1.3);
            dc.DrawRoundedRectangle(null, pen, new Rect(0.65, 0.65, w - 1.3, ph - 1.3), r - 0.65, r - 0.65);
            dc.DrawEllipse(b, null, new Point(w - r, r), r - 2.2, r - 2.2);
            double y2 = h - ph;
            dc.DrawRoundedRectangle(b, null, new Rect(0, y2, w, ph), r, r);
            dc.DrawEllipse(Res("Tile.Background") is SolidColorBrush s && s.Color.A > 0x80 ? s : Brushes.Black, null, new Point(r, y2 + r), r - 2.2, r - 2.2);
        }
    }
}

/// <summary>
/// System metrics at a glance: CPU, GPU, RAM and network as labelled mini bars (the AI-limits tile layout).
/// Card: live charts, memory, VRAM, and the top processes by CPU and by memory.
/// </summary>
public sealed class SystemMonitorWidget : WidgetBase
{
    readonly Action<double>[] _bars = new Action<double>[3];
    readonly TextBlock[] _vals = new TextBlock[3];
    TextBlock _net = null!;

    protected override UIElement BuildContent()
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        string[] names = { "CPU", "GPU", "RAM" };
        for (int r = 0; r < 3; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(11.5) });
            grid.Children.Add(Text(names[r], 7.5, FontWeights.Bold, "Text.Secondary").Row(r));
            var bar = Progress(out _bars[r], "Accent", 3).Row(r).Col(1);
            bar.Margin = new Thickness(0, 0, 4, 0);
            grid.Children.Add(bar);
            _vals[r] = Text("—", 8.5, FontWeights.Bold).Row(r).Col(2).Right();
            grid.Children.Add(_vals[r]);
        }
        _net = Text("", 7.5, FontWeights.SemiBold, "Text.Secondary");
        var net = V(Icon(Glyphs.Download, 9, "Accent"), _net.Center());
        net.Margin = new Thickness(10, 0, 0, 0);
        net.MinWidth = 40;
        return H(grid, net);
    }

    protected override void OnAttach() { Svc.Sys.Updated += Render; Svc.Gpu.Updated += Render; }
    protected override void OnDetach() { Svc.Sys.Updated -= Render; Svc.Gpu.Updated -= Render; }

    void Render()
    {
        var s = Svc.Sys;
        var g = Svc.Gpu;
        Set(0, s.Cpu);
        Set(1, g.Load);
        Set(2, s.Ram);
        _net.Text = Fmt.Rate(s.NetDown);
    }

    void Set(int i, double v)
    {
        _bars[i](Math.Max(0, v) / 100);
        _vals[i].Text = v < 0 ? "—" : $"{v:0}%";
    }

    protected override double CardWidth => 380;
    protected override UIElement BuildCard()
    {
        var s = Svc.Sys;
        var g = Svc.Gpu;
        Sparkline Chart(IEnumerable<double> values, string brush, double? max = 100)
        {
            var c = new Sparkline(brush) { Height = 34, Max = max, Margin = new Thickness(0, 4, 0, 10) };
            c.SetValues(values);
            return c;
        }
        var cpuV = Text($"{s.Cpu:0}%", 13, FontWeights.Bold);
        var gpuV = Text(g.Load < 0 ? "—" : $"{g.Load:0}%", 13, FontWeights.Bold);
        var ramV = Text($"{s.RamUsedGb:0.0} / {s.RamTotalGb:0} GB", 13, FontWeights.Bold);
        var netV = Text($"↓ {Fmt.Rate(s.NetDown)}   ↑ {Fmt.Rate(s.NetUp)}", 13, FontWeights.Bold);
        var cpuC = Chart(s.CpuHistory, "Accent");
        var gpuC = Chart(g.History, "Sys.Purple");
        var ramC = Chart(s.RamHistory, "Sys.Green");
        var netC = Chart(s.NetHistory, "Sys.Orange", null);
        void Live()
        {
            cpuV.Text = $"{s.Cpu:0}%"; gpuV.Text = g.Load < 0 ? "—" : $"{g.Load:0}%";
            ramV.Text = $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0} GB{(g.VramGb >= 0 ? $"  ·  VRAM {g.VramGb:0.0} GB" : "")}";
            netV.Text = $"↓ {Fmt.Rate(s.NetDown)}   ↑ {Fmt.Rate(s.NetUp)}";
            cpuC.SetValues(s.CpuHistory); gpuC.SetValues(g.History); ramC.SetValues(s.RamHistory); netC.SetValues(s.NetHistory);
        }
        var top = new StackPanel();
        top.Children.Add(Text("Measuring…", 12, null, "Text.Secondary"));
        var root = V(Card.Header("System"),
            Columns("*,auto", Label("Processor"), cpuV), cpuC,
            Columns("*,auto", Label("Graphics"), gpuV), gpuC,
            Columns("*,auto", Label("Memory"), ramV), ramC,
            Columns("*,auto", Label("Network"), netV), netC,
            Label("Top processes (CPU · memory)").Margin(0, 2, 0, 8), top,
            PillButton("Task Manager", WinActions.OpenTaskManager).Margin(0, 8, 0, 0));
        root.Loaded += async (_, _) =>
        {
            s.Updated += Live; g.Updated += Live;
            var procs = await Task.Run(TopProcesses);
            top.Children.Clear();
            foreach (var p in procs)
                top.Children.Add(Columns("*,52,64", Text(p.name, 12.5, FontWeights.SemiBold), Text($"{p.cpu:0.0}%", 12, null, "Text.Secondary").Right(),
                    Text(Fmt.Bytes(p.mem), 12, null, "Text.Secondary").Right()).Margin(0, 0, 0, 6));
        };
        root.Unloaded += (_, _) => { s.Updated -= Live; g.Updated -= Live; };
        return root;
    }

    /// <summary>Two snapshots of process CPU time one second apart → per-app CPU % (all cores = 100 %).</summary>
    static List<(string name, double cpu, long mem)> TopProcesses()
    {
        static Dictionary<int, (string name, TimeSpan cpu, long mem)> Snap()
        {
            var d = new Dictionary<int, (string, TimeSpan, long)>();
            foreach (var p in Process.GetProcesses())
            {
                try { d[p.Id] = (p.ProcessName, p.TotalProcessorTime, p.WorkingSet64); } catch { }
                finally { p.Dispose(); }
            }
            return d;
        }
        var a = Snap();
        var t0 = DateTime.UtcNow;
        Thread.Sleep(1000);
        var b = Snap();
        double wall = (DateTime.UtcNow - t0).TotalMilliseconds * Environment.ProcessorCount;
        return b.Select(kv => (kv.Value.name, cpu: a.TryGetValue(kv.Key, out var o) ? (kv.Value.cpu - o.cpu).TotalMilliseconds / wall * 100 : 0, kv.Value.mem))
            .GroupBy(x => x.name).Select(gr => (name: gr.Key, cpu: gr.Sum(x => x.cpu), mem: gr.Sum(x => x.mem)))
            .Where(x => x.name != "Idle").OrderByDescending(x => x.cpu).ThenByDescending(x => x.mem).Take(6).ToList();
    }
}

/// <summary>GPU temperature (WDDM adapter data, no driver needed) with VRAM and load; the card charts it.</summary>
public sealed class TemperatureWidget : WidgetBase
{
    RingGauge _ring = null!;
    TextBlock _v = null!, _sub = null!;

    protected override UIElement BuildContent()
    {
        var r = Rings.Make("°C", "Sys.Orange");
        (_ring, _v) = (r.ring, r.value);
        _sub = Text("", 9, FontWeights.SemiBold);
        return H(r.host, V(Caption("Graphics"), _sub).Margin(8, 0, 2, 0));
    }

    protected override void OnAttach() => Svc.Gpu.Updated += Render;
    protected override void OnDetach() => Svc.Gpu.Updated -= Render;

    void Render()
    {
        var g = Svc.Gpu;
        if (g.TempC < 0) { _v.Text = "—"; _ring.Value = 0; _sub.Text = "No sensor"; return; }
        _v.Text = $"{g.TempC:0}";
        _ring.Value = Math.Clamp(g.TempC / 100, 0, 1);
        _sub.Text = g.Load >= 0 ? $"{g.Load:0}% load" : "";
    }

    static TextBlock Note(string s) { var t = Text(s, 11, null, "Text.Secondary"); t.TextWrapping = TextWrapping.Wrap; return t; }

    protected override UIElement BuildCard()
    {
        var g = Svc.Gpu;
        var value = Text(g.TempC < 0 ? "—" : $"{g.TempC:0} °C", 13, FontWeights.Bold);
        var load = Text(g.Load < 0 ? "—" : $"{g.Load:0}%", 13, FontWeights.Bold);
        var vram = Text(g.VramGb < 0 ? "—" : $"{g.VramGb:0.0} GB", 13, FontWeights.Bold);
        var chart = new Sparkline("Sys.Orange") { Height = 50, Max = 100, Margin = new Thickness(0, 4, 0, 12) };
        chart.SetValues(g.TempHistory);
        void Live()
        {
            value.Text = g.TempC < 0 ? "—" : $"{g.TempC:0} °C";
            load.Text = g.Load < 0 ? "—" : $"{g.Load:0}%";
            vram.Text = g.VramGb < 0 ? "—" : $"{g.VramGb:0.0} GB";
            chart.SetValues(g.TempHistory);
        }
        var root = V(Card.Header("Temperature"),
            Columns("*,auto", Label("Graphics card"), value), chart,
            Columns("*,auto", Label("Load"), load).Margin(0, 0, 0, 8),
            Columns("*,auto", Label("Video memory"), vram).Margin(0, 0, 0, 8),
            Note("Processor temperature needs a kernel driver on Windows, so Vela shows the GPU's own sensor."));
        root.Loaded += (_, _) => g.Updated += Live;
        root.Unloaded += (_, _) => g.Updated -= Live;
        return root;
    }
}
