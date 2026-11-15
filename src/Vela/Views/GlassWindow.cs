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

    public void UpdateRegion()
    {
        if (Hwnd == IntPtr.Zero) return;
        var el = RegionElement;
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
