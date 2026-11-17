using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Vela.Core;
using Vela.Theming;
using Vela.Widgets;
using static Vela.UI.UI;

namespace Vela.Views;

/// <summary>
/// Launch posters from the real widgets (no screenshots): `Vela.exe --render &lt;dir&gt; --poster`. Three 16:9 images —
/// hero, widget wall, cards — rendered off-screen like --render. Only widgets without personal data are used.
/// </summary>
public static class PosterRenderer
{
    const double W = 1600, H = 900;
    static readonly List<WidgetBase> Live = new();

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        Settings.Load();
        Settings.Current.Theme = "glass";
        Settings.Current.Use24Hour = false;
        ThemeManager.Apply();

        var posters = new (string file, FrameworkElement view)[]
        {
            ("vela-poster-1-hero.png", Hero()),
            ("vela-poster-2-widgets.png", WidgetWall()),
            ("vela-poster-3-cards.png", Cards()),
        };
        foreach (var w in Live) w.AttachForRender();
        RenderCatalog.Pump(TimeSpan.FromSeconds(3));
        foreach (var (file, view) in posters) RenderCatalog.Save(view, System.IO.Path.Combine(dir, file), 1.5);
        Log.Info($"Rendered {posters.Length} posters to {dir}");
    }

    // ------------------------------------------------------------------ building blocks

    static WidgetBase Widget(string type)
    {
        var w = WidgetRegistry.Create(new WidgetInstance { Type = type }, preview: true)!;
        Live.Add(w);
        return w;
    }

    /// <summary>A dock bar with these widgets, the way the dock draws them.</summary>
    static Border Dock(params string[] types)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var t in types) { var w = Widget(t); w.Margin = new Thickness(3, 0, 3, 0); row.Children.Add(w); }
        var bar = RenderCatalog.Bar(row);
        bar.Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 18, Direction = 270, Opacity = 0.55, Color = Colors.Black };
        return bar;
    }

    static Border Card(UIElement content, double width = 340)
    {
        var c = new Border { Width = width, CornerRadius = new CornerRadius(22), Padding = new Thickness(18), Child = content, VerticalAlignment = VerticalAlignment.Top };
        c.Bg("Flyout.Background");
        c.SetResourceReference(Border.BorderBrushProperty, "Flyout.Border");
        c.BorderThickness = new Thickness(1);
        c.Effect = new DropShadowEffect { BlurRadius = 50, ShadowDepth = 14, Direction = 270, Opacity = 0.5, Color = Colors.Black };
        return c;
    }

    static UIElement CardOf(string type, double width = 340) =>
        Card(Widget(type).CardForRender() ?? new TextBlock(), width);

    /// <summary>Deep-water gradient with soft colour glows (an original backdrop, not a wallpaper).</summary>
    static Grid Backdrop(params (double x, double y, double r, string color, double alpha)[] glows)
    {
        var g = new Grid { Width = W, Height = H, ClipToBounds = true };
        g.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Color.FromRgb(0x06, 0x0B, 0x14), Color.FromRgb(0x0B, 0x1E, 0x2B), 65) });
        foreach (var (x, y, r, color, alpha) in glows)
        {
            var c = (Color)ColorConverter.ConvertFromString(color);
            var brush = new RadialGradientBrush(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
            g.Children.Add(new Ellipse { Width = r * 2, Height = r * 2, Fill = brush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x - r, y - r, 0, 0) });
        }
        // A faint horizon line of light, like glass catching it.
        g.Children.Add(new Rectangle
        {
            Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 230), Opacity = 0.18,
            Fill = new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, 0), new(Colors.White, 0.5), new(Colors.Transparent, 1) }, 0),
        });
        return g;
    }

    static TextBlock T(string text, double size, FontWeight weight, double opacity = 1, string brush = "Text.Primary")
    {
        var t = Text(text, size, weight, brush);
        if (brush == "Text.Primary") t.Foreground = Brushes.White; // posters are always dark; no theme lookup
        // Display-mode glyph caching drops mid-size headlines (~90 px) in RenderTargetBitmap; Ideal draws them reliably.
        TextOptions.SetTextFormattingMode(t, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(t, TextRenderingMode.Grayscale);
        t.Opacity = opacity;
        t.TextTrimming = TextTrimming.None;
        return t;
    }

    static FrameworkElement Chip(string text, string? glyph = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph != null) row.Children.Add(Icon(glyph, 14, "Accent").Margin(0, 0, 8, 0));
        row.Children.Add(T(text, 15, FontWeights.SemiBold, 0.92));
        var b = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 9, 18, 9), Margin = new Thickness(0, 0, 10, 0), Child = row, BorderThickness = new Thickness(1) };
        b.Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));
        b.BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255));
        return b;
    }

    static FrameworkElement Eyebrow(string text)
    {
        var t = T(text, 14, FontWeights.Bold, 1, "Accent");
        return t.Margin(2, 0, 0, 12);
    }

    // ------------------------------------------------------------------ 1. hero

    static FrameworkElement Hero()
    {
        var g = Backdrop((1250, 120, 520, "#2B6CB0", 0.42), (300, 820, 560, "#0E7C86", 0.35), (900, 520, 380, "#6B46C1", 0.18));

        var head = new StackPanel { Margin = new Thickness(110, 96, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        head.Children.Add(Eyebrow("INTRODUCING"));
        head.Children.Add(T("Vela", 128, FontWeights.Bold).Margin(-4, -18, 0, -6));
        head.Children.Add(T("Your Windows taskbar, reimagined.", 38, FontWeights.SemiBold, 0.95));
        head.Children.Add(T("Apps and 100+ live widgets in one glassy dock — native, light and open source.", 21, FontWeights.Normal, 0.62).Margin(0, 12, 0, 0));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 30, 0, 0) };
        chips.Children.Add(Chip("Free & open source"));
        chips.Children.Add(Chip("Native C# / WPF"));
        chips.Children.Add(Chip("~0.5% idle CPU"));
        head.Children.Add(chips);
        g.Children.Add(head);

        // Floating card, as if just opened from the dock.
        var card = CardOf("terminal", 320);
        ((FrameworkElement)card).HorizontalAlignment = HorizontalAlignment.Right;
        ((FrameworkElement)card).Margin = new Thickness(0, 120, 110, 0);
        ((FrameworkElement)card).LayoutTransform = new ScaleTransform(1.05, 1.05);
        g.Children.Add(card);

        var dock = Dock("battery", "nowplaying.mini", "volume", "quicktoggles", "apps", "claude", "clock");
        dock.HorizontalAlignment = HorizontalAlignment.Center;
        dock.VerticalAlignment = VerticalAlignment.Bottom;
        dock.Margin = new Thickness(0, 0, 0, 92);
        dock.LayoutTransform = new ScaleTransform(1.32, 1.32);
        g.Children.Add(dock);

        g.Children.Add(Footer());
        return g;
    }

    static FrameworkElement Footer()
    {
        var f = T("github.com/sherifrahim/Vela", 16, FontWeights.SemiBold, 0.55);
        f.HorizontalAlignment = HorizontalAlignment.Center;
        f.VerticalAlignment = VerticalAlignment.Bottom;
        f.Margin = new Thickness(0, 0, 0, 30);
        return f;
    }

    // ------------------------------------------------------------------ 2. widget wall

    static FrameworkElement WidgetWall()
    {
        var g = Backdrop((200, 100, 520, "#0E7C86", 0.38), (1400, 760, 600, "#2B6CB0", 0.4), (1300, 80, 300, "#C05621", 0.14));
        var head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 70, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var eyebrow = Eyebrow("WIDGETS");
        eyebrow.HorizontalAlignment = HorizontalAlignment.Center;
        head.Children.Add(eyebrow);
        var title = T("100+ live widgets", 64, FontWeights.Bold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        head.Children.Add(title);
        var sub = T("Clocks, focus timers, music, system, weather, AI limits, revenue, calendar and more.", 20, FontWeights.Normal, 0.62);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        head.Children.Add(sub.Margin(0, 8, 0, 0));
        g.Children.Add(head);

        string[][] rows =
        {
            new[] { "clock", "clock.flip", "pomodoro", "timer", "battery", "quicktoggles" },
            new[] { "weather.forecast", "nowplaying", "screentime", "clock.week", "crypto" },
            new[] { "claude", "codex", "stripe.chart", "github.activity", "battery.dots" },
            new[] { "volume", "notes", "todos", "currency", "sysstats", "clock.analog", "brightness" },
        };
        var wall = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 190, 0, 0) };
        foreach (var r in rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) };
            foreach (var t in r)
            {
                var bar = Dock(t);
                bar.Margin = new Thickness(9, 0, 9, 0);
                line.Children.Add(bar);
            }
            wall.Children.Add(line);
        }
        wall.LayoutTransform = new ScaleTransform(1.18, 1.18);
        g.Children.Add(wall);
        g.Children.Add(Footer());
        return g;
    }

    // ------------------------------------------------------------------ 3. cards

    static FrameworkElement Cards()
    {
        var g = Backdrop((800, 860, 700, "#2B6CB0", 0.42), (120, 120, 420, "#6B46C1", 0.2), (1500, 160, 420, "#0E7C86", 0.3));
        var head = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 70, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var eyebrow = Eyebrow("ONE CLICK AWAY");
        eyebrow.HorizontalAlignment = HorizontalAlignment.Center;
        head.Children.Add(eyebrow);
        var title = T("Every tile opens a card", 60, FontWeights.Bold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        head.Children.Add(title);
        var sub = T("Calendar, currency, quick commands, terminals — without leaving what you're doing.", 20, FontWeights.Normal, 0.62);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        head.Children.Add(sub.Margin(0, 8, 0, 0));
        g.Children.Add(head);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 250, 0, 0) };
        foreach (var (type, width, dy) in new[] { ("calendar", 320.0, 26.0), ("currency", 360.0, 0.0), ("commands", 340.0, 40.0) })
        {
            var c = (FrameworkElement)CardOf(type, width);
            c.Margin = new Thickness(18, dy, 18, 0);
            row.Children.Add(c);
        }
        g.Children.Add(row);

        var dock = Dock("calendar", "currency", "codex", "commands", "terminal", "clock");
        dock.HorizontalAlignment = HorizontalAlignment.Center;
        dock.VerticalAlignment = VerticalAlignment.Bottom;
        dock.Margin = new Thickness(0, 0, 0, 80);
        dock.LayoutTransform = new ScaleTransform(1.2, 1.2);
        g.Children.Add(dock);
        g.Children.Add(Footer());
        return g;
    }
}
