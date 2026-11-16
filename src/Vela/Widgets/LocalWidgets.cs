using System.Globalization;
using System.Runtime.InteropServices;
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

// Reference widgets that need no account: more clocks, battery shapes, keyboard / language, weather details,
// Claude token usage. All ride the shared 1 Hz ticker (or slower) like every other tile.

/// <summary>Weekday over the full date ("Tuesday / November 17").</summary>
public sealed class DateWidget : WidgetBase
{
    TextBlock _wd = null!, _date = null!;
    protected override int TickSeconds => 30;
    protected override UIElement BuildContent()
    {
        _wd = Text("", 7.5, FontWeights.SemiBold, "Text.Secondary");
        _date = Text("", 13, FontWeights.SemiBold);
        return V(_wd, _date.Margin(0, -1, 0, 0)).Margin(2, 0);
    }
    protected override void OnTick(DateTime now)
    {
        _wd.Text = now.ToString("dddd", CultureInfo.CurrentCulture);
        _date.Text = now.ToString("MMMM d", CultureInfo.CurrentCulture);
    }
    protected override UIElement BuildCard() => new MonthCalendar();
    protected override double CardWidth => 320;
}

/// <summary>"TODAY" in red, the short date, and where we are in the year (week number, day of year).</summary>
public sealed class TodayWidget : WidgetBase
{
    TextBlock _date = null!, _sub = null!;
    protected override int TickSeconds => 30;
    protected override UIElement BuildContent()
    {
        _date = Text("", 12, FontWeights.SemiBold);
        _sub = Text("", 7, FontWeights.SemiBold, "Text.Secondary");
        return V(Text("TODAY", 6.75, FontWeights.Bold, "Sys.Red"), _date.Margin(0, -1, 0, 0), _sub).Margin(2, 0);
    }
    protected override void OnTick(DateTime now)
    {
        _date.Text = now.ToString("ddd, MMM d", CultureInfo.CurrentCulture);
        int week = ISOWeek.GetWeekOfYear(now), days = DateTime.IsLeapYear(now.Year) ? 366 : 365;
        _sub.Text = $"Week {week} · day {now.DayOfYear} of {days}";
    }
    protected override UIElement BuildCard() => new MonthCalendar();
    protected override double CardWidth => 320;
}

/// <summary>This week as seven columns; today is a red pill.</summary>
public sealed class SevenDaysWidget : WidgetBase
{
    readonly TextBlock[] _letters = new TextBlock[7], _numbers = new TextBlock[7];
    readonly Border[] _pills = new Border[7];
    protected override int TickSeconds => 60;
    protected override UIElement BuildContent()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < 7; i++)
        {
            _letters[i] = Text("", 6.5, FontWeights.Bold, "Text.Secondary").Center();
            _numbers[i] = Text("", 10, FontWeights.SemiBold).Center();
            _pills[i] = new Border { Width = 19, Height = 19, CornerRadius = new CornerRadius(9.5), Child = _numbers[i], Margin = new Thickness(0, 1, 0, 0) };
            row.Children.Add(V(_letters[i], _pills[i]).Margin(i == 0 ? 0 : 2, 0, 0, 0));
        }
        return row;
    }
    protected override void OnTick(DateTime now)
    {
        var first = now.Date.AddDays(-(int)now.DayOfWeek); // the week as the calendar shows it (Sunday first)
        for (int i = 0; i < 7; i++)
        {
            var d = first.AddDays(i);
            bool today = d == now.Date;
            _letters[i].Text = d.ToString("ddd", CultureInfo.CurrentCulture)[..1].ToUpperInvariant();
            _numbers[i].Text = d.Day.ToString();
            _numbers[i].Fg(today ? "OnAccent" : d < now.Date ? "Text.Secondary" : "Text.Primary");
            if (today) _pills[i].Bg("Sys.Red"); else _pills[i].Background = Brushes.Transparent;
        }
    }
    protected override UIElement BuildCard() => new MonthCalendar();
    protected override double CardWidth => 320;
}

