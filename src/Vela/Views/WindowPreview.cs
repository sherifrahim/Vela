using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Vela.Core;
using Vela.Shell;
using static Vela.Interop.Native;
using static Vela.UI.UI;

namespace Vela.Views;

/// <summary>
/// Live window previews above an app icon (Windows taskbar / macOS-dock style): one card per window with
/// the app icon, title, a close button and a live DWM thumbnail. Click a card to switch to that window.
///
/// DWM thumbnails are composited *on top of* the destination window's own content and need a normal
/// (non-layered) window, so this is a plain Window, not a GlassWindow. Headers sit above the thumbnail
/// rectangles, so all controls stay visible and clickable. Win11 rounds the window's corners.
/// Approach adapted from OpenTaskbar's WindowThumbnailPreview (MIT, github.com/alztrk/opentaskbar).
/// </summary>
public sealed class WindowPreview : Window
{
    [StructLayout(LayoutKind.Sequential)] struct ThumbProps
    {
        public uint Flags; public RECT Dest; public RECT Src; public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible; [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
    }
    [StructLayout(LayoutKind.Sequential)] struct SIZE32 { public int W, H; }
    [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref ThumbProps props);
    [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE32 size);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    const uint DWM_TNP_RECTDESTINATION = 1, DWM_TNP_OPACITY = 4, DWM_TNP_VISIBLE = 8;

    const double CardW = 214, ThumbH = 122, HeaderH = 30, Gap = 8, Pad = 8;

    static WindowPreview? _instance;
    static readonly DispatcherTimer ShowTimer = new() { Interval = TimeSpan.FromMilliseconds(420) };
    static readonly DispatcherTimer HideTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };
    static FrameworkElement? _pendingAnchor;
    static Func<IReadOnlyList<AppWindow>>? _pendingWindows;
    static ImageSource? _pendingIcon;

