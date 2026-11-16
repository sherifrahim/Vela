using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Vela.Services;

namespace Vela.UI;

/// <summary>Segoe Fluent Icons / MDL2 code points used across the app.</summary>
public static class Glyphs
{
    public const string Play = "", Pause = "", Next = "", Previous = "";
    public const string Volume = "", Volume0 = "", Volume1 = "", Volume2 = "", Mute = "";
    public const string Mic = "", MicOff = "";
    public const string Settings = "", Close = "", Edit = "", Add = "", Delete = "", Remove = "";
    public const string Wifi = "", Bluetooth = "", Airplane = "";
    public const string Calendar = "", Clock = "", Stopwatch = "", Timer = "";
    public const string Search = "", Refresh = "", Check = "", ChevronRight = "", ChevronLeft = "";
    public const string ChevronDown = "", ChevronUp = "";
    public const string Pin = "", Unpin = "", More = "";
    public const string Brightness = "", Moon = "", Contrast = "";
    public const string Bell = "", Desktop = "", Storage = "", Recycle = "";
    public const string Mail = "", Globe = "", Code = "", Lightning = "", Plug = "";
    public const string Skip = "", Reset = "", Download = "", Upload = "";
    public const string Grid = "", AllApps = "", Cpu = "", Memory = "", Network = "";
    public const string Money = "", Chart = "", Note = "", Task = "", Lock = "";
    // Segoe Fluent / MDL2: CommandPrompt (U+E756), Shield (U+EA18).
    public const string Terminal = "", Shield = "";
}

/// <summary>Code-side UI builders. Brushes use SetResourceReference so theme switches are live.</summary>
public static class UI
{
    /// <summary>Inter Display (bundled, SIL OFL) with Segoe fallbacks. Closest open match to SF Pro Display.</summary>
    public static readonly FontFamily Font = new(new Uri("pack://application:,,,/Vela;component/"), "./Assets/Fonts/#Inter Display, Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static T Fg<T>(this T e, string brushKey) where T : FrameworkElement
    {
        switch (e)
        {
            case TextBlock tb: tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey); break;
            case Control c: c.SetResourceReference(Control.ForegroundProperty, brushKey); break;
            case Shape s: s.SetResourceReference(Shape.FillProperty, brushKey); break;
        }
        return e;
    }

    public static T Bg<T>(this T e, string brushKey) where T : FrameworkElement
    {
        switch (e)
        {
            case Border b: b.SetResourceReference(Border.BackgroundProperty, brushKey); break;
            case Panel p: p.SetResourceReference(Panel.BackgroundProperty, brushKey); break;
            case Control c: c.SetResourceReference(Control.BackgroundProperty, brushKey); break;
        }
        return e;
    }

