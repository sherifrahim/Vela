using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Vela.Interop;
using Vela.Theming;
using static Vela.Interop.Native;

namespace Vela.Views;

/// <summary>
/// Base for every Vela surface (dock, cards, picker, settings).
///
/// Rendering model (perf-critical, see docs/ARCHITECTURE.md#rendering):
///  - NOT a layered window (AllowsTransparency=false). Layered WPF windows force a GPU→CPU
///    readback every frame; instead we extend the DWM frame over the client area and render
///    with a transparent clear colour, so DWM composites our pixels with real alpha.
///  - The blur/acrylic comes from DWM's accent policy (no capture, no shaders, ~0 CPU).
///  - The window region is clipped to <see cref="RegionElement"/>'s rounded rect so the blur
///    has rounded corners and clicks outside the shape fall through to the desktop.
/// </summary>
public class GlassWindow : Window
{
    protected IntPtr Hwnd { get; private set; }
    public IntPtr WindowHandle => Hwnd;
    /// <summary>Element whose rounded bounds define the visible window shape. Null = whole window.</summary>
    public FrameworkElement? RegionElement { get; set; }
    /// <summary>Corner radius of the region in DIPs (before <see cref="RegionScale"/>).</summary>
    public double RegionRadius { get; set; } = 18;
    public double RegionScale { get; set; } = 1;
    protected bool NoActivate { get; set; }

    /// <summary>
    /// How the window gets per-pixel transparency. Override with env var VELA_RENDER=chrome|layered.
    ///  layered (default): AllowsTransparency=true. Works with the rounded window region on Win10/11.
    ///          WPF re-uploads the bitmap only when something changes (≈1 Hz here), so cost is tiny.
    ///  chrome  (experimental): WindowChrome glass frame. Renders BLACK once a window region is set,
    ///          because DWM drops the glass sheet for regioned windows. Kept for future work.

    /// </summary>
    public static readonly string RenderMode =
        Environment.GetEnvironmentVariable("VELA_RENDER") is { Length: > 0 } m ? m.ToLowerInvariant() : "layered";

