using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Vela.Core;
using Vela.UI;
using static Vela.Interop.Native;

namespace Vela.Views;

/// <summary>
/// A widget's "card": opens over its widget and slides out of the bar (up when the dock is at the
/// bottom, down when it's at the top). One card at a time; clicking the same widget again closes it;
/// clicking anywhere outside closes it.
/// </summary>
public sealed class FlyoutWindow : GlassWindow
{
    public static FlyoutWindow? Current { get; private set; }
    public FrameworkElement Anchor { get; }
    readonly Border _root;
    readonly TranslateTransform _slide = new();
    bool _closing;

    public static void Toggle(FrameworkElement anchor, UIElement content, double width = 340)
    {
        if (Current != null && Current.Anchor == anchor) { Current.CloseAnimated(); return; }
        WindowPreview.CloseAll();
        Current?.CloseNow();
        var f = new FlyoutWindow(anchor, content, width);
        Current = f;
        f.Show();
        f.Activate();
    }

    public static void CloseCurrent() => Current?.CloseAnimated();

    /// <summary>Swap the open card's content in place (e.g. Control Centre → Wi-Fi list); it re-anchors itself.</summary>
    public static void Navigate(UIElement content)
    {
        if (Current == null) return;
        Current._root.Child = content;
    }

    /// <summary>Open a card for <paramref name="anchor"/> even if another card is open (no toggle-close).</summary>
    public static void Show(FrameworkElement anchor, UIElement content, double width = 340)
    {
        if (Current != null && Current.Anchor == anchor) { Navigate(content); return; }
        Toggle(anchor, content, width);
    }

    FlyoutWindow(FrameworkElement anchor, UIElement content, double width)
    {
        Anchor = anchor;
        Topmost = true;
        ShowActivated = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -20000; Top = -20000; // placed over the anchor once laid out (Position)
        Title = "Vela card";
        RegionRadius = 22;

        _root = new Border
        {
            Width = width,
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(18),
            Child = content,
            RenderTransform = _slide,
            Opacity = 0,
        };
        _root.SetResourceReference(Border.BackgroundProperty, "Flyout.Background");
        _root.SetResourceReference(Border.BorderBrushProperty, "Flyout.Border");
        _root.SetResourceReference(Border.BorderThicknessProperty, "Hairline");
        RegionElement = _root;
        LiveBackdrop = true; // frosted, macOS-style card over a live blur
        Content = _root;
        // Cards follow the light/dark look their dock adapted to.
        if (DockWindow.For(anchor)?.AdaptiveResources is { } rd) Resources.MergedDictionaries.Add(rd);

        // Close when another app takes focus. If one of Vela's own blur backdrops grabbed it (they sit right
        // under the card), take it straight back instead of closing.
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && GetWindowTitle(fg) == "Vela Backdrop") { Activate(); return; }
            CloseAnimated();
        }, System.Windows.Threading.DispatcherPriority.Input);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) CloseAnimated(); };
        SizeChanged += (_, _) => Position();
        Loaded += (_, _) => { Position(); Animate(true); };
        Closed += (_, _) => { if (Current == this) Current = null; TrimSoon(); };
    }

    static System.Windows.Threading.DispatcherTimer? _trim;

    /// <summary>Give memory back a few seconds after the last card closes (cards pull in WinRT, images…).</summary>
    static void TrimSoon()
    {
        _trim ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _trim.Stop();
        _trim.Tick -= OnTrim;
        _trim.Tick += OnTrim;
        _trim.Start();
    }

    static void OnTrim(object? s, EventArgs e)
    {
        _trim?.Stop();
        if (Current != null) return;
        GC.Collect(2, GCCollectionMode.Optimized, false);
        Shell.WinActions.TrimWorkingSet();
    }

    bool DockAtTop => Settings.Current.Edge == "top";

    /// <summary>
    /// Centre the card over its widget, just above the bar (below it when docked at the top), clamped to
    /// the screen. Positioned through WPF's Left/Top (DIPs): a raw SetWindowPos gets overwritten by WPF's
    /// own size-to-content pass, which is what used to drop cards in the middle of the screen.
    /// </summary>
    void Position()
    {
        if (!Anchor.IsLoaded || PresentationSource.FromVisual(Anchor) == null || ActualWidth <= 0) return;
        var tl = Anchor.PointToScreen(new Point(0, 0));                                    // physical px
        var br = Anchor.PointToScreen(new Point(Anchor.ActualWidth, Anchor.ActualHeight));
        double s = DpiScale, w = ActualWidth, h = ActualHeight, gap = 8;
        var dockWin = DockWindow.For(Anchor);
        var area = (dockWin?.MonitorInfo(out _) ?? GetPrimaryMonitor(out _)).rcMonitor;
        double left = area.Left / s + gap, right = area.Right / s - gap, top = area.Top / s + gap, bottom = area.Bottom / s - gap;

        // Anchor to the bar's edge, not the tile's, so every card sits at the same height.
        double barTop = tl.Y / s, barBottom = br.Y / s;
        if (dockWin is { } dock && PresentationSource.FromVisual(dock.BarElement) != null)
        {
            barTop = dock.BarElement.PointToScreen(new Point(0, 0)).Y / s;
            barBottom = dock.BarElement.PointToScreen(new Point(0, dock.BarElement.ActualHeight)).Y / s;
        }
        double x = Math.Clamp((tl.X + br.X) / 2 / s - w / 2, left, Math.Max(left, right - w));
        double y = DockAtTop ? barBottom + gap : barTop - gap - h;
        y = Math.Clamp(y, top, Math.Max(top, bottom - h));
        if (Math.Abs(Left - x) > 0.5) Left = x;
        if (Math.Abs(Top - y) > 0.5) Top = y;
    }

    void Animate(bool open)
    {
        double from = DockAtTop ? -14 : 14;
        var dur = TimeSpan.FromMilliseconds(open ? 190 : 120);
        var ease = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };
        _root.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 0 : 1, open ? 1 : 0, dur) { EasingFunction = ease });
        var slide = new DoubleAnimation(open ? from : 0, open ? 0 : from, dur) { EasingFunction = ease };
        if (!open) { HideLiveBackdrop(); slide.Completed += (_, _) => CloseNow(); }
        else slide.Completed += (_, _) => Dispatcher.BeginInvoke(SyncLiveBackdrop); // after the render pass: blur follows once the slide settles
        _slide.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        if (Current == this) Current = null;
        Animate(false);
    }

    public void CloseNow()
    {
        _closing = true;
        if (Current == this) Current = null;
        try { Close(); } catch (InvalidOperationException) { }
    }
}