    public static T Margin<T>(this T e, double l, double t, double r, double b) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }
    public static T Margin<T>(this T e, double h, double v) where T : FrameworkElement { e.Margin = new Thickness(h, v, h, v); return e; }
    public static T Margin<T>(this T e, double all) where T : FrameworkElement { e.Margin = new Thickness(all); return e; }
    public static T Col<T>(this T e, int c) where T : UIElement { Grid.SetColumn(e, c); return e; }
    public static T Row<T>(this T e, int r) where T : UIElement { Grid.SetRow(e, r); return e; }
    public static T Center<T>(this T e) where T : FrameworkElement { e.HorizontalAlignment = HorizontalAlignment.Center; e.VerticalAlignment = VerticalAlignment.Center; return e; }
    public static T Right<T>(this T e) where T : FrameworkElement { e.HorizontalAlignment = HorizontalAlignment.Right; return e; }
    public static T Tip<T>(this T e, string tip) where T : FrameworkElement { e.ToolTip = tip; return e; }

    public static TextBlock Text(string text = "", double size = 12, FontWeight? weight = null, string brush = "Text.Primary")
    {
        var t = new TextBlock
        {
            Text = text,
            FontFamily = Font,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        TextOptions.SetTextFormattingMode(t, TextFormattingMode.Display);
        // Tabular digits only for number-like text (clocks, %, durations): digits don't jiggle as values tick. Inter's tabular
        // set also widens punctuation such as the hyphen, so prose ("sign-in") keeps proportional figures.
        if (NumberLike.IsMatch(text)) System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t.Fg(brush);
    }

    /// <summary>Digits plus the units and separators that appear in values ("12:05 PM", "2h 24m", "56%", "4.2 GB", "$15").</summary>
    static readonly System.Text.RegularExpressions.Regex NumberLike = new(@"^[\d\s:.,%°+−–—/·hmsdwAPMKGTB$€£¥x]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Tile caption ("Screen Time", "Session"...).</summary>
    public static TextBlock Caption(string text, string brush = "Text.Secondary", double size = 8) => Text(text, size, FontWeights.SemiBold, brush);
    /// <summary>Section label inside cards and windows ("Today", "Feels like"...).</summary>
    public static TextBlock Label(string text, string brush = "Text.Secondary") => Text(text, 11, FontWeights.SemiBold, brush);
    /// <summary>Tile headline value ("2h 24m", "25:00"...).</summary>
    public static TextBlock Value(string text, double size = 15.5) => Text(text, size, FontWeights.SemiBold);

    public static TextBlock Icon(string glyph, double size = 14, string brush = "Text.Primary")
    {
        var t = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        return t.Fg(brush);
    }

    public static StackPanel H(params UIElement[] kids) => Stack(Orientation.Horizontal, kids);
    public static StackPanel V(params UIElement[] kids) => Stack(Orientation.Vertical, kids);

    static StackPanel Stack(Orientation o, UIElement[] kids)
    {
        var p = new StackPanel { Orientation = o, VerticalAlignment = VerticalAlignment.Center };
        foreach (var k in kids) p.Children.Add(k);
        return p;
    }

    public static Grid Columns(string spec, params UIElement[] kids)
    {
        var g = new Grid();
        foreach (var part in spec.Split(','))
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = ParseLength(part.Trim()) });
        for (int i = 0; i < kids.Length; i++) { Grid.SetColumn(kids[i], Math.Min(i, g.ColumnDefinitions.Count - 1)); g.Children.Add(kids[i]); }
        return g;
    }

    static GridLength ParseLength(string s) => s switch
    {
        "*" => new GridLength(1, GridUnitType.Star),
        "auto" or "Auto" => GridLength.Auto,
        _ when s.EndsWith('*') => new GridLength(double.Parse(s[..^1]), GridUnitType.Star),
        _ => new GridLength(double.Parse(s)),
    };

    static Style S(string key) => (Style)Application.Current.FindResource(key);

    public static Button CircleButton(string glyph, Action onClick, double size = 30, string bg = "Fill.Control", string fg = "Text.Primary", double glyphSize = 0)
    {
        var b = new Button { Style = S("Vela.CircleButton"), Width = size, Height = size, Content = Icon(glyph, glyphSize > 0 ? glyphSize : size * 0.4, fg) };
        b.SetResourceReference(Control.BackgroundProperty, bg);
        b.Click += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    public static Button PillButton(string text, Action onClick, bool accent = false)
    {
        var b = new Button { Style = S(accent ? "Vela.AccentButton" : "Vela.PillButton"), Content = text };
        b.Click += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    public static Button GhostButton(UIElement content, Action onClick)
    {
        var b = new Button { Style = S("Vela.GhostButton"), Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        b.Click += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    public static TextBox TextBox(string text = "", string hint = "")
        => new() { Style = S("Vela.TextBox"), Text = text, Tag = hint };

    public static PasswordBox PasswordBox() => new() { Style = S("Vela.PasswordBox") };

    public static Slider Slider(double value, Action<double> onChange)
    {
        var s = new Slider { Style = S("Vela.Slider"), Value = value };
        s.ValueChanged += (_, e) => onChange(e.NewValue);
        return s;
    }

    public static CheckBox Switch(bool on, Action<bool> onChange)
    {
        var c = new CheckBox { Style = S("Vela.Switch"), IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        c.Click += (_, _) => onChange(c.IsChecked == true);
        return c;
    }

    public static RadioButton Segment(string text, string group, bool isChecked, Action onPick)
    {
        var r = new RadioButton { Style = S("Vela.Segment"), Content = text, GroupName = group, IsChecked = isChecked };
        r.Checked += (_, _) => onPick();
        return r;
    }

    public static Border Divider(double margin = 10) => new Border { Height = 1, Margin = new Thickness(0, margin, 0, margin) }.Bg("Divider");

    /// <summary>Horizontal progress bar (iOS-style rounded track). Returns setter.</summary>
    public static FrameworkElement Progress(out Action<double> set, string fill = "Sys.Green", double height = 4)
    {
        var track = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), VerticalAlignment = VerticalAlignment.Center }.Bg("Fill.Track");
        var bar = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left }.Bg(fill);
        var g = new Grid { VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(track);
        g.Children.Add(bar);
        double value = 0;
        void Layout() => bar.Width = Math.Max(value > 0 ? height : 0, g.ActualWidth * value);
        g.SizeChanged += (_, _) => Layout();
        set = v => { value = Math.Clamp(v, 0, 1); Layout(); };
        return g;
    }

    /// <summary>Rounded square app-style badge with a glyph (e.g. the Notes / Screen Time icons).</summary>
    public static Border Badge(UIElement content, Brush? background, double size = 34, double radius = 9)
    {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), Background = background, Child = content, VerticalAlignment = VerticalAlignment.Center };
        return b;
    }

    public static Brush Brush(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }

    public static LinearGradientBrush Gradient(string top, string bottom)
    {
        var g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top), (Color)ColorConverter.ConvertFromString(bottom), 90);
        g.Freeze();
        return g;
    }

    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}

