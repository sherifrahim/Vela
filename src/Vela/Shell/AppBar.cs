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

    public void Register()
    {
        if (_registered) Unregister();
        var d = Data();
        _registered = SHAppBarMessage(ABM_NEW, ref d) != IntPtr.Zero;
    }

    /// <summary>Reserve <paramref name="thicknessPx"/> on an edge of <paramref name="monitor"/>. Returns the granted rect.</summary>
    public RECT Reserve(RECT monitor, bool top, int thicknessPx)
    {
        if (!_registered) Register();
        var d = Data();
        d.uEdge = top ? ABE_TOP : ABE_BOTTOM;
        d.rc = monitor;
        SHAppBarMessage(ABM_QUERYPOS, ref d);
        if (top) d.rc.Bottom = d.rc.Top + thicknessPx;
        else d.rc.Top = d.rc.Bottom - thicknessPx;
        SHAppBarMessage(ABM_SETPOS, ref d);
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
        SHAppBarMessage(ABM_SETPOS, ref d);
    }

    public void Unregister()
    {
        if (!_registered) return;
        var d = Data();
        SHAppBarMessage(ABM_REMOVE, ref d);
        _registered = false;
    }

    public void Dispose() => Unregister();
}