    public GlassWindow()
    {
        ResizeMode = ResizeMode.NoResize;
        if (RenderMode == "layered")
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
        }
        else
        {
            // Keep a real (caption) frame so DWM treats the client as a glass sheet, but give
            // WPF the whole window: no caption, no resize border, glass over everything.
            WindowStyle = WindowStyle.SingleBorderWindow;
            AllowsTransparency = false;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
                NonClientFrameEdges = System.Windows.Shell.NonClientFrameEdges.None,
            });
        }
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        FontFamily = UI.UI.Font;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(Hwnd);
        if (RenderMode != "layered") src.CompositionTarget.BackgroundColor = Colors.Transparent;

        long ex = GetExStyle(Hwnd) | WS_EX_TOOLWINDOW;
        ex &= ~WS_EX_APPWINDOW;
        if (NoActivate) ex |= WS_EX_NOACTIVATE;
        SetExStyle(Hwnd, ex);

        if (RenderMode != "layered")
        {
            var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(Hwnd, ref m);
        }
        int noRound = 1; // DWMWCP_DONOTROUND: our region does the rounding
        DwmSetWindowAttribute(Hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(int));
        int noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(Hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
        int dark = ThemeManager.Current.IsLight ? 0 : 1;
        DwmSetWindowAttribute(Hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        ApplyBackdrop();
        if (RegionElement != null)
        {
            RegionElement.SizeChanged += (_, _) => UpdateRegion();
            RegionElement.Loaded += (_, _) => UpdateRegion();
            if (LiveBackdrop)
            {
                // Deferred: these fire inside layout / render passes, where creating a window can fail.
                void Later() => Dispatcher.BeginInvoke(SyncLiveBackdrop, System.Windows.Threading.DispatcherPriority.Background);
                RegionElement.SizeChanged += (_, _) => Later();
                RegionElement.Loaded += (_, _) => Later();
                LocationChanged += (_, _) => Later();
                IsVisibleChanged += (_, _) => Later();
                Closed += (_, _) => { try { _live?.Close(); } catch { } };
            }
        }
        SizeChanged += (_, _) => UpdateRegion();
    }

    void OnThemeChanged()
    {
        ApplyBackdrop();
        UpdateRegion();
    }

    protected virtual BackdropKind BackdropKind => ThemeManager.Current.Backdrop;

    public void ApplyBackdrop()
    {
        if (Hwnd == IntPtr.Zero) return;
        // Layered windows: DWM blur ignores the window region (it would fill the whole rect),
        // so it stays off; the dock draws its own glass (Theming/WallpaperGlass.cs).
        Backdrop.Apply(Hwnd, RenderMode == "layered" ? BackdropKind.None : BackdropKind, ThemeManager.Current.Bar);
    }

    public double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    bool _regionCleared;

    /// <summary>
    /// macOS-style frosted panel: a live-blur <see cref="BackdropWindow"/> under <see cref="RegionElement"/> (cards, picker,
    /// control panel) while Settings.LiveBlur is on; the panel itself paints a translucent Flyout.Background.
    /// </summary>
    protected bool LiveBackdrop { get; set; }
    BackdropWindow? _live;

    /// <summary>Re-place the blur under the panel (call after a slide animation settles).</summary>
    protected void SyncLiveBackdrop()
    {
        var el = RegionElement;
        if (!LiveBackdrop || !Core.Settings.Current.LiveBlur || el == null || !IsVisible || Hwnd == IntPtr.Zero || !el.IsLoaded || el.ActualWidth <= 0)
        { _live?.HideBackdrop(); return; }
        try
        {
            var tl = el.PointToScreen(new Point(0, 0));
            var br = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
            _live ??= new BackdropWindow(BackdropWindow.Best);
            _live.Place(new RECT { Left = (int)Math.Round(tl.X), Top = (int)Math.Round(tl.Y), Right = (int)Math.Round(br.X), Bottom = (int)Math.Round(br.Y) }, RegionRadius * DpiScale, Hwnd);
        }
        catch (InvalidOperationException) { }
    }

    protected void HideLiveBackdrop() => _live?.HideBackdrop();

    public void UpdateRegion()
    {
        if (Hwnd == IntPtr.Zero) return;
        var el = RegionElement;
        if (RenderMode == "layered")
        {
            // Per-pixel alpha already makes transparent pixels click-through, so no Win32 region:
            // regions are aliased (jagged corners). Clip the shape with an anti-aliased geometry instead.
            if (!_regionCleared) { SetWindowRgn(Hwnd, IntPtr.Zero, true); _regionCleared = true; }
            if (el != null && el.ActualWidth > 0)
            {
                double rad = RegionRadius;
                var clip = new RectangleGeometry(new Rect(0, 0, el.ActualWidth, el.ActualHeight), rad, rad);
                clip.Freeze();
                el.Clip = clip;
            }
            return;
        }
        Rect r;
        if (el == null || !el.IsLoaded || el.ActualWidth <= 0)
            r = new Rect(0, 0, ActualWidth, ActualHeight);
        else
        {
            try { r = el.TransformToAncestor(this).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight)); }
            catch (InvalidOperationException) { return; }
        }
        double s = DpiScale;
        int l = (int)Math.Floor(r.Left * s), t = (int)Math.Floor(r.Top * s);
        int rr = (int)Math.Ceiling(r.Right * s), b = (int)Math.Ceiling(r.Bottom * s);
        int d = (int)Math.Round(RegionRadius * RegionScale * s * 2);
        var rgn = CreateRoundRectRgn(l, t, rr + 1, b + 1, d, d);
        if (SetWindowRgn(Hwnd, rgn, true) == 0) DeleteObject(rgn);
    }
}