/// <summary>Seven-segment digits, drawn as geometry (no font needed); unlit segments stay faintly visible.</summary>
public sealed class SegmentDisplay : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(SegmentDisplay),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    string _text = "";
    public string Value { get => _text; set { if (_text == value) return; _text = value; InvalidateMeasure(); InvalidateVisual(); } }

    // Segments a..g for 0-9 (bit 0 = a … bit 6 = g).
    static readonly int[] Map = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };

    protected override Size MeasureOverride(Size available)
    {
        double h = double.IsInfinity(available.Height) ? 22 : available.Height;
        return new Size(TextWidth(h), h);
    }

    double TextWidth(double h) => _text.Sum(c => c == ':' ? h * 0.22 : h * 0.62);

    protected override void OnRender(DrawingContext dc)
    {
        double h = ActualHeight, w = h * 0.5, t = h * 0.11, x = 0;
        var on = Fill;
        var off = on.Clone(); off.Opacity = 0.09; off.Freeze();
        foreach (var c in _text)
        {
            if (c == ':')
            {
                double r = t * 0.55;
                dc.DrawEllipse(on, null, new Point(x + h * 0.08, h * 0.32), r, r);
                dc.DrawEllipse(on, null, new Point(x + h * 0.08, h * 0.68), r, r);
                x += h * 0.22;
                continue;
            }
            int bits = char.IsDigit(c) ? Map[c - '0'] : 0;
            void Seg(int i, double x0, double y0, double x1, double y1)
            {
                var b = (bits & (1 << i)) != 0 ? on : off;
                var pen = new Pen(b, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, new Point(x + x0, y0), new Point(x + x1, y1));
            }
            double l = t, r2 = w - t / 2, top = t / 2 + 0.5, mid = h / 2, bot = h - t / 2 - 0.5, g = t * 0.9;
            Seg(0, l + g * 0.3, top, r2 - g * 0.3, top);       // a
            Seg(1, r2, top + g, r2, mid - g * 0.6);            // b
            Seg(2, r2, mid + g * 0.6, r2, bot - g);            // c
            Seg(3, l + g * 0.3, bot, r2 - g * 0.3, bot);       // d
            Seg(4, l - t / 2, mid + g * 0.6, l - t / 2, bot - g); // e
            Seg(5, l - t / 2, top + g, l - t / 2, mid - g * 0.6); // f
            Seg(6, l + g * 0.3, mid, r2 - g * 0.3, mid);       // g
            x += h * 0.62;
        }
    }
}

public sealed class SegmentClockWidget : WidgetBase
{
    SegmentDisplay _digits = null!;
    TextBlock _ampm = null!;
    protected override int TickSeconds => 1;
    protected override UIElement BuildContent()
    {
        _digits = new SegmentDisplay { Height = 22, VerticalAlignment = VerticalAlignment.Center };
        _digits.SetResourceReference(SegmentDisplay.FillProperty, "Sys.Red");
        _ampm = Text("", 7, FontWeights.Bold, "Text.Secondary").Margin(4, 0, 0, 2);
        _ampm.VerticalAlignment = VerticalAlignment.Bottom;
        return H(_digits, _ampm).Margin(3, 0);
    }
    protected override void OnTick(DateTime now)
    {
        bool h24 = Settings.Current.Use24Hour;
        _digits.Value = now.ToString(h24 ? "HH:mm" : "hh:mm");
        _ampm.Text = h24 ? "" : now.ToString("tt", CultureInfo.InvariantCulture);
    }
    protected override void OnClick() => WindowTracker.OpenUri("ms-clock:");
}