/// <summary>Circular gauge (CPU, RAM, todos progress...).</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public double Thickness { get; set; } = 4;

    public RingGauge(string fill = "Accent")
    {
        SetResourceReference(FillProperty, fill);
        SetResourceReference(TrackProperty, "Fill.Track");
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = (size - Thickness) / 2;
        dc.DrawEllipse(null, new Pen(Track, Thickness), c, r, r);
        double v = Math.Clamp(Value, 0, 1);
        if (v <= 0) return;
        var pen = new Pen(Fill, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (v >= 0.999) { dc.DrawEllipse(null, pen, c, r, r); return; }
        double a = v * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, v > 0.5, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}

/// <summary>Bars or line chart for small series (screen time hours, network, revenue).</summary>
public sealed class Sparkline : FrameworkElement
{
    IReadOnlyList<double> _values = Array.Empty<double>();
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public bool Bars { get; set; }
    /// <summary>Draw a faint dotted baseline under the bars (Screen Time tile).</summary>
    public bool Baseline { get; set; }
    public double? Max { get; set; }

    public Sparkline(string brush = "Accent") => SetResourceReference(StrokeProperty, brush);

    public void SetValues(IEnumerable<double> values) { _values = values.ToList(); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (_values.Count == 0 || w <= 0 || h <= 0) return;
        double max = Max ?? _values.Max();
        double min = Bars ? 0 : _values.Min();
        if (max - min < 1e-9) max = min + 1;
        if (Bars)
        {
            if (Baseline)
            {
                var pen = new Pen(Stroke, 0.6) { DashStyle = new DashStyle(new[] { 1.0, 2.0 }, 0) };
                dc.PushOpacity(0.55);
                dc.DrawLine(pen, new Point(0, h - 0.3), new Point(w, h - 0.3));
                dc.Pop();
            }
            double slot = w / _values.Count, bw = Math.Max(1.2, slot * 0.5);
            for (int i = 0; i < _values.Count; i++)
            {
                double bh = Math.Max(_values[i] > 0 ? 1.5 : 0.75, (_values[i] - min) / (max - min) * h);
                dc.DrawRoundedRectangle(Stroke, null, new Rect(i * slot + (slot - bw) / 2, h - bh, bw, bh), bw / 2, bw / 2);
            }
            return;
        }
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (int i = 0; i < _values.Count; i++)
            {
                var p = new Point(_values.Count == 1 ? w : i * w / (_values.Count - 1), h - 1 - (_values[i] - min) / (max - min) * (h - 2));
                if (i == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
            }
        }
        g.Freeze();
        dc.DrawGeometry(null, new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round }, g);
    }
}

/// <summary>Vector weather icons (WPF can't render colour emoji).</summary>
public sealed class WeatherIcon : FrameworkElement
{
    public WeatherKind Kind { get; set; }
    public bool Night { get; set; }

