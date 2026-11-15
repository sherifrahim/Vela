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

static class Art
{
    /// <summary>Album art in a rounded square (or circle), with a music-note placeholder.</summary>
    public static (Border frame, Action<ImageSource?> set) Make(double size, bool round)
    {
        var img = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var placeholder = Icon("", size * 0.42, "Text.Secondary"); // music note
        var g = new Grid();
        g.Children.Add(placeholder);
        g.Children.Add(img);
        var frame = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(round ? size / 2 : size * 0.22), Child = g, ClipToBounds = true };
        frame.Bg("Fill.Control");
        // Border.ClipToBounds doesn't clip to the corner radius; clip explicitly.
        frame.SizeChanged += (_, _) => frame.Clip = new RectangleGeometry(new Rect(0, 0, size, size), frame.CornerRadius.TopLeft, frame.CornerRadius.TopLeft);
        return (frame, src => { img.Source = src; placeholder.Visibility = src == null ? Visibility.Visible : Visibility.Collapsed; });
    }
}

/// <summary>Cover art, title, artist and controls for whatever is playing.</summary>
public sealed class NowPlayingWidget : WidgetBase
{
    TextBlock _title = null!, _artist = null!;
    Action<ImageSource?> _setArt = null!;
    Button _play = null!;

    protected override UIElement BuildContent()
    {
        var (frame, set) = Art.Make(34, false);
        _setArt = set;
        _title = Text("", 12.5, FontWeights.Bold);
        _title.MaxWidth = 140;
        _artist = Text("", 11, null, "Text.Secondary");
        _artist.MaxWidth = 140;
        _play = CircleButton(Glyphs.Play, () => Svc.Media.TogglePlayPause(), 30, "Fill.Control", "Text.Primary", 11);
        var buttons = H(
            CircleButton(Glyphs.Previous, () => Svc.Media.Previous(), 26, "Transparent", "Text.Primary", 10),
            _play.Margin(2, 0),
            CircleButton(Glyphs.Next, () => Svc.Media.Next(), 26, "Transparent", "Text.Primary", 10));
        buttons.Margin = new Thickness(12, 0, 0, 0);
        return H(frame, V(_title, _artist).Margin(10, 0, 0, 0), buttons);
    }

    protected override void OnAttach() { Svc.Media.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Media.Changed -= Render;

    void Render()
    {
        var m = Svc.Media;
        _title.Text = m.HasSession ? m.Title : "Not playing";
        _artist.Text = m.HasSession ? m.Artist : "Play something";
        _setArt(m.Art);
        ((TextBlock)_play.Content).Text = m.IsPlaying ? Glyphs.Pause : Glyphs.Play;
    }

    protected override UIElement BuildCard() => MediaCard.Build();
}

/// <summary>Round cover with a play/pause button over it.</summary>
public sealed class NowPlayingMiniWidget : WidgetBase
{
    Action<ImageSource?> _setArt = null!;
    TextBlock _glyph = null!;
    Border _overlay = null!;
    protected override bool HasTile => false;

    protected override UIElement BuildContent()
    {
        var (frame, set) = Art.Make(42, true);
        _setArt = set;
        _glyph = Icon(Glyphs.Play, 14, "OnAccent");
        _overlay = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(21), Background = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), Child = _glyph, Opacity = 0 };
        var g = new Grid();
        g.Children.Add(frame);
        g.Children.Add(_overlay);
        MouseEnter += (_, _) => _overlay.Opacity = 1;
        MouseLeave += (_, _) => _overlay.Opacity = Svc.Media.IsPlaying ? 0 : 1;
        return g;
    }

    protected override void OnAttach() { Svc.Media.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Media.Changed -= Render;

    void Render()
    {
        var m = Svc.Media;
        _setArt(m.Art);
        _glyph.Text = m.IsPlaying ? Glyphs.Pause : Glyphs.Play;
        if (!IsMouseOver) _overlay.Opacity = m.IsPlaying && m.Art != null ? 0 : 1;
        ToolTip = m.HasSession ? $"{m.Title} — {m.Artist}" : "Not playing";
    }

    protected override void OnClick() => Svc.Media.TogglePlayPause();
    protected override void AddMenuItems(ContextMenu menu)
    {
        menu.Items.Add(Item("Next track", () => Svc.Media.Next()));
        menu.Items.Add(Item("Previous track", () => Svc.Media.Previous()));
        menu.Items.Add(Item("Show player…", OpenCard));
    }
    protected override UIElement BuildCard() => MediaCard.Build();
}

