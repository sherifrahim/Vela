using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Vela.Core;
using static Vela.Interop.Native;

namespace Vela.Views;

/// <summary>
/// Live blur of whatever is behind the dock, done by DWM on the GPU (no capture, ~0 CPU).
///
/// DWM blur only works on a normal (non-layered) window, and the dock itself must stay layered for per-pixel
/// alpha. So the blur lives in this separate, borderless window that sits exactly under the bar (z-ordered
/// right below its dock), shaped to the bar with a rounded window region inset by a pixel so its aliased edge
/// hides under the bar's anti-aliased rim. The dock draws its tint, shading and tiles on top.
/// Material: Windows 11 22H2+ system backdrop "transient" (Acrylic); older builds use the accent-policy blur.
/// </summary>
public sealed class BackdropWindow : Window
{
    public enum Kind { SystemAcrylic, AccentAcrylic, AccentBlur }

    /// <summary>Test hook: raw accent state / tint (AABBGGRR) / dark flag, overriding <see cref="Kind"/>.</summary>
    public (int State, uint Tint, int Flags, bool Dark)? Raw { get; init; }

    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int L, R, T, B; }
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMSBT_TRANSIENTWINDOW = 3;

    readonly Kind _kind;
    IntPtr _hwnd;
    double _radiusPx;

    /// <summary>The material that blurs live even when the window is never active (system backdrops only render while active).</summary>
    public static Kind Best => Kind.AccentAcrylic;

    public BackdropWindow(Kind kind)
    {
        _kind = kind;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;              // DWM backdrops need a non-layered window
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        Title = "Vela Backdrop";
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -20000; Top = -20000; Width = 10; Height = 10;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        if (_hwnd == IntPtr.Zero) return; // creation failed (e.g. re-entrant render pass); Place() retries later
        var src = HwndSource.FromHwnd(_hwnd);
        src.CompositionTarget.BackgroundColor = Colors.Transparent;
        src.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(MA_NOACTIVATE); }
            return IntPtr.Zero;
        });
        SetExStyle(_hwnd, GetExStyle(_hwnd) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        var m = new MARGINS { L = -1, R = -1, T = -1, B = -1 };
        DwmExtendFrameIntoClientArea(_hwnd, ref m);
        int dark = Raw is { } rw ? (rw.Dark ? 1 : 0) : 1;
        DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int noRound = 1; // DWMWCP_DONOTROUND: our region does the rounding
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref noRound, sizeof(int));
        int none = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref none, sizeof(int));
        int keep = 1; // the glass stays under the dock during Aero Peek
        DwmSetWindowAttribute(_hwnd, DWMWA_EXCLUDED_FROM_PEEK, ref keep, sizeof(int));
        ApplyMaterial();
    }

    void ApplyMaterial()
    {
        try
        {
            if (Raw is { } raw) { SetAccent(raw.State, raw.Tint, raw.Flags); return; }
            if (_kind == Kind.SystemAcrylic)
            {
                int t = DWMSBT_TRANSIENTWINDOW;
                DwmSetWindowAttribute(_hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref t, sizeof(int));
            }
            // Tested on Win11 26300 over a striped test pattern: accent Acrylic with flags 0 and a near-transparent
            // tint (dark window) gives a true, colourful live blur. Flags 2 renders black; plain blur renders black.
            else SetAccent(_kind == Kind.AccentAcrylic ? 4 : 3, 0x01000000, 0);
        }
        catch (Exception ex) { Log.Warn("Backdrop material: " + ex.Message); }
    }

    void SetAccent(int state, uint tint, int flags)
    {
        var accent = new AccentPolicy { AccentState = state, AccentFlags = flags, GradientColor = tint };
        int size = Marshal.SizeOf(accent);
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttribData { Attribute = 19, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(_hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    /// <summary>Cover <paramref name="r"/> (physical px) with a rounded region, directly below <paramref name="above"/> in z-order.</summary>
    internal void Place(RECT r, double radiusPx, IntPtr above)
    {
        // EnsureHandle, not Show(): Show() can take activation, which closes the card this backdrop sits under.
        // The window is then revealed by SetWindowPos(SWP_SHOWWINDOW | SWP_NOACTIVATE) below.
        if (_hwnd == IntPtr.Zero) { try { new WindowInteropHelper(this).EnsureHandle(); } catch (Exception ex) { Log.Warn("Backdrop window: " + ex.Message); } }
        if (_hwnd == IntPtr.Zero) return;
        if (_visible && r.Left == _last.Left && r.Top == _last.Top && r.Right == _last.Right && r.Bottom == _last.Bottom && Math.Abs(radiusPx - _radiusPx) < 0.1) return;
        _last = r;
        _visible = true;
        _radiusPx = radiusPx;
        // Inset 1 px: the region's stair-stepped edge stays hidden under the dock's anti-aliased rim.
        int w = Math.Max(1, r.Width), h = Math.Max(1, r.Height);
        SetWindowPos(_hwnd, above == IntPtr.Zero ? HWND_TOPMOST : above, r.Left, r.Top, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        int d = (int)Math.Round(radiusPx * 2);
        var rgn = CreateRoundRectRgn(1, 1, w, h, d, d);
        if (SetWindowRgn(_hwnd, rgn, true) == 0) DeleteObject(rgn);
    }

    RECT _last;
    bool _visible;

    public void HideBackdrop() { if (_hwnd != IntPtr.Zero && _visible) { ShowWindow(_hwnd, SW_HIDE); _visible = false; } }
}
