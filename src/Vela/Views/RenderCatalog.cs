using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vela.Core;
using Vela.Theming;
using Vela.Widgets;
using static Vela.UI.UI;

namespace Vela.Views;

/// <summary>
/// <c>Vela.exe --render &lt;folder&gt; [--theme id]</c>: draws the dock layout and every widget off-screen to PNGs
/// (2× scale) and exits — no window, no taskbar change, no single-instance lock. For visual checks by agents
/// and for comparing against the reference renders without touching the owner's screen.
/// </summary>
public static class RenderCatalog
{
    public static void Run(string dir, string? theme)
    {
        Directory.CreateDirectory(dir);
        Settings.Load();
        if (theme != null) Settings.Current.Theme = theme;
        ThemeManager.Apply();

        // 1) The current dock layout on a dark "wallpaper".
        var dockItems = Settings.Current.Widgets.Select(i => WidgetRegistry.Create(i, preview: true)).Where(w => w != null).Cast<WidgetBase>().ToList();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var w in dockItems) { w.Margin = new Thickness(3, 0, 3, 0); row.Children.Add(w); }
        var dock = Bar(row);

        // 2) Every widget, labelled, in a grid of bars.
        var all = WidgetRegistry.All.Where(d => !d.Hidden && d.Type is not "apps").ToList();
        var grid = new WrapPanel { Width = 1700 };
        var catalog = new List<WidgetBase>();
        foreach (var d in all)
        {
            var w = WidgetRegistry.Create(new WidgetInstance { Type = d.Type }, preview: true);
            if (w == null) continue;
            catalog.Add(w);
            var cell = V(Text(d.Name + "  ·  " + d.Type, 9, FontWeights.SemiBold, "Text.Secondary").Margin(4, 0, 0, 4), Bar(w));
            cell.Margin = new Thickness(8);
            grid.Children.Add(cell);
        }

        foreach (var w in dockItems.Concat(catalog)) w.AttachForRender();
        Pump(TimeSpan.FromSeconds(3)); // let async services (media, weather, connectors) report once

        Save(Backdrop(dock), Path.Combine(dir, "dock.png"));
        Save(Backdrop(grid), Path.Combine(dir, "catalog.png"));
        // 3) A few cards (solid, as they open over the dock).
        var cards = new WrapPanel { Width = 1500 };
        foreach (var type in new[] { "claude", "codex", "terminal", "volume", "linear", "revenue.total" })
        {
            var w = catalog.FirstOrDefault(c => c.Instance?.Type == type);
            if (w?.CardForRender() is not { } content) continue;
            var card = new Border { Width = 340, CornerRadius = new CornerRadius(22), Padding = new Thickness(18), Margin = new Thickness(10), Child = content, VerticalAlignment = VerticalAlignment.Top };
            card.Bg("Flyout.Background");
            card.SetResourceReference(Border.BorderBrushProperty, "Flyout.Border");
            card.SetResourceReference(Border.BorderThicknessProperty, "Hairline");
            cards.Children.Add(card);
        }
        Pump(TimeSpan.FromSeconds(1));
        Save(Backdrop(cards), Path.Combine(dir, "cards.png"));
        var island = new StackPanel();
        foreach (var (name, view, big) in IslandWindow.Previews())
            island.Children.Add(V(Text(name, 9, FontWeights.SemiBold, "Text.Secondary").Margin(4, 0, 0, 4), IslandWindow.PreviewPill(view, big)).Margin(0, 0, 0, 14));
        Save(Backdrop(island), Path.Combine(dir, "island.png"));
        Log.Info($"Rendered dock + {catalog.Count} widgets to {dir}");
    }

    static Border Bar(UIElement content)
    {
        var tint = new Border { CornerRadius = new CornerRadius(16) }.Bg("Bar.Background");
        var shade = new Border { CornerRadius = new CornerRadius(16) }.Bg("Bar.Shade");
        var g = new Grid();
        g.Children.Add(tint);
        g.Children.Add(shade);
        g.Children.Add(new Border { Padding = new Thickness(3), Child = content });
        var b = new Border { CornerRadius = new CornerRadius(16), Child = g, HorizontalAlignment = HorizontalAlignment.Left };
        b.SetResourceReference(Border.BorderBrushProperty, "Bar.Border");
        b.SetResourceReference(Border.BorderThicknessProperty, "Hairline");
        return b;
    }

    static Border Backdrop(UIElement content) => new()
    {
        Padding = new Thickness(24),
        Background = new LinearGradientBrush(Color.FromRgb(0x24, 0x3B, 0x4A), Color.FromRgb(0x0C, 0x12, 0x1A), 60),
        Child = content,
    };

    static void Pump(TimeSpan t)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = t };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    static void Save(FrameworkElement e, string path)
    {
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        e.Arrange(new Rect(e.DesiredSize));
        e.UpdateLayout();
        const double scale = 2;
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(e.ActualWidth * scale), (int)Math.Ceiling(e.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