    static readonly Brush Sun = UI.Brush("#FFC93D"), MoonB = UI.Brush("#E8E6F0"), CloudB = UI.Brush("#F2F4F8"), CloudDark = UI.Brush("#AEB4C0"),
                          Rain = UI.Brush("#4DA3FF"), Snow = UI.Brush("#FFFFFF"), Bolt = UI.Brush("#FFD60A"), Fog = UI.Brush("#C7CCD6");

    public void Set(WeatherKind k, bool night) { Kind = k; Night = night; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - s) / 2, (ActualHeight - s) / 2));
        dc.PushTransform(new ScaleTransform(s / 24, s / 24));
        switch (Kind)
        {
            case WeatherKind.Clear: DrawSunOrMoon(dc, new Point(12, 12), 5.5); break;
            case WeatherKind.PartlyCloudy: DrawSunOrMoon(dc, new Point(9, 9), 4.5); DrawCloud(dc, CloudB, 3, 10); break;
            case WeatherKind.Cloudy: DrawCloud(dc, CloudDark, 5, 5.5, 0.85); DrawCloud(dc, CloudB, 2, 9); break;
            case WeatherKind.Fog:
                DrawCloud(dc, CloudDark, 2, 6);
                for (int i = 0; i < 2; i++) dc.DrawRoundedRectangle(Fog, null, new Rect(4, 18 + i * 3, 16, 1.6), 0.8, 0.8);
                break;
            case WeatherKind.Drizzle:
            case WeatherKind.Rain:
                DrawCloud(dc, CloudB, 2, 5);
                int drops = Kind == WeatherKind.Rain ? 3 : 2;
                for (int i = 0; i < drops; i++) dc.DrawLine(new Pen(Rain, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                    new Point(8 + i * 4, 18), new Point(7 + i * 4, 21.5));
                break;
            case WeatherKind.Snow:
                DrawCloud(dc, CloudB, 2, 5);
                for (int i = 0; i < 3; i++) dc.DrawEllipse(Snow, new Pen(CloudDark, 0.4), new Point(8 + i * 4, 19.5 + (i % 2)), 1.3, 1.3);
                break;
            case WeatherKind.Storm:
                DrawCloud(dc, CloudDark, 2, 5);
                var bolt = Geometry.Parse("M12,15 L9,20 L11.5,20 L10,24 L15,18 L12.5,18 L14,15 Z");
                dc.DrawGeometry(Bolt, null, bolt);
                break;
        }
        dc.Pop(); dc.Pop();
    }

    void DrawSunOrMoon(DrawingContext dc, Point c, double r)
    {
        if (Night)
        {
            var moon = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(c, r, r), new EllipseGeometry(new Point(c.X + r * 0.6, c.Y - r * 0.5), r * 0.85, r * 0.85));
            dc.DrawGeometry(MoonB, null, moon);
            return;
        }
        dc.DrawEllipse(Sun, null, c, r, r);
        var pen = new Pen(Sun, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            dc.DrawLine(pen, new Point(c.X + Math.Cos(a) * (r + 2), c.Y + Math.Sin(a) * (r + 2)), new Point(c.X + Math.Cos(a) * (r + 3.8), c.Y + Math.Sin(a) * (r + 3.8)));
        }
    }

    static void DrawCloud(DrawingContext dc, Brush b, double x, double y, double scale = 1)
    {
        dc.PushTransform(new TranslateTransform(x, y));
        dc.PushTransform(new ScaleTransform(scale, scale));
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        g.Children.Add(new EllipseGeometry(new Point(7, 8), 4.5, 4.5));
        g.Children.Add(new EllipseGeometry(new Point(12, 6), 5.5, 5.5));
        g.Children.Add(new RectangleGeometry(new Rect(3, 8, 15, 5), 2.5, 2.5));
        g.Children.Add(new EllipseGeometry(new Point(16, 9.5), 3.5, 3.5));
        dc.DrawGeometry(b, null, g);
        dc.Pop(); dc.Pop();
    }
}