    readonly List<IntPtr> _thumbs = new();
    readonly StackPanel _cards = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(Pad) };
    IntPtr _hwnd;
    public FrameworkElement? Anchor { get; private set; }

    static WindowPreview()
    {
        ShowTimer.Tick += (_, _) => { ShowTimer.Stop(); if (_pendingAnchor is { IsMouseOver: true } a && _pendingWindows != null) Open(a, _pendingWindows(), _pendingIcon); };
        HideTimer.Tick += (_, _) => { HideTimer.Stop(); if (_instance is { IsMouseOver: false } p && p.Anchor?.IsMouseOver != true) p.Close(); };
    }

    // ---------------- hover protocol used by the apps strip ----------------

    /// <summary>Pointer entered an app icon: show (or move) the preview after a short hover.</summary>
    public static void Hover(FrameworkElement anchor, Func<IReadOnlyList<AppWindow>> windows, ImageSource? icon)
    {
        if (!Settings.Current.WindowPreviews || FlyoutWindow.Current != null) return;
        HideTimer.Stop();
        _pendingAnchor = anchor; _pendingWindows = windows; _pendingIcon = icon;
        if (_instance != null) { Open(anchor, windows(), icon); return; } // already open: follow the pointer instantly
        ShowTimer.Stop();
        ShowTimer.Start();
    }

    /// <summary>Pointer left an app icon: close unless it moves onto the preview.</summary>
    public static void Leave()
    {
        ShowTimer.Stop();
        if (_instance != null) { HideTimer.Stop(); HideTimer.Start(); }
    }

    public static bool IsOpen => _instance != null;

    public static void CloseAll() { ShowTimer.Stop(); HideTimer.Stop(); _instance?.Close(); }

    static void Open(FrameworkElement anchor, IReadOnlyList<AppWindow> windows, ImageSource? icon)
    {
        var list = windows.Where(w => IsWindow(w.Hwnd)).Take(6).ToList();
        if (list.Count == 0) { CloseAll(); return; }
        if (_instance == null)
        {
            _instance = new WindowPreview();
            _instance.Closed += (_, _) => _instance = null;
        }
        _instance.Fill(anchor, list, icon);
    }

    WindowPreview()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;   // DWM thumbnails need a non-layered destination
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -20000; Top = -20000;
        Title = "Vela preview";
        UseLayoutRounding = true;
        FontFamily = Font;
        this.SetResourceReference(BackgroundProperty, "Flyout.Background");
        Content = _cards;
        MouseEnter += (_, _) => HideTimer.Stop();
        MouseLeave += (_, _) => { HideTimer.Stop(); HideTimer.Start(); };
        SizeChanged += (_, _) => { Place(); PlaceThumbnails(); };
        Closed += (_, _) => ReleaseThumbnails();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(_hwnd);
        src.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(MA_NOACTIVATE); } // keep focus where it is
            return IntPtr.Zero;
        });
        SetExStyle(_hwnd, GetExStyle(_hwnd) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        int dark = Theming.ThemeManager.Current.IsLight ? 0 : 1;
        DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    List<AppWindow> _windows = new();

    void Fill(FrameworkElement anchor, List<AppWindow> windows, ImageSource? icon)
    {
        bool same = Anchor == anchor && _windows.Select(w => w.Hwnd).SequenceEqual(windows.Select(w => w.Hwnd));
        Anchor = anchor;
        _windows = windows;
        Resources.MergedDictionaries.Clear();
        if (DockWindow.For(anchor)?.AdaptiveResources is { } rd) Resources.MergedDictionaries.Add(rd);
        if (!same || _cards.Children.Count == 0)
        {
            _cards.Children.Clear();
            for (int i = 0; i < windows.Count; i++) _cards.Children.Add(Card(windows[i], icon, i < windows.Count - 1));
        }
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        }
        UpdateLayout();
        Place();
        PlaceThumbnails();
    }

    FrameworkElement Card(AppWindow w, ImageSource? icon, bool gapAfter)
    {
        var close = CircleButton(UI.Glyphs.Close, () =>
        {
            WindowTracker.Close(w.Hwnd);
            Dispatcher.BeginInvoke(() =>
            {
                var rest = _windows.Where(x => x.Hwnd != w.Hwnd && IsWindow(x.Hwnd)).ToList();
                if (rest.Count == 0) Close(); else { _windows.Clear(); Fill(Anchor!, rest, icon); }
            }, DispatcherPriority.Background);
        }, 22, "Transparent", "Text.Secondary", 8).Tip("Close window");
        var img = new Image { Width = 16, Height = 16, Source = icon, Margin = new Thickness(0, 0, 7, 0) };
        var title = Text(w.Title, 11.5, FontWeights.SemiBold);
        var header = Columns("auto,*,auto", img, title, close);
        header.Height = HeaderH;
        // The thumbnail is drawn by DWM into this rectangle; the border is a hover ring just outside it.
        var thumbSlot = new Border { Height = ThumbH, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent };
        var card = new Border
        {
            Width = CardW, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 8),
            Margin = new Thickness(0, 0, gapAfter ? Gap : 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent,
            Child = V(header, thumbSlot), Tag = w.Hwnd,
        };
        card.MouseEnter += (_, _) => { card.Bg("Tile.Hover"); thumbSlot.SetResourceReference(Border.BorderBrushProperty, "Accent"); };
        card.MouseLeave += (_, _) => { card.Background = Brushes.Transparent; thumbSlot.BorderBrush = Brushes.Transparent; };
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (close.IsMouseOver) return;
            e.Handled = true;
            CloseAll();
            WindowTracker.Activate(w.Hwnd);
        };
        return card;
    }

    /// <summary>Centre over the icon, just above the dock's bar (below it when docked at the top), clamped to the display.</summary>
    void Place()
    {
        if (Anchor == null || PresentationSource.FromVisual(Anchor) == null || ActualWidth <= 0) return;
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var dock = DockWindow.For(Anchor);
        var mon = (dock?.MonitorInfo(out _) ?? GetPrimaryMonitor(out _)).rcMonitor;
        var c = Anchor.PointToScreen(new Point(Anchor.ActualWidth / 2, 0));
        double barTop = c.Y, barBottom = Anchor.PointToScreen(new Point(0, Anchor.ActualHeight)).Y;
        if (dock != null && PresentationSource.FromVisual(dock.BarElement) != null)
        {
            barTop = dock.BarElement.PointToScreen(new Point(0, 0)).Y;
            barBottom = dock.BarElement.PointToScreen(new Point(0, dock.BarElement.ActualHeight)).Y;
        }
        bool top = Settings.Current.Edge == "top";
        double w = ActualWidth * s, h = ActualHeight * s, gap = 10 * s;
        double x = Math.Clamp(c.X - w / 2, mon.Left + gap, Math.Max(mon.Left + gap, mon.Right - gap - w));
        double y = top ? barBottom + gap : barTop - gap - h;
        Left = x / s; Top = y / s;
    }

    void ReleaseThumbnails()
    {
        foreach (var t in _thumbs) DwmUnregisterThumbnail(t);
        _thumbs.Clear();
    }

    /// <summary>Register one DWM thumbnail per card and fit it (aspect kept) into the card's thumbnail slot.</summary>
    void PlaceThumbnails()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        ReleaseThumbnails();
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        foreach (Border card in _cards.Children)
        {
            if (card.Tag is not IntPtr src || card.Child is not StackPanel sp || sp.Children[1] is not Border slot) continue;
            if (DwmRegisterThumbnail(_hwnd, src, out var thumb) != 0 || thumb == IntPtr.Zero) continue;
            _thumbs.Add(thumb);
            var tl = slot.TranslatePoint(new Point(3, 3), this);
            double sw = (slot.ActualWidth - 6) * s, sh = (slot.ActualHeight - 6) * s;
            int x = (int)(tl.X * s), y = (int)(tl.Y * s), dw = (int)sw, dh = (int)sh;
            if (DwmQueryThumbnailSourceSize(thumb, out var size) == 0 && size.W > 0 && size.H > 0)
            {
                double k = Math.Min(sw / size.W, sh / size.H);
                dw = (int)(size.W * k); dh = (int)(size.H * k);
                x += (int)((sw - dw) / 2); y += (int)((sh - dh) / 2);
            }
            var p = new ThumbProps
            {
                Flags = DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY | DWM_TNP_VISIBLE,
                Dest = new RECT { Left = x, Top = y, Right = x + dw, Bottom = y + dh },
                Opacity = 255, Visible = true,
            };
            DwmUpdateThumbnailProperties(thumb, ref p);
        }
    }
}
