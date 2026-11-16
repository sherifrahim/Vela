using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Vela.Core;
using Vela.Shell;
using Vela.UI;
using static Vela.UI.UI;

namespace Vela.Views;

/// <summary>A running countdown / stopwatch published by a widget so the island can show it.</summary>
public sealed record TimerActivity(string Id, string Label, string ColorKey, DateTime? EndsAt, DateTime? Since, Action Toggle, Action Open);

/// <summary>What widgets tell the island (kept separate so widgets don't depend on the window).</summary>
public static class Island
{
    static readonly Dictionary<string, TimerActivity> _timers = new();
    public static event Action? TimersChanged;
    public static IReadOnlyCollection<TimerActivity> Timers => _timers.Values;

    public static void SetTimer(TimerActivity? a, string id)
    {
        bool changed = a == null ? _timers.Remove(id) : !_timers.TryGetValue(id, out var old) || old != a;
        if (a != null) _timers[id] = a;
        if (changed) TimersChanged?.Invoke();
    }
}

/// <summary>
/// Vela Island: a Dynamic-Island-style black pill at the top centre of the main display.
/// It appears only while something is happening — a running timer, music playing, a volume change, power
/// plugged in or out — and grows into a small player / detail card on hover. Click opens the matching card.
/// Layered + click-through outside the pill; hidden with the dock in fullscreen; no work while idle.
/// </summary>
public sealed class IslandWindow : GlassWindow
{
    public static IslandWindow? Instance { get; private set; }

    const double CompactH = 30, ExpandedH = 112, CompactMinW = 130, ExpandedW = 360, TopGap = 7;
    readonly Border _pill;
    readonly ContentControl _body = new() { Focusable = false };
    readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    UIElement? _hud;               // transient (volume / power), shown over everything else
    bool _expanded, _fullscreen, _tickHooked;
    double _lastVolume = -1;
    bool? _lastMuted, _lastAc;
    string _shown = "";

    public static void Sync()
    {
        bool want = Settings.Current.Island;
        if (want && Instance == null) { Instance = new IslandWindow(); Instance.Show(); }
        else if (!want && Instance != null) { Instance.Close(); }
    }

