using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Vela.Core;
using Vela.Interop;

namespace Vela.Theming;

/// <summary>
/// The dock's frosted glass, done without DWM: a tiny, pre-blurred copy of the desktop wallpaper,
/// stretched under the bar so it lines up with the real wallpaper behind it.
///
/// Why: DWM blur (accent policy / Mica) ignores window regions on layered windows, so it can't be
/// clipped to a rounded bar. Since the dock reserves its screen strip, what's behind it is almost
/// always the wallpaper anyway. Cost: one ~200px bitmap, decoded once per wallpaper change, 0 CPU after.
/// Live refraction of moving windows is a roadmap item (Windows.Graphics.Capture + shader).
/// </summary>
public static class WallpaperGlass
{
    /// <summary>Decode width + blur passes from Settings.GlassBlur (0 = crisp, 100 = very soft).</summary>
    static (int width, int radius, int passes) Quality()
    {
        double b = Math.Clamp(Settings.Current.GlassBlur, 0, 100) / 100;
        return ((int)(560 - b * 440), b < 0.05 ? 0 : 2, 1 + (int)Math.Round(b * 3));
    }
    static BitmapSource? _image;
    static double _imgW, _imgH;
    public static event Action? Changed;

    public static BitmapSource? Image => _image;

    public static void Reload()
    {
        try
        {
            _image = null;
            var path = WallpaperPath();
            if (path != null)
            {
                var bmp = new BitmapImage();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = Quality().width;
                    bmp.StreamSource = fs;
                    bmp.EndInit();
                }
                var q = Quality();
                // macOS-style vibrancy: glass styles get a saturation lift so colours glow through.
                _image = BoxBlur(bmp, q.radius, q.radius == 0 ? 0 : q.passes, Settings.Current.GlassEffect is "frosted" or "liquid" ? 1.35 : 1.12);
            }
            else if (SolidColor() is { } c)
            {
                _image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { c.B, c.G, c.R, 255 }, 4);
                _image.Freeze();
            }
            if (_image != null) { _imgW = _image.PixelWidth; _imgH = _image.PixelHeight; }
        }
        catch (Exception ex) { Log.Warn("Wallpaper glass unavailable: " + ex.Message); _image = null; }
        Changed?.Invoke();
    }

    /// <summary>
    /// Brush showing the part of the wallpaper under <paramref name="screenRect"/> (physical px)
    /// on a monitor whose bounds are <paramref name="monitor"/>. Assumes Fill/Stretch wallpaper fit.
    /// </summary>
    public static Brush? BrushFor(Rect screenRect, Rect monitor)
    {
        if (_image == null || monitor.Width <= 0) return null;
        bool stretch = WallpaperStyle() == "2";
        double sx = monitor.Width / _imgW, sy = monitor.Height / _imgH;
        double s = stretch ? 0 : Math.Max(sx, sy); // Fill: cover the monitor, centre-crop
        double offX = stretch ? 0 : (monitor.Width - _imgW * s) / 2;
        double offY = stretch ? 0 : (monitor.Height - _imgH * s) / 2;
        double ix(double x) => stretch ? (x - monitor.X) / sx : (x - monitor.X - offX) / s;
        double iy(double y) => stretch ? (y - monitor.Y) / sy : (y - monitor.Y - offY) / s;
        var vb = new Rect(new Point(ix(screenRect.Left) / _imgW, iy(screenRect.Top) / _imgH),
                          new Point(ix(screenRect.Right) / _imgW, iy(screenRect.Bottom) / _imgH));
        var brush = new ImageBrush(_image)
        {
            Viewbox = vb,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        return brush;
    }

    static string? WallpaperPath()
    {
        var sb = new System.Text.StringBuilder(520);
        if (SystemParametersInfo(0x0073 /*SPI_GETDESKWALLPAPER*/, sb.Capacity, sb, 0) && sb.Length > 0 && File.Exists(sb.ToString()))
            return sb.ToString();
        var transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
        return File.Exists(transcoded) ? transcoded : null;
    }

    static string WallpaperStyle()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return k?.GetValue("WallpaperStyle") as string ?? "10";
    }

    static Color? SolidColor()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
        var parts = (k?.GetValue("Background") as string)?.Split(' ');
        return parts is { Length: 3 } && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b)
            ? Color.FromRgb(r, g, b) : null;
    }

    /// <summary>Separable box blur, a few passes ≈ gaussian. Runs once on a ~200px image.</summary>
    static BitmapSource BoxBlur(BitmapSource src, int radius, int passes, double saturation = 1)
    {
        var fmt = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = fmt.PixelWidth, h = fmt.PixelHeight, stride = w * 4;
        var a = new byte[stride * h];
        fmt.CopyPixels(a, stride, 0);
        var b = new byte[a.Length];
        for (int p = 0; p < passes; p++)
        {
            Pass(a, b, w, h, radius, true);
            Pass(b, a, w, h, radius, false);
        }
        if (Math.Abs(saturation - 1) > 0.01) Saturate(a, saturation);
        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, a, stride);
        result.Freeze();
        return result;
    }

    static void Saturate(byte[] px, double s)
    {
        for (int i = 0; i < px.Length; i += 4)
        {
            double b = px[i], g = px[i + 1], r = px[i + 2];
            double l = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            px[i] = (byte)Math.Clamp(l + (b - l) * s, 0, 255);
            px[i + 1] = (byte)Math.Clamp(l + (g - l) * s, 0, 255);
            px[i + 2] = (byte)Math.Clamp(l + (r - l) * s, 0, 255);
        }
    }

    static void Pass(byte[] src, byte[] dst, int w, int h, int r, bool horizontal)
    {
        int outer = horizontal ? h : w, inner = horizontal ? w : h;
        for (int o = 0; o < outer; o++)
            for (int c = 0; c < 4; c++)
                for (int i = 0; i < inner; i++)
                {
                    int sum = 0, n = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int j = Math.Clamp(i + k, 0, inner - 1);
                        int idx = horizontal ? (o * w + j) * 4 + c : (j * w + o) * 4 + c;
                        sum += src[idx]; n++;
                    }
                    int di = horizontal ? (o * w + i) * 4 + c : (i * w + o) * 4 + c;
                    dst[di] = (byte)(sum / n);
                }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool SystemParametersInfo(int action, int param, System.Text.StringBuilder pv, int winIni);
}