/// <summary>Big weather glyph, the time, and the temperature with its description.</summary>
public sealed class ClockWeatherWidget : WidgetBase
{
    WeatherIcon _icon = null!;
    TextBlock _time = null!, _sub = null!;
    protected override int TickSeconds => 1;
    protected override UIElement BuildContent()
    {
        _icon = new WeatherIcon { Width = 22, Height = 22, Margin = new Thickness(2, 0, 9, 0) };
        _time = Text("", 13, FontWeights.SemiBold);
        _sub = Text("", 7.5, FontWeights.SemiBold, "Text.Secondary");
        return H(_icon, V(_time, _sub.Margin(0, -1, 0, 0)));
    }
    protected override void OnAttach() { if (Svc.Weather.Configured) Svc.Weather.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Weather.Changed -= Render;
    void Render()
    {
        var w = Svc.Weather;
        _icon.Visibility = w.HasData ? Visibility.Visible : Visibility.Collapsed;
        if (w.HasData) _icon.Set(WeatherService.Kind(w.Code), !w.IsDay);
        _sub.Text = w.HasData ? $"{w.Temp:0}° · {WeatherService.Describe(w.Code)}" : Svc.Weather.Configured ? "…" : "Set a city in Settings";
    }
    protected override void OnTick(DateTime now) => _time.Text = now.ToString(Settings.Current.Use24Hour ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
    protected override void OnClick() => WindowTracker.OpenUri("ms-clock:");
}

/// <summary>Half-circle gauge (speedometer style); value 0..1. Colours are theme resources.</summary>
public sealed class ArcGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(ArcGauge),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(ArcGauge),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public double Thickness { get; set; } = 3.5;

    public ArcGauge()
    {
        SetResourceReference(TrackProperty, "Fill.Track");
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, r = w / 2 - Thickness / 2, cx = w / 2, cy = ActualHeight - Thickness / 2;
        Draw(dc, Track, 1);
        if (Value > 0.005) Draw(dc, Fill, Math.Clamp(Value, 0, 1));

        void Draw(DrawingContext d, Brush b, double v)
        {
            double a = Math.PI * (1 - v); // from 180° (left) towards 0° (right)
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(cx - r, cy), false, false);
                c.ArcTo(new Point(cx + r * Math.Cos(a), cy - r * Math.Sin(a)), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            d.DrawGeometry(null, new Pen(b, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
        }
    }
}

public sealed class BatteryGaugeWidget : WidgetBase
{
    ArcGauge _gauge = null!;
    TextBlock _pct = null!;
    protected override int TickSeconds => 15;
    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight + 12;
        Tile.Padding = new Thickness(0);
        _gauge = new ArcGauge { Width = 40, Height = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0) };
        _pct = Text("", 10.5, FontWeights.Bold).Center();
        _pct.VerticalAlignment = VerticalAlignment.Bottom;
        _pct.Margin = new Thickness(0, 0, 0, 7);
        var g = new Grid();
        g.Children.Add(_gauge);
        g.Children.Add(_pct);
        return g;
    }
    protected override void OnTick(DateTime now)
    {
        var s = Svc.Sys;
        s.SampleBattery();
        _gauge.Value = (s.HasBattery ? s.BatteryPercent : 100) / 100.0;
        _gauge.SetResourceReference(ArcGauge.FillProperty, s.OnAc ? "Sys.Green" : s.BatteryPercent <= 20 ? "Sys.Red" : "Text.Primary");
        _pct.Text = s.HasBattery ? $"{s.BatteryPercent}%" : "AC";
        ToolTip = s.HasBattery ? $"{s.BatteryPercent}% {(s.OnAc ? "· charging" : "· on battery")}" : "Plugged in";
    }
    protected override void OnClick() => WinActions.OpenSettings("batterysaver");
}

/// <summary>A battery shape that fills to the charge, with the percentage beside it.</summary>
public sealed class BatteryCoreWidget : WidgetBase
{
    Border _fill = null!;
    TextBlock _pct = null!, _state = null!;
    protected override int TickSeconds => 15;
    const double InnerWidth = 30;
    protected override UIElement BuildContent()
    {
        _fill = new Border { CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        var body = new Border { Width = InnerWidth + 6, Height = 17, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.3), Padding = new Thickness(1.7), Child = _fill };
        body.SetResourceReference(Border.BorderBrushProperty, "Text.Secondary");
        var nub = new Border { Width = 2.2, Height = 6, CornerRadius = new CornerRadius(0, 1.5, 1.5, 0), Margin = new Thickness(1, 0, 0, 0) }.Bg("Text.Secondary");
        _pct = Text("", 12, FontWeights.SemiBold);
        _state = Text("", 7, FontWeights.SemiBold, "Text.Secondary");
        return H(body, nub, V(_pct, _state.Margin(0, -1, 0, 0)).Margin(9, 0, 2, 0));
    }
    protected override void OnTick(DateTime now)
    {
        var s = Svc.Sys;
        s.SampleBattery();
        int pct = s.HasBattery ? s.BatteryPercent : 100;
        _fill.Width = Math.Max(2, InnerWidth * pct / 100.0);
        _fill.Bg(s.OnAc ? "Sys.Green" : pct <= 20 ? "Sys.Red" : "Text.Primary");
        _pct.Text = s.HasBattery ? $"{pct}%" : "AC";
        _state.Text = !s.HasBattery ? "plugged in" : s.OnAc ? "charging" : "on battery";
    }
    protected override void OnClick() => WinActions.OpenSettings("batterysaver");
}

/// <summary>On-screen keyboard (reference "Keyboard" tile).</summary>
public sealed class KeyboardWidget : WidgetBase
{
    const string KeyboardGlyph = "";
    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        ToolTip = "On-screen keyboard";
        return Icon(KeyboardGlyph, 15);
    }
    protected override void OnClick() => WindowTracker.Launch("osk.exe");
    protected override void AddMenuItems(ContextMenu menu) => menu.Items.Add(Item("Keyboard settings", () => WinActions.OpenSettings("keyboard")));
}