/// <summary>Shared building blocks for card content, so every card looks the same.</summary>
public static class Card
{
    /// <summary>Card header: optional icon badge, title, optional pill, close button.</summary>
    public static FrameworkElement Header(string title, UIElement? icon = null, string? pill = null)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null) { left.Children.Add(icon); ((FrameworkElement)icon).Margin = new Thickness(0, 0, 12, 0); }
        left.Children.Add(UI.UI.Text(title, 16, FontWeights.Bold));
        if (pill != null)
        {
            var p = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = UI.UI.Text(pill, 11.5, FontWeights.SemiBold, "Text.Secondary") };
            p.SetResourceReference(Border.BackgroundProperty, "Fill.Control");
            left.Children.Add(p);
        }
        row.Children.Add(left);
        var close = UI.UI.CircleButton(UI.Glyphs.Close, CloseCurrentCard, 28, "Fill.Control", "Text.Secondary", 10);
        Grid.SetColumn(close, 2);
        row.Children.Add(close);
        return row;
    }

    static void CloseCurrentCard() => FlyoutWindow.CloseCurrent();

    /// <summary>Usage row like the reference AI cards: label, "resets 2h 8m", percent, bar.</summary>
    public static FrameworkElement UsageRow(string label, double percent, string resets)
    {
        var top = UI.UI.Columns("auto,*,auto",
            UI.UI.Text(label, 14, FontWeights.SemiBold),
            UI.UI.Text(resets, 12, null, "Text.Secondary").Right().Margin(0, 0, 14, 0),
            UI.UI.Text($"{percent:0}%", 14, FontWeights.Bold));
        var bar = UI.UI.Progress(out var set, percent >= 90 ? "Sys.Red" : percent >= 70 ? "Sys.Orange" : "Sys.Green", 6);
        bar.Margin = new Thickness(0, 8, 0, 0);
        bar.Loaded += (_, _) => set(percent / 100);
        return UI.UI.V(top, bar).Margin(0, 0, 0, 16);
    }

    public static FrameworkElement Footer(string note, string button, Action onClick)
    {
        var g = UI.UI.Columns("*,auto",
            UI.UI.Text(note, 12, null, "Text.Secondary"),
            UI.UI.PillButton(button, onClick));
        g.Margin = new Thickness(0, 8, 0, 0);
        return g;
    }

    /// <summary>Paste-a-key form for ApiKey connectors.</summary>
    public static FrameworkElement ConnectForm(Connectors.IConnector c, Action? onSaved = null)
    {
        var box = UI.UI.PasswordBox();
        var status = UI.UI.Text(c.Error ?? "", 12, null, "Sys.Red");
        status.TextWrapping = TextWrapping.Wrap;
        var save = UI.UI.PillButton("Connect", () =>
        {
            Secrets.Set(c.SecretName!, box.Password);
            box.Clear();
            onSaved?.Invoke();
        }, accent: true);
        var help = UI.UI.GhostButton(UI.UI.Text("How to get a key ↗", 12, FontWeights.SemiBold, "Accent"), () => { if (c.HelpUrl != null) Shell.WindowTracker.OpenUri(c.HelpUrl); });
        help.HorizontalAlignment = HorizontalAlignment.Left;
        help.Padding = new Thickness(0, 4, 4, 4);
        var desc = UI.UI.Text(c.Description, 12.5, null, "Text.Secondary");
        desc.TextWrapping = TextWrapping.Wrap;
        var keyLabel = UI.UI.Text($"Key ({c.KeyHint})", 12, FontWeights.SemiBold, "Text.Secondary").Margin(0, 12, 0, 6);
        var row = UI.UI.Columns("*,auto", box, save.Margin(8, 0, 0, 0));
        var note = UI.UI.Text("Stored encrypted on this PC (Windows DPAPI). Sent only to " + c.Name + ".", 11, null, "Text.Tertiary").Margin(0, 10, 0, 0);
        note.TextWrapping = TextWrapping.Wrap;
        return UI.UI.V(desc, keyLabel, row, status, help, note);
    }
}
