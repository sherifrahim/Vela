using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Vela.Core;
using Vela.Shell;
using Vela.Theming;
using Vela.Widgets;
using static Vela.Interop.Native;

namespace Vela.Views;

/// <summary>
/// The dock: one borderless, non-activating, topmost window spanning the primary monitor's edge,
/// whose visible shape (window region) is just the rounded bar.
/// Owns: appbar registration, taskbar hiding, fullscreen hiding, edge drag, widget layout/reorder.
/// </summary>
public partial class DockWindow : GlassWindow
{
    /// <summary>The dock on the primary display (owns the taskbar, shell hook and windows).</summary>
    public static DockWindow? Instance { get; private set; }
    /// <summary>Every dock: the primary plus one per extra display when Settings.Displays == "all".</summary>
    public static readonly List<DockWindow> All = new();
    /// <summary>The primary display went fullscreen (true) or back (false). The island hides with it.</summary>
    public static event Action<bool>? PrimaryFullscreenChanged;
    public bool IsFullscreenHidden => _fullscreenHidden;
    /// <summary>The dock that contains <paramref name="d"/> (falls back to the primary dock).</summary>
    public static DockWindow? For(DependencyObject? d) => (d != null ? Window.GetWindow(d) as DockWindow : null) ?? Instance;

    /// <summary>Monitor device name (e.g. \.DISPLAY2) for secondary docks; null = primary display.</summary>
    readonly string? _device;
    public bool IsPrimary => _device == null;
    /// <summary>The dock whose edit triggered a "widgets" save (it doesn't need to rebuild itself).</summary>
    static DockWindow? _origin;
    /// <summary>The visible bar (cards anchor to its edge).</summary>
    public FrameworkElement BarElement => Bar;

    /// <summary>Registered message for `Vela.exe --widgets / --settings` (wParam 1 / 2).</summary>
    public static readonly uint CommandMessage = RegisterWindowMessage("Vela.Command");

    AppBar? _appBar;
    uint _taskbarCreatedMsg;
    bool _fullscreenHidden, _repositionQueued;
    readonly ScaleTransform _sizeScale = new(1, 1);
    WidgetPickerWindow? _picker;
    SettingsWindow? _settings;
    int _enforceCounter;

    // edge drag / reorder state
    Point _dragStart;
    bool _barDragging, _barDragMoved;
    WidgetBase? _reorderItem;

    public event Action? WidgetsChanged;

    /// <summary>Bar geometry (DIPs at size M): padding around the tiles and the outer corner radius.</summary>
    public const double BarPadding = 3, BarRadius = 16;
    /// <summary>Gap between the top of the bar and the reserved work area (room for the bar's shadow).</summary>
    const double TopGap = 4;
    /// <summary>Transparent space above the bar (not reserved from other windows) for magnified icons.</summary>
    public const double Headroom = 18;