/// <summary>The input language of the app in front ("ENG", "ARA"…). Click: the Windows language switcher (Win+Space).</summary>
public sealed class LanguageWidget : WidgetBase
{
    TextBlock _code = null!;
    protected override int TickSeconds => 1;
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);

    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        _code = Text("", 10, FontWeights.Bold);
        return _code.Center();
    }
    protected override void OnTick(DateTime now)
    {
        try
        {
            var hkl = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero));
            var ci = CultureInfo.GetCultureInfo((int)((long)hkl & 0xFFFF));
            var code = ci.ThreeLetterISOLanguageName.ToUpperInvariant(); // "ENG", "ARA", "DEU"
            if (_code.Text != code) { _code.Text = code; ToolTip = ci.DisplayName; }
        }
        catch { if (_code.Text.Length == 0) _code.Text = "—"; }
    }
    protected override void OnClick() => WinActions.SendKeys(0x5B, 0x20); // Win+Space
    protected override void AddMenuItems(ContextMenu menu) => menu.Items.Add(Item("Language settings", () => WinActions.OpenSettings("regionlanguage")));
}

/// <summary>Feels like, humidity and wind in three small columns.</summary>
public sealed class WeatherDetailsWidget : WidgetBase
{
    readonly TextBlock[] _values = new TextBlock[3];
    protected override UIElement BuildContent()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        string[] labels = { "Feels", "Humidity", "Wind" };
        for (int i = 0; i < 3; i++)
        {
            _values[i] = Text("—", 12, FontWeights.SemiBold);
            row.Children.Add(V(Text(labels[i], 6.75, FontWeights.SemiBold, "Text.Secondary"), _values[i]).Margin(i == 0 ? 2 : 13, 0, 0, 0));
        }
        return row;
    }
    protected override void OnAttach() { if (Svc.Weather.Configured) Svc.Weather.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Weather.Changed -= Render;
    void Render()
    {
        var w = Svc.Weather;
        if (!w.HasData) { foreach (var v in _values) v.Text = "—"; ToolTip = w.Configured ? null : "Set a city in Settings → Weather"; return; }
        _values[0].Text = $"{w.FeelsLike:0}°";
        _values[1].Text = $"{w.Humidity}%";
        _values[2].Text = $"{w.Wind:0} {(w.Unit == "F" ? "mph" : "km/h")}";
    }
}

/// <summary>Claude Code tokens from its local logs (no sign-in needed): today, and the last 7 days.</summary>
public sealed class ClaudeTokensWidget : WidgetBase
{
    TextBlock _today = null!, _week = null!;
    protected override UIElement BuildContent()
    {
        _today = Text("—", 13, FontWeights.SemiBold);
        _week = Text("", 7, FontWeights.SemiBold, "Text.Secondary");
        return H(BrandBadges.Claude(), V(_today, _week.Margin(0, -1, 0, 0)).Margin(9, 0, 3, 0));
    }
    protected override void OnAttach() { Svc.Claude.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Claude.Changed -= Render;
    void Render()
    {
        var c = Svc.Claude;
        _today.Text = $"{Fmt.Tokens(c.TokensToday)} today";
        _week.Text = $"{Fmt.Tokens(c.TokensWeek)} · 7 days" + (c.TopModel != null ? $" · {c.TopModel}" : "");
    }
    protected override void OnClick() => _ = Svc.Claude.RefreshAsync();
}
