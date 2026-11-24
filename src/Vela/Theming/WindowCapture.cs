using System.Runtime.InteropServices;
using Vela.Core;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using static Vela.Interop.Native;

namespace Vela.Theming;

/// <summary>
/// Live pixels of ONE window (the one behind the dock), for the Liquid Glass lens. Uses Windows.Graphics.Capture on
/// that window only — not the screen — so Vela itself is never in the image (no feedback, and the dock still shows up
/// in the user's own screenshots). Frames arrive only when that window repaints; each is cropped on the GPU to the
/// strip behind the bar and handed over as BGRA bytes, at most ~30 times a second.
///
/// Minimal Direct3D 11 interop by hand (no packages): create a device, wrap it for WinRT, copy the crop into a
/// staging texture and map it.
/// </summary>
internal sealed unsafe class WindowCapture : IDisposable
{
    public IntPtr Target { get; }
    /// <summary>Screen rectangle (physical px) to crop: the bar. Set from the UI thread whenever the bar moves.</summary>
    public RECT Crop { get; set; }
    /// <summary>Called on a capture thread: BGRA pixels, width, height, and the crop's screen rectangle.</summary>
    public event Action<byte[], int, int, RECT>? Frame;

    IntPtr _device, _context, _staging;
    int _stagingW, _stagingH;
    Direct3D11CaptureFramePool? _pool;
    GraphicsCaptureSession? _session;
    SizeInt32 _size;
    long _lastTicks;
    byte[]? _last;          // previous strip, to skip frames where nothing behind the bar changed
    int _lastLen;
    RECT _lastAt;
    RECT? _stagedAt;        // the staging texture holds a strip not yet handed over
    readonly Timer _timer;
    bool _timerArmed;
    readonly object _gate = new();
    bool _disposed;

    public static bool Supported
    {
        get { try { return GraphicsCaptureSession.IsSupported(); } catch { return false; } }
    }

    public WindowCapture(IntPtr hwnd)
    {
        Target = hwnd;
        _timer = new Timer(OnTimer);
        CreateDevice();
        var winrtDevice = WrapDevice(_device);
        var item = ItemForWindow(hwnd);
        _size = item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
        _pool.FrameArrived += OnFrame;
        _session = _pool.CreateCaptureSession(item);
        try { _session.IsCursorCaptureEnabled = false; } catch { }
        // A yellow "being captured" frame around someone's window would be far worse than plain blur: refuse instead.
        if (!HideBorder(_session)) { Dispose(); throw new NotSupportedException("the capture border can't be hidden on this Windows version"); }
        _session.StartCapture();
    }