static class MediaCard
{
    public static UIElement Build()
    {
        var m = Svc.Media;
        var (frame, set) = Art.Make(268, false);
        set(m.Art);
        frame.Margin = new Thickness(0, 0, 0, 14);
        frame.HorizontalAlignment = HorizontalAlignment.Center;
        var title = Text(m.HasSession ? m.Title! : "Not playing", 17, FontWeights.Bold);
        var artist = Text(m.Artist ?? "", 13, null, "Text.Secondary");
        var progress = Progress(out var setP, "Text.Primary", 4);
        progress.Margin = new Thickness(0, 14, 0, 4);
        var pos = Text("", 10.5, null, "Text.Tertiary");
        var dur = Text("", 10.5, null, "Text.Tertiary");
        void Tick(DateTime _)
        {
            var p = m.EstimatedPosition;
            setP(m.Duration.TotalSeconds > 0 ? p.TotalSeconds / m.Duration.TotalSeconds : 0);
            pos.Text = Fmt.Countdown(p); dur.Text = m.Duration.TotalSeconds > 0 ? Fmt.Countdown(m.Duration) : "";
        }
        progress.Loaded += (_, _) => { Tick(DateTime.Now); Svc.Ticker.Tick += Tick; };
        progress.Unloaded += (_, _) => Svc.Ticker.Tick -= Tick;

        var play = CircleButton(m.IsPlaying ? Glyphs.Pause : Glyphs.Play, () => m.TogglePlayPause(), 52, "Text.Primary", "Flyout.Background", 20);
        void Sync() => ((TextBlock)play.Content).Text = m.IsPlaying ? Glyphs.Pause : Glyphs.Play;
        m.Changed += Sync;
        play.Unloaded += (_, _) => m.Changed -= Sync;
        var controls = H(
            CircleButton(Glyphs.Previous, m.Previous, 40, "Transparent", "Text.Primary", 16),
            play.Margin(18, 0),
            CircleButton(Glyphs.Next, m.Next, 40, "Transparent", "Text.Primary", 16));
        controls.HorizontalAlignment = HorizontalAlignment.Center;
        controls.Margin = new Thickness(0, 8, 0, 0);
        var source = Text(m.AppId != null ? "Playing from " + Friendly(m.AppId) : "", 11, null, "Text.Tertiary").Margin(0, 12, 0, 0);
        return V(frame, title, artist, progress, Columns("*,auto", pos, dur), controls, source);
    }

    static string Friendly(string aumid)
    {
        var s = aumid.Split('!')[0].Split('_')[0];
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        var dot = s.LastIndexOf('.');
        return dot >= 0 && dot < s.Length - 1 ? s[(dot + 1)..] : s;
    }
}

/// <summary>Output device name, percent and a fat slider. Scroll to change; click the speaker to mute.</summary>
public sealed class VolumeWidget : WidgetBase
{
    TextBlock _name = null!, _pct = null!, _icon = null!;
    Slider _slider = null!;
    bool _syncing;
    protected override bool Clickable => false;

    protected override UIElement BuildContent()
    {
        _icon = Icon(Glyphs.Volume, 11, "Text.Secondary");
        var iconHost = new Border { Background = Brushes.Transparent, Child = _icon, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0) };
        iconHost.MouseLeftButtonUp += (_, e) => { e.Handled = true; Svc.Speakers.ToggleMute(); };
        _name = Text("", 11.5, FontWeights.Bold);
        _name.MaxWidth = 110;
        _pct = Text("", 10.5, FontWeights.SemiBold, "Text.Secondary");
        _slider = Slider(0, v => { if (!_syncing) Svc.Speakers.SetVolume(v); _pct.Text = $"{v:0}%"; });
        _slider.Width = 148;
        var head = Columns("auto,*,auto", iconHost, _name, _pct);
        MouseWheel += (_, e) => { Svc.Speakers.SetVolume(Svc.Speakers.Volume + (e.Delta > 0 ? 2 : -2)); e.Handled = true; };
        return V(head, _slider.Margin(0, 4, 0, 0));
    }

    protected override void OnAttach() { Svc.Speakers.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Speakers.Changed -= Render;

    void Render()
    {
        var a = Svc.Speakers;
        _name.Text = a.ShortName;
        if (!_slider.IsMouseCaptureWithin) { _syncing = true; _slider.Value = a.Volume; _syncing = false; }
        _pct.Text = a.Muted ? "Muted" : $"{a.Volume:0}%";
        _icon.Text = a.Muted ? Glyphs.Mute : a.Volume < 1 ? Glyphs.Volume0 : a.Volume < 34 ? Glyphs.Volume1 : a.Volume < 67 ? Glyphs.Volume2 : Glyphs.Volume;
    }

    protected override void AddMenuItems(ContextMenu menu)
    {
        menu.Items.Add(Item(Svc.Speakers.Muted ? "Unmute" : "Mute", Svc.Speakers.ToggleMute));
        menu.Items.Add(Item("Volume mixer", () => WinActions.OpenSettings("apps-volume")));
        menu.Items.Add(Item("Sound settings", () => WinActions.OpenSettings("sound")));
    }
}

/// <summary>Microphone mute toggle. Red when muted.</summary>
public sealed class MicrophoneWidget : WidgetBase
{
    TextBlock _icon = null!;
    protected override UIElement BuildContent()
    {
        Tile.Width = 48;
        Tile.Padding = new Thickness(0);
        _icon = Icon(Glyphs.Mic, 17);
        return _icon;
    }

    protected override void OnAttach() { Svc.Microphone.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Microphone.Changed -= Render;

    void Render()
    {
        var m = Svc.Microphone;
        _icon.Text = m.Muted ? Glyphs.MicOff : Glyphs.Mic;
        _icon.Fg(m.Muted ? "Sys.Red" : "Text.Primary");
        ToolTip = m.Available ? $"{m.ShortName} — {(m.Muted ? "muted" : "live")}" : "No microphone";
    }

    protected override void OnClick() => Svc.Microphone.ToggleMute();
}