/// <summary>Windows logo (four tiles) for the Start button.</summary>
public sealed class WindowsLogo : FrameworkElement
{
    /// <summary>Windows 11 blue (light top-left to deeper bottom-right) instead of the theme text colour.</summary>
    public bool Colored { get; set; }
    static readonly Brush Blue = MakeBlue();
    static Brush MakeBlue()
    {
        var g = new LinearGradientBrush(Color.FromRgb(0x5C, 0xC8, 0xFF), Color.FromRgb(0x00, 0x6C, 0xD8), new Point(0, 0), new Point(1, 1));
        g.Freeze();
        return g;
    }

    /// <summary>Explicit fill (overrides <see cref="Colored"/> and the theme text colour).</summary>
    public Brush? Fill { get; set; }

    static Brush Frozen(Brush b) { b.Freeze(); return b; }
    static readonly Brush TileFill = Frozen(new LinearGradientBrush(Color.FromRgb(0x3E, 0x3E, 0x40), Color.FromRgb(0x24, 0x24, 0x26), 90));
    static readonly Brush TileEdge = Frozen(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));
    static readonly Brush MarkFill = Frozen(new LinearGradientBrush(Color.FromRgb(0xF7, 0xF7, 0xF7), Color.FromRgb(0xC4, 0xC4, 0xC6), 90));

    /// <summary>Start button as in the reference: a light four-pane mark on a dark rounded tile, in every style.</summary>
    public static FrameworkElement StartTile(double size) => new Border
    {
        Width = size, Height = size,
        CornerRadius = new CornerRadius(size * 0.24),
        Background = TileFill, BorderBrush = TileEdge, BorderThickness = new Thickness(0.75),
        Child = new WindowsLogo { Fill = MarkFill, Width = size * 0.5, Height = size * 0.5 },
    };

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight), gap = s * 0.07, q = (s - gap) / 2;
        double x0 = (ActualWidth - s) / 2, y0 = (ActualHeight - s) / 2;
        var b = Fill ?? (Colored ? Blue : UI.Res("Text.Primary"));
        for (int i = 0; i < 4; i++)
            dc.DrawRoundedRectangle(b, null, new Rect(x0 + (i % 2) * (q + gap), y0 + (i / 2) * (q + gap), q, q), q * 0.12, q * 0.12);
    }
}

/// <summary>Lightning bolt (charging) drawn as a vector, filled white.</summary>
public sealed class BoltMark : FrameworkElement
{
    static readonly Geometry Shape = Freeze(Geometry.Parse("M7,0 L0,13 L5.5,13 L4,24 L12,9 L6.5,9 L8.5,0 Z"));
    static Geometry Freeze(Geometry g) { g.Freeze(); return g; }

    protected override void OnRender(DrawingContext dc)
    {
        double sx = ActualWidth / 12, sy = ActualHeight / 24, s = Math.Min(sx, sy);
        if (s <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - 12 * s) / 2, (ActualHeight - 24 * s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawGeometry(Brushes.White, null, Shape);
        dc.Pop(); dc.Pop();
    }
}

/// <summary>Smart-watch mark (Screen Time), drawn in the accent colour: strap, case, face and hands.</summary>
public sealed class WatchIcon : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(WatchIcon),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public WatchIcon(string brush = "Accent") => SetResourceReference(FillProperty, brush);

    protected override void OnRender(DrawingContext dc)
    {
        // Designed on a 14 × 24 grid.
        double s = Math.Min(ActualWidth / 14, ActualHeight / 24);
        if (s <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - 14 * s) / 2, (ActualHeight - 24 * s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        dc.PushOpacity(0.75);
        dc.DrawRoundedRectangle(Fill, null, new Rect(3, 0, 8, 24), 2.5, 2.5);   // strap
        dc.Pop();
        dc.DrawRoundedRectangle(Fill, null, new Rect(0.5, 4.5, 13, 15), 4, 4); // case
        dc.DrawRoundedRectangle(Fill, null, new Rect(13, 9, 1.4, 3.5), 0.7, 0.7); // crown
        var face = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0));
        dc.DrawRoundedRectangle(face, null, new Rect(2, 6, 10, 12), 3, 3);
        var hand = new Pen(Brushes.White, 1.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(hand, new Point(7, 12), new Point(7, 8.8));
        dc.DrawLine(hand, new Point(7, 12), new Point(9.2, 13.2));
        dc.Pop(); dc.Pop();
    }
}