    void OnFrame(Direct3D11CaptureFramePool pool, object _)
    {
        lock (_gate)
        {
            if (_disposed) return;
            using var frame = pool.TryGetNextFrame();
            if (frame == null) return;
            if (frame.ContentSize.Width != _size.Width || frame.ContentSize.Height != _size.Height)
            {
                _size = frame.ContentSize;
                pool.Recreate(WrapDevice(_device), DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
                return;
            }
            try { CopyCrop(frame); }
            catch (Exception ex) { Log.Warn("Liquid Glass capture: " + ex.Message); return; }
            // At most 20 frames a second reach the dock (plenty for frosted glass). A frame that comes too soon stays
            // in the staging texture and a one-shot timer hands over the newest one, so the last change is never lost.
            long wait = 50 - (Environment.TickCount64 - _lastTicks);
            if (wait <= 0) Deliver();
            else if (!_timerArmed) { _timerArmed = true; _timer.Change(wait, Timeout.Infinite); }
        }
    }

    /// <summary>GPU-side: copy the strip behind the bar out of the captured frame into the staging texture.</summary>
    void CopyCrop(Direct3D11CaptureFrame frame)
    {
        _stagedAt = null;
        // Where the window's visible frame sits on screen = the captured image's origin.
        if (DwmGetWindowAttribute(Target, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out RECT win, sizeof(RECT)) != 0 && !GetWindowRect(Target, out win)) return;
        var crop = Crop;
        int left = Math.Max(crop.Left, win.Left), top = Math.Max(crop.Top, win.Top);
        int right = Math.Min(crop.Right, win.Right), bottom = Math.Min(crop.Bottom, win.Bottom);
        int fx = left - win.Left, fy = top - win.Top;
        int w = Math.Min(right - left, _size.Width - fx), h = Math.Min(bottom - top, _size.Height - fy);
        if (w <= 0 || h <= 0 || fx < 0 || fy < 0) return;

        var texture = TextureOf(frame.Surface);
        try
        {
            EnsureStaging(w, h);
            var box = new D3D11_BOX { left = (uint)fx, top = (uint)fy, front = 0, right = (uint)(fx + w), bottom = (uint)(fy + h), back = 1 };
            var ctx = (void**)*(void***)_context;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, D3D11_BOX*, void>)ctx[46])(_context, _staging, 0, 0, 0, 0, texture, 0, &box);
            _stagedAt = new RECT { Left = left, Top = top, Right = left + w, Bottom = top + h };
        }
        finally { Marshal.Release(texture); }
    }

    void OnTimer(object? _)
    {
        lock (_gate)
        {
            _timerArmed = false;
            if (_disposed) return;
            try { Deliver(); }
            catch (Exception ex) { Log.Warn("Liquid Glass capture: " + ex.Message); }
        }
    }

    /// <summary>CPU-side: read the staged strip back and hand it to the dock, unless it's the same as last time.</summary>
    void Deliver()
    {
        if (_stagedAt is not { } at) return;
        _stagedAt = null;
        _lastTicks = Environment.TickCount64;
        int w = at.Right - at.Left, h = at.Bottom - at.Top, n = w * h * 4;
        var ctx = (void**)*(void***)_context;
        D3D11_MAPPED_SUBRESOURCE mapped;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)ctx[14])(_context, _staging, 0, 1 /*READ*/, 0, &mapped);
        if (hr < 0) return;
        var bytes = System.Buffers.ArrayPool<byte>.Shared.Rent(n); // the receiver returns it to the pool
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy((IntPtr)((byte*)mapped.pData + y * mapped.RowPitch), bytes, y * w * 4, w * 4);
        }
        finally { ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)ctx[15])(_context, _staging, 0); }
        // Windows repaint far more than the strip behind the bar changes (a chat streaming above it, a caret
        // blinking elsewhere). Same pixels at the same place = nothing to redraw, so the dock stays idle.
        if (_last != null && _lastLen == n && _lastAt.Equals(at) && bytes.AsSpan(0, n).SequenceEqual(_last.AsSpan(0, n)))
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(bytes);
            return;
        }
        if (_last == null || _last.Length < n) _last = new byte[n];
        bytes.AsSpan(0, n).CopyTo(_last);
        _lastLen = n; _lastAt = at;
        Frame?.Invoke(bytes, w, h, at);
    }

    void EnsureStaging(int w, int h)
    {
        if (_staging != IntPtr.Zero && _stagingW == w && _stagingH == h) return;
        if (_staging != IntPtr.Zero) { Marshal.Release(_staging); _staging = IntPtr.Zero; }
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = 87 /*B8G8R8A8_UNORM*/,
            SampleCount = 1, SampleQuality = 0, Usage = 3 /*STAGING*/, BindFlags = 0, CPUAccessFlags = 0x20000 /*READ*/, MiscFlags = 0,
        };
        IntPtr tex;
        var dev = (void**)*(void***)_device;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_TEXTURE2D_DESC*, IntPtr, IntPtr*, int>)dev[5])(_device, &desc, IntPtr.Zero, &tex);
        Marshal.ThrowExceptionForHR(hr);
        _staging = tex; _stagingW = w; _stagingH = h;
    }

    // ---------- device + WinRT plumbing ----------

    void CreateDevice()
    {
        IntPtr device, context;
        int level;
        int hr = D3D11CreateDevice(IntPtr.Zero, 1 /*HARDWARE*/, IntPtr.Zero, 0x20 /*BGRA_SUPPORT*/, IntPtr.Zero, 0, 7, out device, out level, out context);
        Marshal.ThrowExceptionForHR(hr);
        _device = device; _context = context;
    }

    static IDirect3DDevice WrapDevice(IntPtr d3dDevice)
    {
        var iidDxgi = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, in iidDxgi, out var dxgi));
        try
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
            try { return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
            finally { Marshal.Release(inspectable); }
        }
        finally { Marshal.Release(dxgi); }
    }

    static GraphicsCaptureItem ItemForWindow(IntPtr hwnd)
    {
        var factory = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var iidInterop = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory.ThisPtr, in iidInterop, out var interop));
        try
        {
            var iidItem = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            IntPtr result;
            var vt = (void**)*(void***)interop;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vt[3])(interop, hwnd, &iidItem, &result);
            Marshal.ThrowExceptionForHR(hr);
            try { return GraphicsCaptureItem.FromAbi(result); }
            finally { Marshal.Release(result); }
        }
        finally { Marshal.Release(interop); }
    }

    /// <summary>Windows 11: no yellow capture border (IGraphicsCaptureSession3.IsBorderRequired = false).</summary>
    static bool HideBorder(GraphicsCaptureSession session)
    {
        try
        {
            var iid = new Guid("f2cdd966-22ae-5ea1-9596-3a289344c3be");
            var ptr = ((WinRT.IWinRTObject)session).NativeObject.ThisPtr;
            if (Marshal.QueryInterface(ptr, in iid, out var s3) < 0) return false; // Windows 10: the border always shows
            try
            {
                var vt = (void**)*(void***)s3;
                return ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)vt[7])(s3, 0) >= 0;
            }
            finally { Marshal.Release(s3); }
        }
        catch { return false; }
    }

    static IntPtr TextureOf(IDirect3DSurface surface)
    {
        var iidAccess = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
        var iidTexture = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        var ptr = ((WinRT.IWinRTObject)surface).NativeObject.ThisPtr;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(ptr, in iidAccess, out var access));
        try
        {
            IntPtr tex;
            var vt = (void**)*(void***)access;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vt[3])(access, &iidTexture, &tex));
            return tex;
        }
        finally { Marshal.Release(access); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            try { _session?.Dispose(); } catch { }
            try { if (_pool != null) { _pool.FrameArrived -= OnFrame; _pool.Dispose(); } } catch { }
            if (_staging != IntPtr.Zero) { Marshal.Release(_staging); _staging = IntPtr.Zero; }
            if (_context != IntPtr.Zero) { Marshal.Release(_context); _context = IntPtr.Zero; }
            if (_device != IntPtr.Zero) { Marshal.Release(_device); _device = IntPtr.Zero; }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct D3D11_TEXTURE2D_DESC
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags;
    }
    [StructLayout(LayoutKind.Sequential)] struct D3D11_BOX { public uint left, top, front, right, bottom, back; }
    [StructLayout(LayoutKind.Sequential)] struct D3D11_MAPPED_SUBRESOURCE { public void* pData; public uint RowPitch, DepthPitch; }

    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr levels, uint levelCount,
        uint sdkVersion, out IntPtr device, out int level, out IntPtr context);

    [DllImport("d3d11.dll")]
    static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
