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
    public static (Grid host, RingGauge ring, TextBlock value) Make(string label, string fill, double size = 38)
    {
        var ring = new RingGauge(fill) { Width = size, Height = size, Thickness = 3.5 };
        var value = Text("0", 12.5, FontWeights.Bold).Center();
        var lbl = Text(label, 7.5, FontWeights.Bold, "Text.Secondary").Center();
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
        r.host.Margin = new Thickness(8, 0, 0, 0);
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
        _sub = Text("", 10.5, FontWeights.SemiBold, "Text.Secondary");
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
            Columns("*,auto", Caption("CPU"), Text($"{s.Cpu:0}%", 13, FontWeights.Bold)), cpuLine,
            Columns("*,auto", Caption("Memory"), Text($"{s.RamUsedGb:0.0} of {s.RamTotalGb:0} GB", 13, FontWeights.Bold)).Margin(0, 0, 0, 12),
            Caption("Top memory"), list.Margin(0, 6, 0, 0),
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
        _down = Text("", 13, FontWeights.Bold);
        _up = Text("", 10.5, FontWeights.SemiBold, "Text.Secondary");
        _line = new Sparkline("Accent") { Width = 60, Height = 26, Margin = new Thickness(12, 0, 2, 0) };
        return H(V(H(Icon(Glyphs.Download, 10, "Accent").Margin(0, 0, 4, 0), _down), H(Icon(Glyphs.Upload, 9, "Text.Secondary").Margin(0, 0, 4, 0), _up)).Margin(2, 0, 0, 0), _line);
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
    TextBlock _pct = null!, _bolt = null!;
    protected override int TickSeconds => 15;
    protected override Brush? TileFill => _fill;
    Brush _fill = Gradient("#4AA8FF", "#0A6CFF");

    protected override UIElement BuildContent()
    {
        Tile.Width = 50;
        Tile.Padding = new Thickness(0);
        _bolt = Text("⚡", 12, FontWeights.Bold, "OnAccent");
        _pct = Text("", 15, FontWeights.Bold, "OnAccent");
        return H(_bolt, _pct).Center();
    }

    protected override void OnTick(DateTime now)
    {
        var s = Svc.Sys;
        s.SampleBattery();
        _pct.Text = s.HasBattery ? s.BatteryPercent.ToString() : "AC";
        _bolt.Visibility = s.OnAc ? Visibility.Visible : Visibility.Collapsed;
        var fill = !s.HasBattery || s.BatteryPercent > 20 || s.OnAc ? Gradient("#4AA8FF", "#0A6CFF") : Gradient("#FF6B60", "#E0281D");
        if (!ReferenceEquals(fill, _fill)) { _fill = fill; InvalidateTile(); }
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
        var grid = new UniformGrid { Rows = 3, Columns = 10, Width = 92, Height = 27 };
        for (int i = 0; i < 30; i++) { _dots[i] = new Ellipse { Width = 7.5, Height = 7.5 }; grid.Children.Add(_dots[i]); }
        _date = Text("", 8.5, FontWeights.SemiBold, "Text.Secondary");
        _pct = Text("", 8.5, FontWeights.Bold);
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
        (_wifi, _wifiIcon) = Pill(Glyphs.Wifi, () => _ = Svc.Radios.ToggleAsync(RadioKind.WiFi), () => WinActions.OpenSettings("network-wifi"));
        (_bt, _btIcon) = Pill(Glyphs.Bluetooth, () => _ = Svc.Radios.ToggleAsync(RadioKind.Bluetooth), () => WinActions.OpenSettings("bluetooth"));
        _bt.Margin = new Thickness(6, 0, 0, 0);
        return H(_wifi, _bt);
    }

    static (Border, TextBlock) Pill(string glyph, Action toggle, Action more)
    {
        var icon = Icon(glyph, 14);
        var left = new Border { Width = 36, Height = 34, Background = Brushes.Transparent, Child = icon, Cursor = System.Windows.Input.Cursors.Hand };
        left.MouseLeftButtonUp += (_, e) => { e.Handled = true; toggle(); };
        var divider = new Border { Width = 1, Height = 16 }.Bg("Divider");
        var chev = new Border { Width = 30, Height = 34, Background = Brushes.Transparent, Child = Icon(Glyphs.ChevronRight, 10), Cursor = System.Windows.Input.Cursors.Hand };
        chev.MouseLeftButtonUp += (_, e) => { e.Handled = true; more(); };
        var pill = new Border { CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(1.5), Child = H(left, divider, chev), Height = 36 };
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
        _pct = Text("", 11, FontWeights.SemiBold, "Text.Secondary");
        _slider = Slider(50, v =>
        {
            _pct.Text = $"{v:0}%";
            if (!_syncing) Svc.Brightness.Set((int)v);
        });
        _slider.Width = 140;
        var head = Columns("auto,*,auto", Icon(Glyphs.Brightness, 12, "Text.Secondary").Margin(0, 0, 5, 0), Caption("Brightness", "Text.Primary"), _pct);
        return V(head, _slider.Margin(0, 3, 0, 0));
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
        Tile.Width = 48;
        Tile.Padding = new Thickness(0);
        var glyph = _kind switch
        {
            Kind.Theme => Glyphs.Contrast,
            Kind.Notifications => Glyphs.Bell,
            Kind.ShowDesktop => Glyphs.Desktop,
            _ => Glyphs.Chart,
        };
        ToolTip = _kind switch { Kind.Theme => "Light / dark mode", Kind.Notifications => "Notifications", Kind.ShowDesktop => "Show desktop", _ => "Task Manager" };
        return Icon(glyph, 17);
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

/// <summary>Free space on the system drive; card lists every drive.</summary>
public sealed class StorageWidget : WidgetBase
{
    TextBlock _free = null!;
    Action<double> _set = null!;
    protected override int TickSeconds => 120;

    protected override UIElement BuildContent()
    {
        _free = Text("", 13, FontWeights.Bold);
        var bar = Progress(out _set, "Accent", 5);
        bar.Width = 90;
        return H(Icon(Glyphs.Storage, 18, "Text.Secondary").Margin(0, 0, 10, 0), V(_free, bar.Margin(0, 6, 0, 0)));
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
        _count = Text("", 11, FontWeights.SemiBold, "Text.Secondary");
        return H(Icon(Glyphs.Recycle, 18).Margin(0, 0, 8, 0), V(Text("Bin", 12.5, FontWeights.Bold), _count));
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