    IslandWindow()
    {
        Title = "Vela Island";
        Topmost = true;
        NoActivate = true;
        ShowActivated = false;
        Width = ExpandedW + 40; Height = ExpandedH + TopGap + 30;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _pill = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255)),
            CornerRadius = new CornerRadius(CompactH / 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, TopGap, 0, 0),
            Height = CompactH, MinWidth = CompactMinW,
            Padding = new Thickness(6, 0, 10, 0),
            Child = _body, Cursor = Cursors.Hand,
            Opacity = 0, ClipToBounds = true,
        };
        _pill.SetResourceReference(Border.BorderThicknessProperty, "Hairline");
        _pill.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45, Color = Colors.Black, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance };
        Content = new Grid { Children = { _pill } };
        // White text on the black pill regardless of the theme.
        Resources["Text.Primary"] = Brushes.White;
        Resources["Text.Secondary"] = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255));
        Resources["Fill.Control"] = new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255));
        Resources["Fill.Track"] = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));

        _pill.MouseEnter += (_, _) => SetExpanded(true);
        _pill.MouseLeave += (_, _) => SetExpanded(false);
        _pill.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnClick(); };
        _hudTimer.Tick += (_, _) => { _hudTimer.Stop(); _hud = null; Render(); };

        Loaded += (_, _) =>
        {
            Place();
            Svc.Media.Changed += Render;
            Svc.Speakers.Changed += OnVolume;
            Island.TimersChanged += OnTimers;
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPower;
            DockWindow.PrimaryFullscreenChanged += OnFullscreen;
            _fullscreen = DockWindow.Instance?.IsFullscreenHidden == true;
            OnTimers();
            Render();
        };
        Closed += (_, _) =>
        {
            Instance = null;
            Svc.Media.Changed -= Render;
            Svc.Speakers.Changed -= OnVolume;
            Island.TimersChanged -= OnTimers;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPower;
            DockWindow.PrimaryFullscreenChanged -= OnFullscreen;
            if (_tickHooked) Svc.Ticker.Tick -= OnTick;
        };
    }

    void Place()
    {
        var m = Interop.Native.GetPrimaryMonitor(out _).rcMonitor;
        double s = DpiScale;
        Left = (m.Left + m.Width / 2.0) / s - Width / 2;
        Top = m.Top / s;
    }

    // ---------------- sources ----------------

    void OnFullscreen(bool fs) { _fullscreen = fs; Render(); }

    void OnTimers()
    {
        bool need = Island.Timers.Count > 0;
        if (need && !_tickHooked) { Svc.Ticker.Tick += OnTick; _tickHooked = true; }
        else if (!need && _tickHooked) { Svc.Ticker.Tick -= OnTick; _tickHooked = false; }
        Render();
    }

    void OnTick(DateTime _) { if (_hud == null) Render(); }

    void OnVolume()
    {
        var a = Svc.Speakers;
        bool first = _lastVolume < 0;
        bool changed = !first && (Math.Abs(a.Volume - _lastVolume) >= 1 || a.Muted != _lastMuted);
        _lastVolume = a.Volume; _lastMuted = a.Muted;
        if (!changed) return;
        var icon = Icon(a.Muted ? Glyphs.Mute : Glyphs.Volume, 11, "Text.Primary");
        var bar = Progress(out var set, "Text.Primary", 4);
        bar.Width = 120;
        set(a.Muted ? 0 : a.Volume / 100);
        ShowHud(H(icon.Margin(4, 0, 10, 0), bar, Text(a.Muted ? "Muted" : $"{a.Volume:0}", 10, FontWeights.SemiBold).Margin(10, 0, 0, 0)));
    }

    void OnPower(object? s, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.StatusChange) return;
        Dispatcher.BeginInvoke(() =>
        {
            var sys = Svc.Sys;
            sys.SampleBattery();
            if (_lastAc == sys.OnAc || !sys.HasBattery) { _lastAc = sys.OnAc; return; }
            _lastAc = sys.OnAc;
            var bolt = new BoltMark { Width = 7, Height = 11, Margin = new Thickness(6, 0, 8, 0) };
            var label = Text(sys.OnAc ? "Charging" : "On battery", 10, FontWeights.SemiBold);
            var pct = Text($"{sys.BatteryPercent}%", 10, FontWeights.SemiBold, sys.OnAc ? "Sys.Green" : "Text.Secondary").Margin(14, 0, 0, 0);
            ShowHud(H(bolt, label, pct));
        });
    }

    void ShowHud(UIElement content)
    {
        _hud = content;
        _hudTimer.Stop();
        _hudTimer.Start();
        Render();
    }

    // ---------------- rendering ----------------

    void SetExpanded(bool on)
    {
        if (_expanded == on) return;
        _expanded = on;
        Render();
    }

    void OnClick()
    {
        var t = Island.Timers.FirstOrDefault();
        if (t != null) { t.Open(); return; }
        if (Svc.Media.HasSession) Svc.Media.TogglePlayPause();
    }

    /// <summary>Pick what to show: HUD &gt; timer &gt; music; nothing → hide.</summary>
    void Render()
    {
        if (_fullscreen) { Fade(false); return; }
        var timer = Island.Timers.OrderBy(t => t.EndsAt ?? DateTime.MaxValue).FirstOrDefault();
        var m = Svc.Media;
        bool music = m.HasSession && (m.IsPlaying || _expanded);

        UIElement? view;
        string key;
        if (_hud != null) { view = _hud; key = "hud"; }
        else if (timer != null) { view = _expanded ? TimerExpanded(timer) : TimerCompact(timer); key = "timer" + _expanded; }
        else if (music) { view = _expanded ? MusicExpanded() : MusicCompact(); key = "music" + _expanded + m.Title; }
        else { view = null; key = ""; }

        if (view == null) { Fade(false); _shown = ""; return; }
        _body.Content = view;
        bool big = _expanded && _hud == null;
        if (key != _shown || _pill.Opacity < 1)
        {
            Resize(big);
            Fade(true);
        }
        _shown = key;
    }

    /// <summary>0 = compact pill, 1 = expanded card. One animated value drives height, minimum width, corner
    /// radius and padding together, so the island morphs instead of snapping (Dynamic-Island style).</summary>
    public static readonly DependencyProperty ExpansionProperty = DependencyProperty.Register(nameof(Expansion), typeof(double), typeof(IslandWindow),
        new PropertyMetadata(0.0, (d, _) => ((IslandWindow)d).ApplyExpansion()));
    public double Expansion { get => (double)GetValue(ExpansionProperty); set => SetValue(ExpansionProperty, value); }

    void ApplyExpansion()
    {
        double t = Expansion;
        static double L(double a, double b, double k) => a + (b - a) * k;
        _pill.Height = L(CompactH, ExpandedH, t);
        _pill.MinWidth = L(CompactMinW, ExpandedW, t);
        _pill.CornerRadius = new CornerRadius(L(CompactH / 2, 26, t));
        _pill.Padding = new Thickness(L(6, 14, t), L(0, 12, t), L(10, 14, t), L(0, 12, t));
    }

    void Resize(bool big)
    {
        // A slight overshoot on the way out gives the springy feel of the reference island.
        var ease = big ? (IEasingFunction)new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(ExpansionProperty, new DoubleAnimation(big ? 1 : 0, TimeSpan.FromMilliseconds(big ? 200 : 170)) { EasingFunction = ease });
    }

    void Fade(bool show)
    {
        double to = show ? 1 : 0;
        if (Math.Abs(_pill.Opacity - to) < 0.01 && _pill.ReadLocalValue(OpacityProperty) != DependencyProperty.UnsetValue) return;
        _pill.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(show ? 160 : 220)));
        _pill.IsHitTestVisible = show;
    }

    // ---- previews for Vela.exe --render ----
    internal static IEnumerable<(string Name, UIElement View, bool Big)> Previews()
    {
        var t = new TimerActivity("preview", "Focus", "Sys.Red", DateTime.Now.AddMinutes(24).AddSeconds(13), null, () => { }, () => { });
        yield return ("Timer", TimerCompact(t), false);
        yield return ("Timer (hover)", TimerExpanded(t), true);
        var bar = Progress(out var set, "Text.Primary", 4);
        bar.Width = 120;
        set(0.62);
        yield return ("Volume", H(Icon(Glyphs.Volume, 11, "Text.Primary").Margin(4, 0, 10, 0), bar, Text("62", 10, FontWeights.SemiBold).Margin(10, 0, 0, 0)), false);
        yield return ("Power", H(new BoltMark { Width = 7, Height = 11, Margin = new Thickness(6, 0, 8, 0) }, Text("Charging", 10, FontWeights.SemiBold), Text("80%", 10, FontWeights.SemiBold, "Sys.Green").Margin(14, 0, 0, 0)), false);
        if (Svc.Media.HasSession) { yield return ("Music", MusicCompact(), false); yield return ("Music (hover)", MusicExpanded(), true); }
    }

    internal static Border PreviewPill(UIElement view, bool big)
    {
        var b = new Border
        {
            Background = Brushes.Black, CornerRadius = new CornerRadius(big ? 26 : CompactH / 2),
            Height = big ? ExpandedH : CompactH, MinWidth = big ? ExpandedW : CompactMinW,
            Padding = big ? new Thickness(14, 12, 14, 12) : new Thickness(6, 0, 10, 0), Child = view, HorizontalAlignment = HorizontalAlignment.Left,
        };
        b.Resources["Text.Primary"] = Brushes.White;
        b.Resources["Text.Secondary"] = new SolidColorBrush(Color.FromArgb(0x99, 255, 255, 255));
        b.Resources["Fill.Control"] = new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255));
        b.Resources["Fill.Track"] = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255));
        return b;
    }

    // ---- music ----
    static FrameworkElement Cover(double size, double radius)
    {
        var img = new Image { Source = Svc.Media.Art, Stretch = Stretch.UniformToFill, Width = size, Height = size };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(radius), Child = img };
        b.Bg("Fill.Control");
        b.Clip = new RectangleGeometry(new Rect(0, 0, size, size), radius, radius);
        return b;
    }

    /// <summary>Static level meter (no continuous animation): bar heights from the track title, tinted green.</summary>
    static FrameworkElement Meter(bool playing)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        int seed = (Svc.Media.Title ?? "").GetHashCode();
        for (int i = 0; i < 4; i++)
        {
            double h = playing ? 5 + Math.Abs((seed >> (i * 5)) % 9) : 3;
            p.Children.Add(new Border { Width = 2.5, Height = h, CornerRadius = new CornerRadius(1.25), Margin = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Center }.Bg("Sys.Green"));
        }
        return p;
    }

    static UIElement MusicCompact()
    {
        var m = Svc.Media;
        var title = Text(m.Title ?? "", 10, FontWeights.SemiBold);
        title.MaxWidth = 150;
        var g = Columns("auto,*,auto", Cover(20, 6), title.Margin(9, 0, 12, 0), Meter(m.IsPlaying));
        g.VerticalAlignment = VerticalAlignment.Center;
        return g;
    }

    static UIElement MusicExpanded()
    {
        var m = Svc.Media;
        var title = Text(m.Title ?? "", 13, FontWeights.SemiBold);
        var artist = Text(m.Artist ?? "", 11, null, "Text.Secondary");
        var bar = Progress(out var set, "Text.Primary", 3.5);
        bar.Margin = new Thickness(0, 9, 0, 0);
        set(m.Duration.TotalSeconds > 0 ? m.EstimatedPosition.TotalSeconds / m.Duration.TotalSeconds : 0);
        var controls = H(
            CircleButton(Glyphs.Previous, () => m.Previous(), 28, "Transparent", "Text.Primary", 11),
            CircleButton(m.IsPlaying ? Glyphs.Pause : Glyphs.Play, () => m.TogglePlayPause(), 34, "Fill.Control", "Text.Primary", 13).Margin(4, 0),
            CircleButton(Glyphs.Next, () => m.Next(), 28, "Transparent", "Text.Primary", 11));
        controls.HorizontalAlignment = HorizontalAlignment.Right;
        var info = V(Columns("*,auto", V(title, artist), Meter(m.IsPlaying)), bar, controls.Margin(0, 6, 0, 0));
        return Columns("auto,*", Cover(78, 16), info.Margin(14, 0, 0, 0));
    }

    // ---- timers ----
    static string Remaining(TimerActivity t) =>
        t.EndsAt is { } end ? Fmt.Countdown(end - DateTime.Now) : t.Since is { } s ? Fmt.Countdown(DateTime.Now - s) : "";

    static UIElement TimerCompact(TimerActivity t)
    {
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(6, 0, 8, 0) }.Bg(t.ColorKey);
        return H(dot, Text(t.Label, 10, FontWeights.SemiBold, t.ColorKey), Text(Remaining(t), 11, FontWeights.SemiBold).Margin(12, 0, 0, 0));
    }

    static UIElement TimerExpanded(TimerActivity t)
    {
        var label = Text(t.Label, 12, FontWeights.SemiBold, t.ColorKey);
        var time = Text(Remaining(t), 34, FontWeights.SemiBold);
        var toggle = CircleButton(Glyphs.Pause, t.Toggle, 44, t.ColorKey, "OnAccent", 15);
        return Columns("*,auto", V(label, time.Margin(0, -2, 0, 0)), toggle);
    }
}
