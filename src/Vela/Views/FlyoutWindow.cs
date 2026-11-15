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
        Current?.CloseNow();
        var f = new FlyoutWindow(anchor, content, width);
        Current = f;
        f.Show();
        f.Activate();
    }

    public static void CloseCurrent() => Current?.CloseAnimated();

    FlyoutWindow(FrameworkElement anchor, UIElement content, double width)
    {
        Anchor = anchor;
        Topmost = true;
        ShowActivated = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Vela card";
        RegionRadius = 20;

        _root = new Border
        {
            Width = width,
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18),
            Child = content,
            RenderTransform = _slide,
            Opacity = 0,
        };
        _root.SetResourceReference(Border.BackgroundProperty, "Flyout.Background");
        _root.SetResourceReference(Border.BorderBrushProperty, "Flyout.Border");
        RegionElement = _root;
        Content = _root;

        Deactivated += (_, _) => CloseAnimated();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) CloseAnimated(); };
        SizeChanged += (_, _) => Position();
        Loaded += (_, _) => { Position(); Animate(true); };
        Closed += (_, _) => { if (Current == this) Current = null; };
    }

    bool DockAtTop => Settings.Current.Edge == "top";

    void Position()
    {
        if (Hwnd == IntPtr.Zero || !Anchor.IsLoaded || PresentationSource.FromVisual(Anchor) == null) return;
        var tl = Anchor.PointToScreen(new Point(0, 0));
        var br = Anchor.PointToScreen(new Point(Anchor.ActualWidth, Anchor.ActualHeight));
        double s = DpiScale;
        int w = (int)Math.Ceiling(ActualWidth * s), h = (int)Math.Ceiling(ActualHeight * s);
        int gap = (int)(10 * s);

        var mi = GetPrimaryMonitor(out _);
        var area = mi.rcMonitor;
        int x = (int)((tl.X + br.X) / 2 - w / 2.0);
        x = Math.Clamp(x, area.Left + gap, Math.Max(area.Left + gap, area.Right - gap - w));
        int y = DockAtTop ? (int)br.Y + gap : (int)tl.Y - gap - h;
        SetWindowPos(Hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE);
    }

    void Animate(bool open)
    {
        double from = DockAtTop ? -14 : 14;
        var dur = TimeSpan.FromMilliseconds(open ? 190 : 120);
        var ease = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };
        _root.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 0 : 1, open ? 1 : 0, dur) { EasingFunction = ease });
        var slide = new DoubleAnimation(open ? from : 0, open ? 0 : from, dur) { EasingFunction = ease };
        if (!open) slide.Completed += (_, _) => CloseNow();
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
