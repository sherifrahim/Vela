using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vela.Core;
using Vela.Interop;

namespace Vela.Shell;

/// <summary>
/// High-quality app icons via IShellItemImageFactory, extracted on a dedicated STA thread so the
/// UI never blocks. Results are frozen BitmapSources cached by parsing name.
/// </summary>
public sealed class IconCache
{
    readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly BlockingCollection<(string key, int size, Action<ImageSource?> done)> _queue = new();
    readonly ConcurrentDictionary<string, List<Action<ImageSource?>>> _pending = new(StringComparer.OrdinalIgnoreCase);

    public IconCache()
    {
        var t = new Thread(Worker) { IsBackground = true, Name = "Vela.IconWorker" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    /// <summary>Get an icon for an exe/lnk path or "shell:AppsFolder\AUMID". Callback runs on the UI thread.</summary>
    public void Get(string parsingName, Action<ImageSource?> onReady, int size = 96)
    {
        if (string.IsNullOrEmpty(parsingName)) { onReady(null); return; }
        if (_cache.TryGetValue(parsingName, out var hit)) { onReady(hit); return; }

        var dispatcher = Application.Current.Dispatcher;
        Action<ImageSource?> marshal = img => dispatcher.BeginInvoke(() => onReady(img));
        var list = _pending.GetOrAdd(parsingName, _ => new List<Action<ImageSource?>>());
        lock (list)
        {
            list.Add(marshal);
            if (list.Count > 1) return; // already queued
        }
        _queue.Add((parsingName, size, _ => { }));
    }

    void Worker()
    {
        foreach (var (key, size, _) in _queue.GetConsumingEnumerable())
        {
            ImageSource? img = null;
            try { img = Extract(key, size); }
            catch (Exception ex) { Log.Warn($"Icon extract failed for {key}: {ex.Message}"); }
            _cache[key] = img;
            if (_pending.TryRemove(key, out var list))
                lock (list) foreach (var cb in list) cb(img);
        }
    }

    static ImageSource? Extract(string parsingName, int size)
    {
        var iid = ShellNative.IID_IShellItemImageFactory;
        if (ShellNative.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var obj) != 0 || obj == null)
            return null;
        try
        {
            var factory = (IShellItemImageFactory)obj;
            if (factory.GetImage(new SIZE(size, size), ShellNative.SIIGBF_ICONONLY | ShellNative.SIIGBF_BIGGERSIZEOK, out var hbm) != 0
                || hbm == IntPtr.Zero)
                return null;
            try { return FromHBitmap(hbm); }
            finally { Native.DeleteObject(hbm); }
        }
        finally { Marshal.ReleaseComObject(obj); }
    }

    /// <summary>Copy a 32bpp DIB section into a WPF bitmap, keeping alpha (CreateBitmapSourceFromHBitmap drops it).</summary>
    static BitmapSource? FromHBitmap(IntPtr hbm)
    {
        var ds = new Native.DIBSECTION();
        if (Native.GetObject(hbm, Marshal.SizeOf<Native.DIBSECTION>(), ref ds) == 0) return null;
        var bm = ds.dsBm;
        if (bm.bmBitsPixel != 32 || bm.bmBits == IntPtr.Zero) return null;

        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight), stride = bm.bmWidthBytes;
        var buf = new byte[stride * h];
        Marshal.Copy(bm.bmBits, buf, 0, buf.Length);

        if (ds.dsBmih.biHeight > 0) // bottom-up: flip
        {
            var row = new byte[stride];
            for (int y = 0; y < h / 2; y++)
            {
                Buffer.BlockCopy(buf, y * stride, row, 0, stride);
                Buffer.BlockCopy(buf, (h - 1 - y) * stride, buf, y * stride, stride);
                Buffer.BlockCopy(row, 0, buf, (h - 1 - y) * stride, stride);
            }
        }

        // Detect alpha format: all-zero alpha = opaque bitmap; any channel > alpha = straight alpha.
        bool anyAlpha = false, straight = false;
        for (int i = 0; i < buf.Length; i += 4)
        {
            byte a = buf[i + 3];
            if (a != 0) anyAlpha = true;
            if (buf[i] > a || buf[i + 1] > a || buf[i + 2] > a) straight = true;
        }
        if (!anyAlpha) for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;

        var fmt = (!anyAlpha || straight) ? PixelFormats.Bgra32 : PixelFormats.Pbgra32;
        var src = BitmapSource.Create(w, h, 96, 96, fmt, null, buf, stride);
        src.Freeze();
        return src;
    }
}
