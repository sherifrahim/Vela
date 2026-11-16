using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vela.Core;
using Vela.Interop;

namespace Vela.Theming;

/// <summary>
/// "Match the screen" for one dock: samples the strip of its own display just above the bar (where the active
/// app's colours meet the dock), turns it into a blurred, colour-boosted glass backdrop, and reports whether
/// that area is light or dark so the dock can switch its text/tiles. Each display's dock owns one, so every
/// dock follows what is on *its* screen.
///
/// Cost model: one small GDI StretchBlt (HALFTONE, to ≤ 240 px wide) + a tiny blur on a thread-pool thread.
/// Triggered by foreground changes and window moves (throttled), plus a 2.5 s heartbeat only while a window
/// actually borders the dock; nothing runs while the desktop is idle behind it or the dock is hidden.
/// </summary>
internal sealed class AdaptiveBackdrop : IDisposable
{
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BMI bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [StructLayout(LayoutKind.Sequential)] struct BMI { public Native.BITMAPINFOHEADER H; public uint C0; }

    public sealed record Sample(BitmapSource Image, double Luminance);

    readonly Func<(Native.RECT strip, bool active)> _where;
    readonly Action<Sample> _onSample;
    readonly DispatcherTimer _throttle = new() { Interval = TimeSpan.FromMilliseconds(220) };
    readonly DispatcherTimer _heartbeat = new() { Interval = TimeSpan.FromSeconds(2.5) };
    bool _busy, _pending, _disposed;
    long _lastHash;

    /// <param name="where">Strip to sample in physical px, and whether sampling is allowed right now.</param>
    public AdaptiveBackdrop(Func<(Native.RECT strip, bool active)> where, Action<Sample> onSample)
    {
        _where = where;
        _onSample = onSample;
        _throttle.Tick += (_, _) => { _throttle.Stop(); Run(); };
        _heartbeat.Tick += (_, _) => Run();
    }

    /// <summary>Something on screen changed (window switched / moved): sample soon, coalescing bursts.</summary>
    public void Poke()
    {
        if (_disposed) return;
        if (!_throttle.IsEnabled) _throttle.Start();
    }

    /// <summary>Keep sampling slowly while a window borders the dock (its content can change without events).</summary>
    public void SetHeartbeat(bool on)
    {
        if (on && !_heartbeat.IsEnabled) _heartbeat.Start();
        else if (!on) _heartbeat.Stop();
    }

    void Run()
    {
        if (_disposed) return;
        if (_busy) { _pending = true; return; }
        var (strip, active) = _where();
        if (!active || strip.Width <= 0 || strip.Height <= 0) return;
        _busy = true;
        var dispatcher = Dispatcher.CurrentDispatcher;
        Task.Run(() => Capture(strip)).ContinueWith(t =>
        {
            dispatcher.BeginInvoke(() =>
            {
                _busy = false;
                if (!_disposed && t.Status == TaskStatus.RanToCompletion && t.Result is { } s) _onSample(s);
                if (_pending) { _pending = false; Poke(); }
            });
        });
    }

    Sample? Capture(Native.RECT r)
    {
        const int W = 240;
        int h = Math.Clamp((int)Math.Round(W * (double)r.Height / r.Width * 4), 6, 24); // keep some vertical detail
        IntPtr screen = GetDC(IntPtr.Zero), mem = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            mem = CreateCompatibleDC(screen);
            var bmi = new BMI { H = new Native.BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(), biWidth = W, biHeight = -h, biPlanes = 1, biBitCount = 32 } };
            dib = CreateDIBSection(mem, ref bmi, 0, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) return null;
            old = SelectObject(mem, dib);
            SetStretchBltMode(mem, 4 /*HALFTONE*/);
            if (!StretchBlt(mem, 0, 0, W, h, screen, r.Left, r.Top, r.Width, r.Height, 0x00CC0020 /*SRCCOPY*/)) return null;
            var px = new byte[W * h * 4];
            Marshal.Copy(bits, px, 0, px.Length);

            // Skip identical frames (heartbeat over a static window).
            long hash = 17;
            for (int i = 0; i < px.Length; i += 64) hash = hash * 31 + px[i];
            if (hash == _lastHash) return null;
            _lastHash = hash;

            double lum = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                lum += 0.0722 * px[i] + 0.7152 * px[i + 1] + 0.2126 * px[i + 2];
                px[i + 3] = 255;
            }
            lum /= (px.Length / 4) * 255.0;
            Blur(px, W, h, 3, 3);
            Saturate(px, 1.4);
            var img = BitmapSource.Create(W, h, 96, 96, PixelFormats.Bgra32, null, px, W * 4);
            img.Freeze();
            return new Sample(img, lum);
        }
        catch (Exception ex) { Log.Warn("Adaptive sample failed: " + ex.Message); return null; }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(mem, old);
            if (dib != IntPtr.Zero) Native.DeleteObject(dib);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    static void Blur(byte[] a, int w, int h, int r, int passes)
    {
        var b = new byte[a.Length];
        for (int p = 0; p < passes; p++) { Pass(a, b, w, h, r, true); Pass(b, a, w, h, Math.Min(r, h / 2), false); }
    }

    static void Pass(byte[] src, byte[] dst, int w, int h, int r, bool horizontal)
    {
        int outer = horizontal ? h : w, inner = horizontal ? w : h;
        for (int o = 0; o < outer; o++)
            for (int c = 0; c < 3; c++)
            {
                int sum = 0, n = 0;
                for (int k = -r; k <= r; k++) { int j = Math.Clamp(k, 0, inner - 1); sum += src[Idx(o, j, w, horizontal) + c]; n++; }
                for (int i = 0; i < inner; i++)
                {
                    dst[Idx(o, i, w, horizontal) + c] = (byte)(sum / n);
                    int add = Math.Min(i + r + 1, inner - 1), rem = Math.Max(i - r, 0);
                    sum += src[Idx(o, add, w, horizontal) + c] - src[Idx(o, rem, w, horizontal) + c];
                }
            }
        for (int i = 3; i < dst.Length; i += 4) dst[i] = 255;
    }

    static int Idx(int o, int i, int w, bool horizontal) => horizontal ? (o * w + i) * 4 : (i * w + o) * 4;

    static void Saturate(byte[] px, double s)
    {
        for (int i = 0; i < px.Length; i += 4)
        {
            double b = px[i], g = px[i + 1], r = px[i + 2], l = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            px[i] = (byte)Math.Clamp(l + (b - l) * s, 0, 255);
            px[i + 1] = (byte)Math.Clamp(l + (g - l) * s, 0, 255);
            px[i + 2] = (byte)Math.Clamp(l + (r - l) * s, 0, 255);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _throttle.Stop();
        _heartbeat.Stop();
    }
}
