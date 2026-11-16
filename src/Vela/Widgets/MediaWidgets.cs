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
    public static (Border frame, Action<ImageSource?> set) Make(double size, bool round, double radius = -1, bool placeholder = true)
    {
        var img = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var placeholderIcon = Icon("", size * 0.42, "Text.Secondary"); // music note
        var g = new Grid();
        g.Children.Add(placeholderIcon);
        g.Children.Add(img);
        var frame = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(round ? size / 2 : radius >= 0 ? radius : size * 0.22), Child = g, ClipToBounds = true };
        frame.Bg("Fill.Control");
        // Border.ClipToBounds doesn't clip to the corner radius; clip explicitly.
        frame.SizeChanged += (_, _) => frame.Clip = new RectangleGeometry(new Rect(0, 0, size, size), frame.CornerRadius.TopLeft, frame.CornerRadius.TopLeft);
        return (frame, src => { img.Source = src; placeholderIcon.Visibility = src == null && placeholder ? Visibility.Visible : Visibility.Collapsed; });
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
        Tile.Padding = new Thickness(6, 0, 8, 0);
        var (frame, set) = Art.Make(30, false, 7);
        _setArt = set;
        _title = Text("", 10, FontWeights.Bold);
        _title.MaxWidth = 120;
        _artist = Text("", 8.5, null, "Text.Secondary");
        _artist.MaxWidth = 120;
        _play = CircleButton(Glyphs.Play, () => Svc.Media.TogglePlayPause(), 26, "Fill.Control", "Text.Primary", 9.5);
        var buttons = H(
            CircleButton(Glyphs.Previous, () => Svc.Media.Previous(), 22, "Transparent", "Text.Primary", 8.5),
            _play.Margin(1, 0),
            CircleButton(Glyphs.Next, () => Svc.Media.Next(), 22, "Transparent", "Text.Primary", 8.5));
        buttons.Margin = new Thickness(10, 0, 0, 0);
        return H(frame, V(_title, _artist).Margin(8, 0, 0, 0), buttons);
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

    protected override UIElement BuildContent()
    {
        // Reference: a 36 px round cover with a thin white ring, sitting on a normal square tile.
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        const double d = 35;
        var (frame, set) = Art.Make(d, true, placeholder: false);
        _setArt = set;
        _glyph = Icon(Glyphs.Play, 11, "OnAccent");
        _overlay = new Border { Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), Child = _glyph, Opacity = 0 };
        var ring = new System.Windows.Shapes.Ellipse { Width = d + 1, Height = d + 1, Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false };
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(frame);
        g.Children.Add(_overlay);
        g.Children.Add(ring);
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
        // No cover: just the play glyph on the plain disc; with a cover: a dimmed overlay on hover / when paused.
        _overlay.Background = m.Art == null ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0));
        _glyph.Fg(m.Art == null ? "Text.Primary" : "OnAccent");
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
        // Reference: [speaker in a small circle] Speakers ........ 8%   over a full-width pill slider.
        _icon = Icon(Glyphs.Volume, 7.5, "Text.Secondary");
        var iconHost = new Border { Width = 15, Height = 15, CornerRadius = new CornerRadius(7.5), Child = _icon, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0) }.Bg("Fill.Control");
        iconHost.MouseLeftButtonUp += (_, e) => { e.Handled = true; Svc.Speakers.ToggleMute(); };
        iconHost.ToolTip = "Mute";
        _name = Text("", 8.5, FontWeights.SemiBold);
        _name.MaxWidth = 100;
        _pct = Text("", 7.5, FontWeights.Medium, "Text.Secondary");
        _slider = Slider(0, v => { if (!_syncing) Svc.Speakers.SetVolume(v); _pct.Text = $"{v:0}%"; });
        _slider.Width = 142;
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
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        _icon = Icon(Glyphs.Mic, 14);
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

/// <summary>Square album cover filling a whole tile. Click: play/pause. Card: the player.</summary>
public sealed class AlbumArtWidget : WidgetBase
{
    Action<ImageSource?> _setArt = null!;
    TextBlock _glyph = null!;
    Border _overlay = null!;

    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        Tile.BorderThickness = new Thickness(0);
        var (frame, set) = Art.Make(TileHeight, false, TileRadius);
        _setArt = set;
        _glyph = Icon(Glyphs.Play, 12, "OnAccent");
        _overlay = new Border { CornerRadius = new CornerRadius(TileRadius), Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), Child = _glyph, Opacity = 0 };
        var g = new Grid();
        g.Children.Add(frame);
        g.Children.Add(_overlay);
        MouseEnter += (_, _) => _overlay.Opacity = 1;
        MouseLeave += (_, _) => _overlay.Opacity = 0;
        return g;
    }

    protected override void OnAttach() { Svc.Media.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Media.Changed -= Render;

    void Render()
    {
        var m = Svc.Media;
        _setArt(m.Art);
        _glyph.Text = m.IsPlaying ? Glyphs.Pause : Glyphs.Play;
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
