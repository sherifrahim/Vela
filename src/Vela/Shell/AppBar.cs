using System.Runtime.InteropServices;
using Vela.Interop;
using static Vela.Interop.Native;

namespace Vela.Shell;

/// <summary>
/// Registers the dock as a Windows "appbar" so maximized windows stop above it and so
/// Windows tells us when a fullscreen app opens (ABN_FULLSCREENAPP).
/// </summary>
internal sealed class AppBar : IDisposable
{
    readonly IntPtr _hwnd;
    bool _registered;
    public uint CallbackMessage { get; }

    public AppBar(IntPtr hwnd)
    {
        _hwnd = hwnd;
        CallbackMessage = RegisterWindowMessage("Vela.AppBarMessage");
    }

    APPBARDATA Data() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = CallbackMessage };

    /// <summary>
    /// SHAppBarMessage goes to the first "Shell_TrayWnd" in z-order. While Vela's tray host runs, that is the host, which
    /// forwards to Explorer: Explorer answers "success" but never changes the work area, so maximized windows ran under
    /// the dock. Our own appbar calls therefore step the host behind Explorer's tray for the call (same UI thread as
    /// the host's 1 Hz keep-on-top, so the two never race).
    /// </summary>
    static IntPtr Send(uint message, ref APPBARDATA d)
    {
        var host = Svc.TrayCreated && Svc.Tray.Running ? Svc.Tray.Handle : IntPtr.Zero;
        if (host == IntPtr.Zero) return SHAppBarMessage(message, ref d);
        const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE;
        SetWindowPos(host, HWND_NOTOPMOST, 0, 0, 0, 0, flags);
        SetWindowPos(host, HWND_BOTTOM, 0, 0, 0, 0, flags);
        try { return SHAppBarMessage(message, ref d); }
        finally { SetWindowPos(host, HWND_TOPMOST, 0, 0, 0, 0, flags); }
    }

    public void Register()
    {
        if (_registered) Unregister();
        var d = Data();
        _registered = Send(ABM_NEW, ref d) != IntPtr.Zero;
    }

    /// <summary>Reserve <paramref name="thicknessPx"/> on an edge of <paramref name="monitor"/>. Returns the granted rect.</summary>
    public RECT Reserve(RECT monitor, bool top, int thicknessPx)
    {
        if (!_registered) Register();
        var d = Data();
        d.uEdge = top ? ABE_TOP : ABE_BOTTOM;
        d.rc = monitor;
        Send(ABM_QUERYPOS, ref d);
        if (top) d.rc.Bottom = d.rc.Top + thicknessPx;
        else d.rc.Top = d.rc.Bottom - thicknessPx;
        Send(ABM_SETPOS, ref d);
        return d.rc;
    }

    /// <summary>Stay registered (for fullscreen notifications) but give back the reserved space.</summary>
    public void Release(RECT monitor, bool top)
    {
        if (!_registered) return;
        var d = Data();
        d.uEdge = top ? ABE_TOP : ABE_BOTTOM;
        d.rc = monitor;
        if (top) d.rc.Bottom = d.rc.Top; else d.rc.Top = d.rc.Bottom;
        Send(ABM_SETPOS, ref d);
    }

    public void Unregister()
    {
        if (!_registered) return;
        var d = Data();
        Send(ABM_REMOVE, ref d);
        _registered = false;
    }

    public void Dispose() => Unregister();
}