    public DockWindow(string? device = null)
    {
        InitializeComponent();
        _device = device;
        if (device == null) Instance = this; else Title = "Vela Dock (" + device.TrimStart('\\', '.') + ")";
        All.Add(this);
        NoActivate = true;
        // No clip on the bar: its rounded layers round themselves, and magnified app icons must be able to
        // rise above it into the transparent headroom (macOS dock).
        RegionElement = null;
        RegionRadius = BarRadius;
        BarHost.LayoutTransform = _sizeScale;
        Left = -32000; Top = -32000; Width = 100; Height = 60;

        ThemeManager.Changed += ApplyLook;
        Settings.Changed += OnSettingChanged;
        WallpaperGlass.Changed += UpdateGlass;
        ScreenEvents.Changed += OnScreenMovedForLens;
        Svc.Windows.ForegroundChanged += OnScreenMovedForLens;
        Vela.Services.GameMode.Changed += UpdateGlass;
        Bar.SizeChanged += (_, _) => UpdateGlass();
        WidgetBase.EditModeChanged += OnEditMode;

        Bar.ContextMenu = new ContextMenu();
        Bar.ContextMenuOpening += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindWidget(d) != null) return; // widget menus handle themselves
            Bar.ContextMenu = BuildBarMenu();
        };
        Closed += (_, _) =>
        {
            All.Remove(this);
            WidgetBase.EditModeChanged -= OnEditMode;
            try { _backdrop?.Close(); } catch { }
            if (_autoHideHooked && _autoHide != null) { Svc.Windows.ForegroundChanged -= _autoHide.Poke; ScreenEvents.Changed -= _autoHide.Poke; }
            _adaptive?.Dispose();
            Svc.Windows.ForegroundChanged -= PokeAdaptive;
            ScreenEvents.Changed -= PokeAdaptive;
            ThemeManager.Changed -= ApplyLook;
            Settings.Changed -= OnSettingChanged;
            WallpaperGlass.Changed -= UpdateGlass;
            ScreenEvents.Changed -= OnScreenMovedForLens;
            Svc.Windows.ForegroundChanged -= OnScreenMovedForLens;
            Vela.Services.GameMode.Changed -= UpdateGlass;
            _lensDebounce?.Stop();
            DropCaptures();
            if (IsPrimary) Application.Current.Shutdown();
        };
        Bar.MouseLeftButtonDown += OnBarMouseDown;
        Bar.MouseMove += OnBarMouseMove;
        Bar.MouseLeftButtonUp += OnBarMouseUp;
        Items.PreviewMouseLeftButtonDown += OnItemsPreviewDown;
        Items.PreviewMouseMove += OnItemsPreviewMove;
        Items.PreviewMouseLeftButtonUp += OnItemsPreviewUp;
        SetupDesktopCorner();
        Items.NaturalWidthChanged += FitItems;
        SizeChanged += (_, _) => FitItems();
    }

    void OnEditMode() => Items.Cursor = WidgetBase.EditMode ? Cursors.SizeWE : null;

    /// <summary>
    /// Dynamic fit: if the widgets need more width than the screen offers (small display, size L, many
    /// widgets), scale the row down uniformly (to 60 % at most) instead of clipping; back to 100 % when it fits.
    /// </summary>
    void FitItems()
    {
        if (!IsLoaded || Items.NaturalWidth <= 0 || ActualWidth < 400) return; // not positioned yet
        double scale = ThemeManager.SizeScale;
        double side = BarHost.Margin.Left + BarHost.Margin.Right;
        double corner = DesktopCorner.Visibility == Visibility.Visible ? DesktopCorner.Width + DesktopCorner.Margin.Left + DesktopCorner.Margin.Right : 0;
        double avail = (ActualWidth - side) / scale - 2 * BarPadding - 2 - corner - Items.Margin.Left - Items.Margin.Right;
        double f = Items.NaturalWidth > avail ? Math.Max(0.6, avail / Items.NaturalWidth) : 1;
        double cur = Items.LayoutTransform is ScaleTransform st ? st.ScaleX : 1;
        if (Math.Abs(f - cur) < 0.005) return;
        Items.LayoutTransform = f >= 0.999 ? Transform.Identity : new ScaleTransform(f, f);
        if (f < 1) Log.Info($"Dock content scaled to {f:P0} to fit the screen");
    }

    // ------------------------------------------------------------------ live blur backdrop

    BackdropWindow? _backdrop;

    /// <summary>Keep this dock's blur window exactly under the bar (or hidden).</summary>
    void SyncBackdrop()
    {
        bool want = LiveBlurOn && Hwnd != IntPtr.Zero && Bar.IsLoaded && !_fullscreenHidden && _autoHide?.IsHidden != true;
        if (!want) { _backdrop?.HideBackdrop(); return; }
        try
        {
            var tl = Bar.PointToScreen(new Point(0, 0));
            var br = Bar.PointToScreen(new Point(Bar.ActualWidth, Bar.ActualHeight));
            _backdrop ??= new BackdropWindow(BackdropWindow.Best);
            double radius = BarRadius * ThemeManager.SizeScale * VisualTreeHelper.GetDpi(this).DpiScaleX;
            _backdrop.Place(new RECT { Left = (int)Math.Round(tl.X), Top = (int)Math.Round(tl.Y), Right = (int)Math.Round(br.X), Bottom = (int)Math.Round(br.Y) }, radius, Hwnd);
        }
        catch (InvalidOperationException) { }
    }

    // ------------------------------------------------------------------ auto-hide

    DockAutoHide? _autoHide;
    bool _autoHideHooked;
    static int _openMenus;

    static DockWindow()
    {
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((_, _) => _openMenus++));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler((_, _) => _openMenus = Math.Max(0, _openMenus - 1)));
    }

    /// <summary>Dragging, reordering or a menu is open: auto-hide must wait.</summary>
    public bool IsInteracting => _reordering || _barDragging || _openMenus > 0;

    void SetupAutoHide()
    {
        if (_autoHide == null)
        {
            _autoHide = new DockAutoHide(this, BarHost);
            _autoHide.HiddenChanged += hidden =>
            {
                EdgeTrigger.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
                if (hidden) _backdrop?.HideBackdrop();
                else _ = Task.Delay(230).ContinueWith(_ => Dispatcher.BeginInvoke(SyncBackdrop)); // after the 200 ms slide settles
            };
            BarHost.MouseEnter += (_, _) => _autoHide.PointerArrived();
            BarHost.MouseLeave += (_, _) => _autoHide.PointerLeft();
            EdgeTrigger.MouseEnter += (_, _) => _autoHide.PointerArrived();
        }
        bool on = Settings.Current.AutoHide != "off";
        if (on && !_autoHideHooked) { Svc.Windows.ForegroundChanged += _autoHide.Poke; ScreenEvents.Changed += _autoHide.Poke; _autoHideHooked = true; }
        else if (!on && _autoHideHooked) { Svc.Windows.ForegroundChanged -= _autoHide.Poke; ScreenEvents.Changed -= _autoHide.Poke; _autoHideHooked = false; }
        EdgeTrigger.VerticalAlignment = Settings.Current.Edge == "top" ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        _autoHide.Poke();
        _autoHide.Evaluate();
    }

    // ------------------------------------------------------------------ match the screen (adaptive theme)

    AdaptiveBackdrop? _adaptive;
    Brush? _adaptiveBrush;
    bool? _adaptiveLight;

    /// <summary>The light/dark resource set this dock currently uses (cards opened from it adopt it too).</summary>
    public ResourceDictionary? AdaptiveResources { get; private set; }

    void SetupAdaptive()
    {
        bool on = Settings.Current.AdaptiveTheme;
        if (!on)
        {
            if (_adaptive == null) return;
            _adaptive.Dispose();
            _adaptive = null;
            Svc.Windows.ForegroundChanged -= PokeAdaptive;
            ScreenEvents.Changed -= PokeAdaptive;
            _adaptiveBrush = null;
            _adaptiveLight = null;
            AdaptiveResources = null;
            Resources.MergedDictionaries.Clear();
            UpdateGlass();
            return;
        }
        if (_adaptive == null)
        {
            _adaptive = new AdaptiveBackdrop(AdaptiveStrip, OnAdaptiveSample);
            Svc.Windows.ForegroundChanged += PokeAdaptive;
            ScreenEvents.Changed += PokeAdaptive;
        }
        _adaptiveLight = null;     // theme changed: re-apply the variant on the next sample
        PokeAdaptive();
    }

    void PokeAdaptive() => _adaptive?.Poke();

    /// <summary>The strip of this display right next to the bar (above it, or below when docked at the top).</summary>
    (RECT strip, bool active) AdaptiveStrip()
    {
        if (Environment.GetEnvironmentVariable("VELA_DEBUG_ADAPTIVE") == "1")
            Log.Info($"adaptive? {_device} hwnd={Hwnd != IntPtr.Zero} fs={_fullscreenHidden} vis={IsVisible} loaded={Bar.IsLoaded} flyout={FlyoutWindow.Current != null} preview={WindowPreview.IsOpen}");
        if (Hwnd == IntPtr.Zero || _fullscreenHidden || !IsVisible || !Bar.IsLoaded || FlyoutWindow.Current != null || WindowPreview.IsOpen)
            return (default, false);
        try
        {
            var tl = Bar.PointToScreen(new Point(0, 0));
            var br = Bar.PointToScreen(new Point(Bar.ActualWidth, Bar.ActualHeight));
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;
            int h = (int)(36 * dpi), gap = (int)(TopGap * dpi) + 2;
            bool top = Settings.Current.Edge == "top";
            var r = new RECT
            {
                Left = (int)tl.X, Right = (int)br.X,
                Top = top ? (int)br.Y + gap : (int)tl.Y - gap - h,
                Bottom = top ? (int)br.Y + gap + h : (int)tl.Y - gap,
            };
            // Heartbeat only while a real window borders the dock on this display (its content can change silently).
            var fg = GetForegroundWindow();
            bool windowNearby = fg != IntPtr.Zero && !IsIconic(fg) && MonitorFromWindow(fg, 2) == MonitorFromWindow(Hwnd, 2)
                && GetClassNameOf(fg) is not ("Progman" or "WorkerW") && GetWindowRect(fg, out var fr) && (top ? fr.Top <= r.Bottom : fr.Bottom >= r.Top);
            _adaptive?.SetHeartbeat(windowNearby);
            return (r, true);
        }
        catch (InvalidOperationException) { return (default, false); }
    }

    void OnAdaptiveSample(AdaptiveBackdrop.Sample s)
    {
        if (!Settings.Current.AdaptiveTheme) return;
        var brush = new ImageBrush(s.Image) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        _adaptiveBrush = brush;
        if (!LiveBlurOn) Bar.Background = brush; // with live blur the sample only drives light/dark
        // Hysteresis so a mid-grey window doesn't make the dock flicker between light and dark.
        bool light = _adaptiveLight switch
        {
            null => s.Luminance > 0.55,
            true => s.Luminance > 0.42,
            false => s.Luminance > 0.62,
        };
        if (light == _adaptiveLight) return;
        _adaptiveLight = light;
        AdaptiveResources = ThemeManager.Variant(light);
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(AdaptiveResources);
        Log.Info($"Dock {(IsPrimary ? "primary" : _device)} adapts to a {(light ? "light" : "dark")} screen (L={s.Luminance:0.00})");
    }

    // ------------------------------------------------------------------ peek / show desktop corner

    System.Windows.Threading.DispatcherTimer? _peekTimer;
    bool _peeking;

    void SetupDesktopCorner()
    {
        DesktopCorner.MouseEnter += (_, _) =>
        {
            _peekTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
            _peekTimer.Tick -= OnPeekTimer;
            _peekTimer.Tick += OnPeekTimer;
            _peekTimer.Start();
            DesktopCorner.Background = (Brush)FindResource("Tile.Hover");
        };
        DesktopCorner.MouseLeave += (_, _) => { _peekTimer?.Stop(); EndPeek(); DesktopCorner.Background = Brushes.Transparent; };
        DesktopCorner.MouseLeftButtonUp += (_, e) => { e.Handled = true; _peekTimer?.Stop(); EndPeek(); WinActions.ShowDesktop(); };
        DesktopCorner.ContextMenuOpening += (_, e) =>
        {
            e.Handled = true;
            var m = new ContextMenu();
            m.Items.Add(MI("Show desktop", WinActions.ShowDesktop));
            m.Items.Add(MI("Hide this corner", () => { Settings.Current.DesktopCorner = false; Settings.Save("dock"); }));
            m.PlacementTarget = DesktopCorner;
            m.IsOpen = true;
        };
    }

    void OnPeekTimer(object? s, EventArgs e)
    {
        _peekTimer?.Stop();
        if (!DesktopCorner.IsMouseOver) return;
        _peeking = true;
        WinActions.Peek(true, Hwnd);
    }

    void EndPeek()
    {
        if (!_peeking) return;
        _peeking = false;
        WinActions.Peek(false, Hwnd);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(Hwnd).AddHook(WndProc);
        SyncHotkeys();
        _appBar = new AppBar(Hwnd);
        _appBar.Register();
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        Svc.Windows.ForegroundChanged += CheckFullscreen;
        if (IsPrimary)
        {
            Svc.Windows.Start(Hwnd);
            Svc.Taskbar.Apply(Settings.Current.TaskbarMode);
            Svc.Ticker.Tick += OnTick;
        }

        BuildWidgets();
        ApplyLook();
        SetupAdaptive();
        SetupAutoHide();
        if (IsPrimary) Dispatcher.BeginInvoke(SyncDisplays, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    // ------------------------------------------------------------------ displays

    /// <summary>This dock's display (falls back to the primary if it was unplugged).</summary>
    internal MONITORINFO MonitorInfo(out IntPtr hmon)
    {
        if (_device != null)
            foreach (var m in AllMonitors())
                if (m.Device == _device) { hmon = m.Handle; return m.Info; }
        return GetPrimaryMonitor(out hmon);
    }

    /// <summary>Primary dock only: open a dock on every other display (or close them), per Settings.Displays.</summary>
    public void SyncDisplays()
    {
        if (!IsPrimary) return;
        var want = Settings.Current.Displays == "all"
            ? AllMonitors().Where(m => !m.Primary).Select(m => m.Device).ToHashSet()
            : new HashSet<string>();
        foreach (var d in All.Where(x => !x.IsPrimary && !want.Contains(x._device!)).ToList())
        {
            Log.Info("Closing dock on " + d._device);
            d.Teardown();
            d.Close();
        }
        foreach (var dev in want.Where(dev => All.All(x => x._device != dev)).ToList())
        {
            Log.Info("Opening dock on " + dev);
            try { new DockWindow(dev).Show(); } catch (Exception ex) { Log.Error("Dock on " + dev, ex); }
        }
    }

    // ------------------------------------------------------------------ layout & position

    void ApplyLook()
    {
        if (_adaptive != null) { _adaptiveLight = null; PokeAdaptive(); } // theme rebuilt: re-pick the light/dark variant
        double s = ThemeManager.SizeScale;
        _sizeScale.ScaleX = _sizeScale.ScaleY = s;
        RegionScale = s;
        Sheen.Visibility = ThemeManager.Current.Sheen.A > 0 || ThemeManager.Liquid ? Visibility.Visible : Visibility.Collapsed;
        Rim.Visibility = ThemeManager.Liquid ? Visibility.Visible : Visibility.Collapsed;
        DesktopCorner.Visibility = Settings.Current.DesktopCorner ? Visibility.Visible : Visibility.Collapsed;
        ApplySpacing();
        QueueReposition();
        UpdateRegion();
        UpdateGlass();
    }

    /// <summary>Line the blurred wallpaper slice up with the bar's current screen position.</summary>
    /// <summary>Live blur is on: the real blur comes from <see cref="BackdropWindow"/> under the bar.</summary>
    static bool LiveBlurOn => Settings.Current.LiveBlur && ThemeManager.GlassOn;

    void UpdateGlass()
    {
        if (Settings.Current.AdaptiveTheme) PokeAdaptive();                 // re-sample (light/dark) after any move / resize
        // Liquid Glass: refract what is behind the bar on the GPU: the wallpaper, plus a live capture of any window there.
        string why = !Settings.Current.LiquidRefraction ? "off in settings" : ThemeManager.Current.Id != "glass" ? "style is not Liquid Glass" : !ThemeManager.GlassOn ? "glass off"
            : Hwnd == IntPtr.Zero || !Bar.IsLoaded ? "not ready" : _fullscreenHidden ? "fullscreen" : !LiquidGlass.Available ? "no shader"
            : Vela.Services.GameMode.Active ? "game mode" : "on";
        var behind = why == "on" ? WindowsBehindBar() : new List<IntPtr>();
        if (why == "on" && behind.Count > 0 && !WindowCapture.Supported) why = "window behind the bar, no capture API";
        if (why == "on" && behind.Any(_noCapture.Contains)) why = "window behind the bar can't be captured";
        if (why != _lensWhy) { _lensWhy = why; Log.Info($"Liquid Glass ({(IsPrimary ? "primary" : "secondary")}): {why}"); }
        if (why == "on") { ShowLens(); SyncCaptures(behind); return; }
        HideLens();
        if (LiveBlurOn) { Bar.Background = null; SyncBackdrop(); return; }
        _backdrop?.HideBackdrop();
        if (Settings.Current.AdaptiveTheme && _adaptiveBrush != null) { Bar.Background = _adaptiveBrush; return; }
        if (Hwnd == IntPtr.Zero || !Bar.IsLoaded || !ThemeManager.GlassOn)
        {
            Bar.Background = null;
            return;
        }
        try
        {
            var tl = Bar.PointToScreen(new Point(0, 0));
            var br = Bar.PointToScreen(new Point(Bar.ActualWidth, Bar.ActualHeight));
            var m = MonitorInfo(out _).rcMonitor;
            Bar.Background = WallpaperGlass.BrushFor(new Rect(tl, br), new Rect(m.Left, m.Top, m.Width, m.Height));
        }
        catch (InvalidOperationException) { /* not yet connected to a presentation source */ }
    }

    // ------------------------------------------------------------------ Liquid Glass lens

    LiquidGlassEffect? _lensFx;
    System.Windows.Shapes.Rectangle? _lensWall; // the wallpaper strip, bottom layer of the scene the lens bends
    double _lensBleed;                          // DIPs the scene overhangs the bar (blur radius + a little)
    bool _lensActive;
    int _lensRequest;
    string? _lensWhy, _lensBlocker;
    System.Windows.Threading.DispatcherTimer? _lensDebounce;

    /// <summary>Liquid Glass style, refraction on (Settings), and a GPU that runs the shader.</summary>
    bool LensWanted => Settings.Current.LiquidRefraction && ThemeManager.Current.Id == "glass" && ThemeManager.GlassOn
                       && Hwnd != IntPtr.Zero && Bar.IsLoaded && !_fullscreenHidden && LiquidGlass.Available;

    /// <summary>
    /// Real windows behind the bar, top of the z-order first (at most 3; the dock reserves its strip, so usually none).
    /// Click-through and always-on-top layered overlays (screen-split helpers, OSDs) are left out: they're mostly
    /// transparent, and capturing them would hide the window that's really there.
    /// </summary>
    List<IntPtr> WindowsBehindBar()
    {
        var found = new List<IntPtr>();
        RECT bar;
        try
        {
            var tl = Bar.PointToScreen(new Point(0, 0));
            var br = Bar.PointToScreen(new Point(Bar.ActualWidth, Bar.ActualHeight));
            bar = new RECT { Left = (int)tl.X, Top = (int)tl.Y, Right = (int)br.X, Bottom = (int)br.Y };
        }
        catch (InvalidOperationException) { return found; }
        uint own = (uint)Environment.ProcessId;
        _noCapture.RemoveWhere(h => !IsWindow(h));
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == own) return true;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            long ex = GetExStyle(h);
            if ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & 0x20 /*WS_EX_TRANSPARENT*/) != 0) return true;
            if ((ex & 0x80000 /*WS_EX_LAYERED*/) != 0 && (ex & WS_EX_TOPMOST) != 0) return true;
            if (GetClassNameOf(h) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            // Visible frame (no invisible resize borders, which make maximized windows overlap by a few px).
            if (DwmGetWindowAttribute(h, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out RECT r, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(h, out r)) return true;
            int ix = Math.Min(r.Right, bar.Right) - Math.Max(r.Left, bar.Left), iy = Math.Min(r.Bottom, bar.Bottom) - Math.Max(r.Top, bar.Top);
            if (ix > 8 && iy > 4)
            {
                found.Add(h);
                // A window that covers the whole bar hides everything below it.
                if (r.Left <= bar.Left && r.Right >= bar.Right && r.Top <= bar.Top && r.Bottom >= bar.Bottom) return false;
            }
            return found.Count < 3;
        }, IntPtr.Zero);
        var who = string.Join(", ", found.Select(h =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            string proc = "";
            try { using var p = Process.GetProcessById((int)pid); proc = p.ProcessName; } catch { }
            return $"{GetClassNameOf(h)} ({proc})";
        }));
        if (who != _lensBlocker) { _lensBlocker = who; if (who.Length > 0) Log.Info($"Liquid Glass: behind the bar: {who}"); }
        return found;
    }

    // ---------- live window capture under the lens ----------

    sealed class LensCapture
    {
        public required WindowCapture Capture;
        public required System.Windows.Controls.Image Image;
        public System.Windows.Media.Imaging.WriteableBitmap? Bitmap;
        public (byte[] px, int w, int h, RECT at)? Pending; // newest frame not yet drawn (guarded by lock on this)
    }
    readonly Dictionary<IntPtr, LensCapture> _captures = new();
    readonly HashSet<IntPtr> _noCapture = new();

    /// <summary>Capture exactly the windows behind the bar (Windows.Graphics.Capture), stacked in z-order.</summary>
    void SyncCaptures(List<IntPtr> behind)
    {
        foreach (var h in _captures.Keys.Except(behind).ToList()) DropCapture(h);
        RECT crop;
        try
        {
            var tl = Bar.PointToScreen(new Point(0, 0));
            var br = Bar.PointToScreen(new Point(Bar.ActualWidth, Bar.ActualHeight));
            int b = (int)Math.Ceiling(_lensBleed * VisualTreeHelper.GetDpi(this).DpiScaleX); // the blur needs pixels past the edge
            crop = new RECT { Left = (int)Math.Floor(tl.X) - b, Top = (int)Math.Floor(tl.Y) - b, Right = (int)Math.Ceiling(br.X) + b, Bottom = (int)Math.Ceiling(br.Y) + b };
        }
        catch (InvalidOperationException) { return; }
        foreach (var h in behind)
        {
            if (_captures.TryGetValue(h, out var existing)) { existing.Capture.Crop = crop; continue; }
            try
            {
                var cap = new WindowCapture(h) { Crop = crop };
                var lc = new LensCapture { Capture = cap, Image = new System.Windows.Controls.Image { Stretch = Stretch.Fill, IsHitTestVisible = false } };
                RenderOptions.SetBitmapScalingMode(lc.Image, BitmapScalingMode.NearestNeighbor);
                cap.Frame += (px, w, h2, at) => QueueCaptureFrame(lc, px, w, h2, at);
                _captures[h] = lc;
                Log.Info($"Liquid Glass: capturing {GetClassNameOf(h)} behind the {(IsPrimary ? "primary" : "secondary")} dock");
            }
            catch (Exception ex)
            {
                _noCapture.Add(h);
                Log.Warn($"Liquid Glass: can't capture {GetClassNameOf(h)} ({ex.Message}); using the live blur while it's there");
                Dispatcher.BeginInvoke(UpdateGlass);
                return;
            }
        }
        // Wallpaper first, then the windows from the bottom of the z-order up, so the topmost is drawn last.
        LensWindows.Children.Clear();
        if (_lensWall != null) LensWindows.Children.Add(_lensWall);
        for (int i = behind.Count - 1; i >= 0; i--)
            if (_captures.TryGetValue(behind[i], out var lc)) LensWindows.Children.Add(lc.Image);
    }

    void QueueCaptureFrame(LensCapture lc, byte[] px, int w, int h, RECT at)
    {
        bool schedule;
        lock (lc)
        {
            if (lc.Pending is { } old) System.Buffers.ArrayPool<byte>.Shared.Return(old.px);
            schedule = lc.Pending == null;
            lc.Pending = (px, w, h, at);
        }
        if (schedule) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () => DrawCaptureFrame(lc));
    }

    void DrawCaptureFrame(LensCapture lc)
    {
        (byte[] px, int w, int h, RECT at) f;
        lock (lc) { if (lc.Pending is not { } p) return; f = p; lc.Pending = null; }
        try
        {
            if (!_captures.ContainsValue(lc) || !_lensActive) return;
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (lc.Bitmap == null || lc.Bitmap.PixelWidth != f.w || lc.Bitmap.PixelHeight != f.h)
            {
                lc.Bitmap = new System.Windows.Media.Imaging.WriteableBitmap(f.w, f.h, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32, null);
                lc.Image.Source = lc.Bitmap;
                lc.Image.Width = f.w / dpi;
                lc.Image.Height = f.h / dpi;
            }
            lc.Bitmap.WritePixels(new Int32Rect(0, 0, f.w, f.h), f.px, f.w * 4, 0);
            var origin = Bar.PointToScreen(new Point(0, 0)); // the scene canvas starts one bleed above-left of the bar
            Canvas.SetLeft(lc.Image, (f.at.Left - origin.X) / dpi + _lensBleed);
            Canvas.SetTop(lc.Image, (f.at.Top - origin.Y) / dpi + _lensBleed);
        }
        catch (InvalidOperationException) { }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(f.px); }
    }

    void DropCapture(IntPtr h)
    {
        if (!_captures.Remove(h, out var lc)) return;
        lc.Capture.Dispose();
        LensWindows.Children.Remove(lc.Image);
        lock (lc) { if (lc.Pending is { } old) { System.Buffers.ArrayPool<byte>.Shared.Return(old.px); lc.Pending = null; } }
    }

    void DropCaptures()
    {
        foreach (var h in _captures.Keys.ToList()) DropCapture(h);
    }

    void ShowLens()
    {
        _backdrop?.HideBackdrop();
        Bar.Background = null;
        Rect lens, monitor;
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        try
        {
            var tl = Bar.PointToScreen(new Point(0, 0));
            var br = Bar.PointToScreen(new Point(Bar.ActualWidth, Bar.ActualHeight));
            lens = new Rect(tl, br);
            var m = MonitorInfo(out _).rcMonitor;
            monitor = new Rect(m.Left, m.Top, m.Width, m.Height);
        }
        catch (InvalidOperationException) { return; }
        if (lens.Width < 4 || lens.Height < 4) return;
        // What the lens bends is the scene behind the bar, blurred on the GPU first (frosted body). The scene canvas
        // overhangs the bar by the blur radius so the blur has real pixels at the edges instead of fading to black.
        // Frost: Gaussian sigma 0.6–11.6 DIP from Settings → Blur strength (WacOS / macOS 26 default ≈ 6 DIP); a WPF BlurEffect
        // radius is about three sigmas.
        double blur = 3 * (0.6 + Math.Clamp(Settings.Current.GlassBlur, 0, 100) / 100 * 11);
        _lensBleed = Math.Ceiling(blur + 2);
        double bleedPx = _lensBleed * dpi;
        // The whole width of the monitor at the bar's height: width changes (magnification, widgets) reuse the slice.
        double top = Math.Max(monitor.Top, Math.Floor(lens.Top - bleedPx)), bottom = Math.Min(monitor.Bottom, Math.Ceiling(lens.Bottom + bleedPx));
        var strip = new Rect(monitor.Left, top, monitor.Width, bottom - top);
        int request = ++_lensRequest;
        WallpaperGlass.SliceAsync(strip, monitor).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            if (request != _lensRequest) return;
            if (t.IsFaulted || t.Result == null)
            {
                Log.Info($"Liquid Glass: no wallpaper strip ({t.Exception?.GetBaseException().Message ?? "no wallpaper file"}); live blur instead");
                _lensRequest++; HideLens(); if (LiveBlurOn) SyncBackdrop(); return;
            }
            double sceneLeft = lens.Left - bleedPx, sceneTop = lens.Top - bleedPx, sceneW = lens.Width + 2 * bleedPx;
            var brush = new ImageBrush(t.Result)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewbox = new Rect((sceneLeft - strip.Left) / strip.Width, 0, sceneW / strip.Width, 1),
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            brush.Freeze();
            _lensWall ??= new System.Windows.Shapes.Rectangle { IsHitTestVisible = false };
            _lensWall.Fill = brush;
            _lensWall.Width = sceneW / dpi;
            _lensWall.Height = strip.Height / dpi;
            Canvas.SetLeft(_lensWall, 0);
            Canvas.SetTop(_lensWall, (strip.Top - sceneTop) / dpi);
            if (!LensWindows.Children.Contains(_lensWall)) LensWindows.Children.Insert(0, _lensWall);
            LensWindows.Margin = new Thickness(-_lensBleed);
            LensWindows.Effect = blur < 0.5 ? null : new System.Windows.Media.Effects.BlurEffect
            {
                Radius = blur, KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
            };
            Lens.Background = null;
            _lensFx ??= new LiquidGlassEffect();
            double w = Bar.ActualWidth * dpi, h = Bar.ActualHeight * dpi, radius = BarRadius * ThemeManager.SizeScale * dpi;
            bool light = ThemeManager.Current.IsLight || (Settings.Current.AdaptiveTheme && _adaptiveLight == true);
            double side = Math.Min(w, h);                                       // calibrated on macOS 26 (see LiquidGlass.cs)
            double bezel = Math.Clamp(side * 0.25, 4 * dpi, 20 * dpi), pullMax = Math.Min(side * 0.8, 60 * dpi);
            _lensFx.Geometry = new System.Windows.Media.Media3D.Point4D(w, h, radius, bezel);
            _lensFx.Lens = new System.Windows.Media.Media3D.Point4D(pullMax, 0.10, 1.8, 0.05);
            _lensFx.Light = new System.Windows.Media.Media3D.Point4D(0, -1, light ? 0.85 : 0.8, Math.Clamp(side * 0.08, 2 * dpi, 6 * dpi));
            _lensFx.More = new System.Windows.Media.Media3D.Point4D(dpi, 0.025, 0, 0);
            _lensFx.Tint = light ? new System.Windows.Media.Media3D.Point4D(1, 1, 1, 0.22) : new System.Windows.Media.Media3D.Point4D(0.03, 0.04, 0.06, 0.24);
            Lens.Effect = _lensFx;
            Lens.Visibility = Visibility.Visible;
            // The lens is the glass now: the flat tint, sheen and rim drawn for the blur would only muddy it.
            Tint.Opacity = 0;
            Sheen.Visibility = Visibility.Collapsed;
            Rim.Visibility = Visibility.Collapsed;
            if (!_lensActive) Log.Info($"Liquid Glass lens on ({(IsPrimary ? "primary" : "secondary")} dock, {t.Result.PixelWidth}×{t.Result.PixelHeight} wallpaper strip)");
            _lensActive = true;
        }));
    }

    void HideLens()
    {
        DropCaptures();
        if (!_lensActive && Lens.Visibility == Visibility.Collapsed) return;
        Log.Info($"Liquid Glass lens off ({(IsPrimary ? "primary" : "secondary")} dock): {(LensWanted ? "a window is behind the bar" : "not wanted")}");
        _lensRequest++;
        _lensActive = false;
        Lens.Visibility = Visibility.Collapsed;
        LensWindows.Effect = null;
        if (_lensWall != null) { LensWindows.Children.Remove(_lensWall); _lensWall = null; }
        Lens.Effect = null;
        Lens.Background = null;
        Tint.Opacity = 1;
        Sheen.Visibility = Visibility.Visible;
        Rim.Visibility = Visibility.Visible;
    }

    /// <summary>Windows moved or the front window changed: re-check whether anything is behind the bar (debounced).</summary>
    void OnScreenMovedForLens()
    {
        if (!LensWanted && !_lensActive) return;
        _lensDebounce ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(150), System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => { _lensDebounce!.Stop(); UpdateGlass(); }, Dispatcher);
        _lensDebounce.Stop();
        _lensDebounce.Start();
    }

    void ApplySpacing()
    {
        double half = Settings.Current.Spacing / 2;
        foreach (FrameworkElement c in Items.Children) c.Margin = new Thickness(half, 0, half, 0);
        Items.Margin = new Thickness(-half, 0, -half, 0);
    }

    void OnSettingChanged(string what)
    {
        switch (what)
        {
            case "theme": case "size": case "accent": ThemeManager.Apply(); break;
            case "glass": WallpaperGlass.Reload(); ThemeManager.Apply(); break;
            case "liveblur": ThemeManager.Apply(); UpdateGlass(); break;
            case "adaptive": ThemeManager.Apply(); SetupAdaptive(); break;
            case "autohide": QueueReposition(); SetupAutoHide(); break;
            case "island": if (IsPrimary) IslandWindow.Sync(); break;
            case "dock": ApplyLook(); ThemeManager.ApplyEdge(); SyncHotkeys(); break;
            case "widgets": if (_origin != this && _origin != null || _origin == null && !IsPrimary) BuildWidgets(); break;
            case "displays": SyncDisplays(); break;
            case "taskbar": Svc.Taskbar.Apply(Settings.Current.TaskbarMode); QueueReposition(); break;
        }
    }

    public void QueueReposition()
    {
        if (_repositionQueued) return;
        _repositionQueued = true;
        Dispatcher.BeginInvoke(() => { _repositionQueued = false; Reposition(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    void Reposition()
    {
        if (Hwnd == IntPtr.Zero || _appBar == null) return;
        var mi = MonitorInfo(out var hmon);
        double dpi = GetMonitorScale(hmon);
        var s = Settings.Current;
        bool top = s.Edge == "top";
        double margin = Math.Max(0, s.EdgeMargin);
        double hairline = 1 / dpi;
        double barH = (WidgetBase.TileHeight + 2 * BarPadding) * ThemeManager.SizeScale + 2 * hairline;
        int hPx = (int)Math.Ceiling((barH + margin + TopGap) * dpi);   // reserved from other windows
        int winPx = hPx + (int)Math.Ceiling(Headroom * dpi);              // window = reservation + magnify headroom

        var rect = mi.rcMonitor;
        if (top) rect.Bottom = rect.Top + hPx; else rect.Top = rect.Bottom - hPx;
        // Auto-hide gives the screen space back (the dock floats over windows and slides away).
        if (s.ReserveSpace && s.AutoHide == "off") rect = _appBar.Reserve(mi.rcMonitor, top, hPx);
        else _appBar.Release(mi.rcMonitor, top);

        // Full width: the bar runs edge to edge with the same small inset on the sides as from the screen edge.
        bool full = s.BarWidth != "fit";
        double side = full ? Math.Max(4, margin) : 0;
        BarHost.HorizontalAlignment = full ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        BarHost.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        BarHost.Margin = top ? new Thickness(side, margin, side, 0) : new Thickness(side, 0, side, margin);
        Shadow.Margin = top ? new Thickness(1, -1, 1, 1) : new Thickness(1, 1, 1, -1);
        SetWindowPos(Hwnd, HWND_TOPMOST, mi.rcMonitor.Left, top ? mi.rcMonitor.Top : mi.rcMonitor.Bottom - winPx,
            mi.rcMonitor.Width, winPx, SWP_NOACTIVATE);
        UpdateRegion();
        Dispatcher.BeginInvoke(UpdateGlass, System.Windows.Threading.DispatcherPriority.Loaded);
        // Windows that were maximized while the taskbar was hidden still cover the whole screen; fit them
        // to the new work area so the dock never sits on top of an app's own UI.
        if (s.ReserveSpace && s.AutoHide == "off") Dispatcher.BeginInvoke(() => WindowTracker.FitMaximizedToWorkArea(Hwnd), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    // ------------------------------------------------------------------ widgets

    void BuildWidgets()
    {
        foreach (var w in Items.Children.OfType<WidgetBase>().ToList()) w.Dispose();
        Items.Children.Clear();
        foreach (var inst in Settings.Current.Widgets.ToList()) AddControl(inst);
        EnsurePlaceholder();
        ApplySpacing();
    }

    WidgetBase? AddControl(WidgetInstance inst)
    {
        var w = WidgetRegistry.Create(inst);
        if (w == null) return null;
        w.RemoveRequested += RemoveWidget;
        Items.Children.Add(w);
        return w;
    }

    void EnsurePlaceholder()
    {
        var existing = Items.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == "placeholder");
        bool empty = !Items.Children.OfType<WidgetBase>().Any();
        if (empty && existing == null)
        {
            var b = UI.UI.PillButton("+  Add widgets", OpenWidgetPicker);
            b.Tag = "placeholder";
            b.Height = WidgetBase.TileHeight;
            Items.Children.Add(b);
        }
        else if (!empty && existing != null) Items.Children.Remove(existing);
    }

    public bool HasWidget(string type) => Settings.Current.Widgets.Any(w => w.Type == type);
    public int WidgetCount => Settings.Current.Widgets.Count;

    public void AddWidget(string type)
    {
        var inst = new WidgetInstance { Type = type };
        Settings.Current.Widgets.Add(inst);
        AddControl(inst);
        EnsurePlaceholder();
        ApplySpacing();
        SaveWidgets();
        WidgetsChanged?.Invoke();
    }

    public void RemoveWidgetType(string type)
    {
        var w = Items.Children.OfType<WidgetBase>().LastOrDefault(x => x.Instance.Type == type);
        if (w != null) RemoveWidget(w);
    }

    void RemoveWidget(WidgetBase w)
    {
        if (FlyoutWindow.Current?.Anchor == w) FlyoutWindow.Current.CloseNow();
        Items.Children.Remove(w);
        w.Dispose();
        Settings.Current.Widgets.Remove(w.Instance);
        EnsurePlaceholder();
        SaveWidgets();
        WidgetsChanged?.Invoke();
    }

    /// <summary>Control-panel helpers: move / remove a widget by its instance.</summary>
    public void MoveWidget(WidgetInstance inst, int delta)
    {
        var w = Items.Children.OfType<WidgetBase>().FirstOrDefault(x => x.Instance == inst);
        if (w == null) return;
        int i = Items.Children.IndexOf(w), j = Math.Clamp(i + delta, 0, Items.Children.Count - 1);
        if (i == j) return;
        Items.Children.RemoveAt(i);
        Items.Children.Insert(j, w);
        PersistOrder();
    }

    public void RemoveInstance(WidgetInstance inst)
    {
        var w = Items.Children.OfType<WidgetBase>().FirstOrDefault(x => x.Instance == inst);
        if (w != null) RemoveWidget(w);
    }

    // ------------------------------------------------------------------ stacks

    /// <summary>Merge a widget with the one to its right into a stack (or add it to that stack).</summary>
    public void StackWithNext(WidgetBase w)
    {
        int i = Items.Children.IndexOf(w);
        if (i < 0 || i + 1 >= Items.Children.Count || Items.Children[i + 1] is not WidgetBase next) return;
        if (next is StackWidget existing)
        {
            Items.Children.Remove(w);
            Settings.Current.Widgets.Remove(w.Instance);
            w.Dispose();
            existing.Append(w.Instance);
            SaveWidgets();
            return;
        }
        var items = new List<WidgetInstance> { w.Instance, next.Instance };
        if (w is StackWidget s) { items = s.Children_; items.Add(next.Instance); }
        var inst = new WidgetInstance { Type = "stack" };
        inst.Config["items"] = System.Text.Json.JsonSerializer.Serialize(items, Json.Options);
        ReplaceWidgets(i, 2, new[] { inst });
    }

    /// <summary>Put a stack's widgets back on the dock side by side.</summary>
    public void Unstack(StackWidget s)
    {
        int i = Items.Children.IndexOf(s);
        if (i < 0) return;
        ReplaceWidgets(i, 1, s.Children_);
    }

    void ReplaceWidgets(int index, int count, IEnumerable<WidgetInstance> with)
    {
        FlyoutWindow.CloseCurrent();
        for (int k = 0; k < count; k++)
        {
            var old = (WidgetBase)Items.Children[index];
            Items.Children.RemoveAt(index);
            Settings.Current.Widgets.Remove(old.Instance);
            old.Dispose();
        }
        int at = index;
        foreach (var inst in with)
        {
            var w = WidgetRegistry.Create(inst);
            if (w == null) continue;
            w.RemoveRequested += RemoveWidget;
            Items.Children.Insert(at++, w);
        }
        ApplySpacing();
        PersistOrder();
        WidgetsChanged?.Invoke();
    }

    /// <summary>Save the layout; other docks (other displays) rebuild from it.</summary>
    void SaveWidgets()
    {
        _origin = this;
        try { Settings.Save("widgets"); } finally { _origin = null; }
    }

    void PersistOrder()
    {
        Settings.Current.Widgets = Items.Children.OfType<WidgetBase>().Select(w => w.Instance).ToList();
        SaveWidgets();
    }

    static WidgetBase? FindWidget(DependencyObject? d)
    {
        while (d != null && d is not WidgetBase) d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        return d as WidgetBase;
    }

    // ------------------------------------------------------------------ reorder (edit mode only)

    // Drag any widget sideways to move it (always, not only while the picker is open). A press only
    // becomes a drag after the pointer travels a few DIPs, so clicks still work; sliders, text boxes
    // and app icons (which reorder inside the apps strip) are excluded.
    WidgetBase? _pressCandidate;
    bool _reordering;
    const double DragThreshold = 7;

    static bool IsInteractive(DependencyObject? d)
    {
        while (d != null && d is not WidgetBase)
        {
            if (d is System.Windows.Controls.Primitives.Thumb or Slider or System.Windows.Controls.Primitives.TextBoxBase or PasswordBox
                or System.Windows.Controls.Primitives.ScrollBar or AppItem) return true;
            d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    void OnItemsPreviewDown(object sender, MouseButtonEventArgs e)
    {
        var w = FindWidget(e.OriginalSource as DependencyObject);
        if (w == null || w.Parent != Items) return;
        bool interactive = IsInteractive(e.OriginalSource as DependencyObject);
        if (interactive && !(WidgetBase.EditMode && e.OriginalSource is not (System.Windows.Controls.Primitives.Thumb or Slider))) return;
        _pressCandidate = w;
        _dragStart = e.GetPosition(Items);
        if (WidgetBase.EditMode) { BeginReorder(); e.Handled = true; }
    }

    Vela.UI.LiveReorder? _live;

    void BeginReorder()
    {
        if (_pressCandidate == null) return;
        _reorderItem = _pressCandidate;
        _reordering = true;
        FlyoutWindow.CloseCurrent();
        _live = new Vela.UI.LiveReorder(Items, _reorderItem, _dragStart);
        Items.CaptureMouse();
    }

    void OnItemsPreviewMove(object sender, MouseEventArgs e)
    {
        if (!_reordering && _pressCandidate != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var q = e.GetPosition(Items);
            if (Math.Abs(q.X - _dragStart.X) > DragThreshold && !Settings.Current.LockDock) BeginReorder();
        }
        if (_reorderItem == null || _live == null) return;
        // Live: the widget follows the pointer, the others glide out of its way.
        _live.Move(e.GetPosition(Items));
    }

    void OnItemsPreviewUp(object sender, MouseButtonEventArgs e)
    {
        _pressCandidate = null;
        if (_reorderItem == null) return;
        Items.ReleaseMouseCapture();
        _live?.End();
        _live = null;
        _reorderItem = null;
        _reordering = false;
        PersistOrder();
        e.Handled = true;
    }

    // ------------------------------------------------------------------ drag the bar to the other edge

    void OnBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (WidgetBase.EditMode || Settings.Current.LockDock || FindWidget(e.OriginalSource as DependencyObject) != null) return;
        if (e.OriginalSource is not (Border or StackPanel or Grid or DockRow)) return;
        _barDragging = true;
        _barDragMoved = false;
        _dragStart = PointToScreen(e.GetPosition(this));
        Bar.CaptureMouse();
    }

    void OnBarMouseMove(object sender, MouseEventArgs e)
    {
        if (!_barDragging) return;
        var p = PointToScreen(e.GetPosition(this));
        if (Math.Abs(p.Y - _dragStart.Y) > 24 && !_barDragMoved) { _barDragMoved = true; Bar.Opacity = 0.75; }
    }

    void OnBarMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_barDragging) return;
        _barDragging = false;
        Bar.ReleaseMouseCapture();
        Bar.Opacity = 1;
        if (!_barDragMoved) return;
        GetCursorPos(out var pt);
        var mi = MonitorInfo(out _);
        var edge = pt.Y < (mi.rcMonitor.Top + mi.rcMonitor.Bottom) / 2 ? "top" : "bottom";
        if (edge != Settings.Current.Edge) { Settings.Current.Edge = edge; Settings.Save("dock"); }
    }

    // ------------------------------------------------------------------ menus & windows

    ContextMenu BuildBarMenu()
    {
        var m = new ContextMenu();
        m.Items.Add(MI("Widgets…", OpenWidgetPicker));
        m.Items.Add(MI("Settings", OpenSettings));
        m.Items.Add(new Separator());
        var style = new MenuItem { Header = "Style" };
        foreach (var p in Palettes.All)
            style.Items.Add(MI(p.Name, () => { Settings.Current.Theme = p.Id; Settings.Save("theme"); }, Settings.Current.Theme == p.Id));
        m.Items.Add(style);
        var size = new MenuItem { Header = "Size" };
        foreach (var (id, name) in new[] { ("S", "Small"), ("M", "Medium"), ("L", "Large") })
            size.Items.Add(MI(name, () => { Settings.Current.Size = id; Settings.Save("size"); }, Settings.Current.Size == id));
        m.Items.Add(size);
        bool top = Settings.Current.Edge == "top";
        m.Items.Add(MI(top ? "Move to bottom" : "Move to top", () => { Settings.Current.Edge = top ? "bottom" : "top"; Settings.Save("dock"); }));
        bool full = Settings.Current.BarWidth != "fit";
        m.Items.Add(MI("Full width", () => { Settings.Current.BarWidth = full ? "fit" : "full"; Settings.Save("dock"); }, full));
        m.Items.Add(new Separator());
        m.Items.Add(MI("Task Manager", WinActions.OpenTaskManager));
        m.Items.Add(MI("Restart Vela", Restart));
        m.Items.Add(MI("Quit Vela", () => Application.Current.Shutdown()));
        return m;
    }

    static MenuItem MI(string header, Action a, bool isChecked = false)
    {
        var mi = new MenuItem { Header = header, IsChecked = isChecked };
        mi.Click += (_, _) => a();
        return mi;
    }

    static void Restart()
    {
        if (Environment.ProcessPath is { } exe)
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Arguments = "--wait-for-exit" });
        }
        Application.Current.Shutdown();
    }

    public void OpenWidgetPicker()
    {
        FlyoutWindow.CloseCurrent();
        if (_picker == null)
        {
            _picker = new WidgetPickerWindow(this);
            _picker.Closed += (_, _) => { _picker = null; WidgetBase.SetEditMode(false); WinActions.TrimWorkingSet(); };
            WidgetBase.SetEditMode(true);
            _picker.Show();
        }
        _picker.Activate();
    }

    public void OpenSettings()
    {
        FlyoutWindow.CloseCurrent();
        if (_settings == null)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => { _settings = null; WinActions.TrimWorkingSet(); };
            _settings.Show();
        }
        _settings.Activate();
    }

    // ------------------------------------------------------------------ fullscreen & system messages

    void OnTick(DateTime _)
    {
        if (++_enforceCounter >= 3) { _enforceCounter = 0; Svc.Taskbar.Enforce(); }
    }

    void CheckFullscreen()
    {
        bool fs = IsFullscreenActive();
        if (fs == _fullscreenHidden) return;
        _fullscreenHidden = fs;
        if (fs)
        {
            FlyoutWindow.CloseCurrent();
            ShowWindow(Hwnd, SW_HIDE);
            _backdrop?.HideBackdrop();
        }
        else
        {
            ShowWindow(Hwnd, SW_SHOWNOACTIVATE);
            SetWindowPos(Hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        if (!fs) SyncBackdrop();
        // Stop drawing and polling only when *every* display is fullscreen; a game on one screen
        // must not freeze the clock on the other.
        Svc.Ticker.Paused = All.All(d => d._fullscreenHidden);
        if (IsPrimary) PrimaryFullscreenChanged?.Invoke(fs);
    }

    /// <summary>
    /// Fullscreen on *this* display. The shell's notification state is global, so it only counts when the
    /// foreground window is on this dock's monitor (a game on display 2 must not hide display 1's dock).
    /// </summary>
    bool IsFullscreenActive()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Hwnd) return false;
        bool onMyDisplay = MonitorFromWindow(fg, 2) == MonitorFromWindow(Hwnd, 2);
        if (!onMyDisplay) return _fullscreenHidden; // focus moved to another display: keep this one as it is
        if (SHQueryUserNotificationState(out int state) == 0 && state is 3 or 4) return true; // D3D fullscreen / presentation
        bool busy = state == 2;
        if (busy && GetWindowRect(fg, out var br)) { var bm = MonitorInfo(out _).rcMonitor; if (br.Left <= bm.Left && br.Top <= bm.Top && br.Right >= bm.Right && br.Bottom >= bm.Bottom) return true; }
        var cls = GetClassNameOf(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        long style = GetWindowLongPtr(fg, GWL_STYLE).ToInt64();
        if ((style & 0x00C00000L) == 0x00C00000L) return false; // has a caption => normal (maybe maximized) window
        if (!GetWindowRect(fg, out var r)) return false;
        var mi = MonitorInfo(out _);
        var m = mi.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_appBar != null && msg == _appBar.CallbackMessage)
        {
            switch (wParam.ToInt32())
            {
                case ABN_POSCHANGED: QueueReposition(); break;
                case ABN_FULLSCREENAPP: Dispatcher.BeginInvoke(CheckFullscreen); break;
            }
        }
        else if (msg == Svc.Windows.ShellHookMessage && msg != 0)
        {
            Svc.Windows.HandleShellMessage(wParam, lParam);
        }
        else if (msg == _taskbarCreatedMsg && msg != 0 && DateTime.Now > TrayHost.SelfBroadcastUntil) // not our own tray host's broadcast
        {
            Log.Info("Explorer restarted; re-applying taskbar + appbar");
            if (IsPrimary) Svc.Taskbar.OnExplorerRestarted();
            _appBar?.Register();
            QueueReposition();
        }
        else if (msg == WM_HOTKEY && wParam.ToInt32() == ShellHotkeyId)
        {
            WinActions.LaunchShell(WinActions.ShellKind.PowerShell, admin: true);
            handled = true;
        }
        else if (msg == CommandMessage)
        {
            if (wParam.ToInt32() == 1) Dispatcher.BeginInvoke(OpenWidgetPicker);
            else if (wParam.ToInt32() == 2) Dispatcher.BeginInvoke(OpenSettings);
            else if (wParam.ToInt32() == 3 && Svc.TrayCreated) Dispatcher.BeginInvoke(Svc.Tray.LogIcons); // --tray-dump (diagnostics)
            handled = true;
        }
        else if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        else if (msg is WM_DISPLAYCHANGE or WM_DPICHANGED)
        {
            QueueReposition();
            if (IsPrimary && msg == WM_DISPLAYCHANGE) Dispatcher.BeginInvoke(SyncDisplays, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        else if (msg == WM_SETTINGCHANGE && wParam.ToInt32() == 0x0014 /*SPI_SETDESKWALLPAPER*/)
        {
            Dispatcher.BeginInvoke(WallpaperGlass.Reload);
        }
        return IntPtr.Zero;
    }

    // ------------------------------------------------------------------ global hotkeys

    const int ShellHotkeyId = 0x5654; // "VT"
    bool _shellHotkey;

    /// <summary>Alt+T: PowerShell as administrator, from anywhere (owner's choice). Only the main dock owns it.</summary>
    void SyncHotkeys()
    {
        if (!IsPrimary || Hwnd == IntPtr.Zero) return;
        bool want = Settings.Current.AdminShellHotkey;
        if (want == _shellHotkey) return;
        if (want)
        {
            _shellHotkey = RegisterHotKey(Hwnd, ShellHotkeyId, MOD_ALT | MOD_NOREPEAT, 0x54 /* T */);
            if (!_shellHotkey) Log.Warn("Alt+T is taken by another app; the admin PowerShell shortcut is off");
        }
        else { UnregisterHotKey(Hwnd, ShellHotkeyId); _shellHotkey = false; }
    }

    /// <summary>Called from App.OnExit. Order matters: give back the screen space, then restore the taskbar.</summary>
    public void Teardown()
    {
        try
        {
            Svc.Ticker.Tick -= OnTick;
            Svc.Windows.ForegroundChanged -= CheckFullscreen;
            if (IsPrimary)
            {
                foreach (var d in All.Where(x => !x.IsPrimary).ToList()) { d.Teardown(); d.Close(); }
                if (_shellHotkey) { UnregisterHotKey(Hwnd, ShellHotkeyId); _shellHotkey = false; }
                if (Hwnd != IntPtr.Zero) Svc.Windows.Stop(Hwnd);
            }
            _appBar?.Dispose();
            _appBar = null;
        }
        catch (Exception ex) { Log.Error("Dock teardown", ex); }
    }
}
