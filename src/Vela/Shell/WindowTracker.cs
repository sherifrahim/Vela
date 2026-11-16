using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Windows.Threading;
using Vela.Core;
using Vela.Interop;
using static Vela.Interop.Native;

namespace Vela.Shell;

public sealed class AppWindow
{
    public IntPtr Hwnd { get; init; }
    public string Title { get; set; } = "";
    public uint Pid { get; init; }
    public string? ExePath { get; init; }
    public string? Aumid { get; init; }
    public bool Flashing { get; set; }

    /// <summary>Grouping key: AUMID when the app sets one, else the exe path.</summary>
    public string GroupKey => (Aumid ?? ExePath ?? $"pid:{Pid}").ToLowerInvariant();
}

/// <summary>
/// Tracks taskbar-eligible top-level windows. Event-driven via the shell hook
/// (RegisterShellHookWindow) — no polling. Rebuilds are debounced.
/// </summary>
public sealed class WindowTracker
{
    readonly Dictionary<IntPtr, AppWindow> _windows = new();
    readonly Dictionary<uint, string?> _exeCache = new();
    readonly DispatcherTimer _debounce;
    readonly uint _ownPid = (uint)Environment.ProcessId;
    public uint ShellHookMessage { get; private set; }

    public IReadOnlyCollection<AppWindow> Windows => _windows.Values;
    public IntPtr Foreground { get; private set; }

    /// <summary>Window list changed (open/close/title/flash).</summary>
    public event Action? Changed;
    /// <summary>Foreground window changed.</summary>
    public event Action? ForegroundChanged;

    public WindowTracker()
    {
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Refresh(); };
    }

    public void Start(IntPtr hookWindow)
    {
        ShellHookMessage = RegisterWindowMessage("SHELLHOOK");
        if (!RegisterShellHookWindow(hookWindow)) Log.Warn("RegisterShellHookWindow failed");
        Refresh();
    }

    public void Stop(IntPtr hookWindow) => DeregisterShellHookWindow(hookWindow);

    public void HandleShellMessage(IntPtr wParam, IntPtr lParam)
    {
        int code = wParam.ToInt32();
        switch (code)
        {
            case HSHELL_WINDOWACTIVATED:
            case HSHELL_RUDEAPPACTIVATED:
                Foreground = lParam;
                if (_windows.TryGetValue(lParam, out var w) && w.Flashing) { w.Flashing = false; Changed?.Invoke(); }
                ForegroundChanged?.Invoke();
                if (!_windows.ContainsKey(lParam)) _debounce.Start();
                break;
            case HSHELL_FLASH:
                if (_windows.TryGetValue(lParam, out var fw) && lParam != Foreground) { fw.Flashing = true; Changed?.Invoke(); }
                break;
            case HSHELL_REDRAW:
                if (_windows.TryGetValue(lParam, out var rw))
                {
                    var t = GetWindowTitle(lParam);
                    if (t != rw.Title) { rw.Title = t; Changed?.Invoke(); }
                }
                break;
            default:
                _debounce.Stop();
                _debounce.Start();
                break;
        }
    }

    public void Refresh()
    {
        var seen = new HashSet<IntPtr>();
        bool changed = false;
        EnumWindows((h, _) =>
        {
            if (!IsTaskbarWindow(h)) return true;
            seen.Add(h);
            if (!_windows.ContainsKey(h))
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (!_exeCache.TryGetValue(pid, out var exe))
                    _exeCache[pid] = exe = GetProcessPath(pid);
                _windows[h] = new AppWindow
                {
                    Hwnd = h,
                    Pid = pid,
                    ExePath = exe,
                    Aumid = ShellNative.GetWindowAumid(h),
                    Title = GetWindowTitle(h),
                };
                changed = true;
            }
            return true;
        }, IntPtr.Zero);

        foreach (var dead in _windows.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _windows.Remove(dead);
            changed = true;
        }
        if (_exeCache.Count > 512) _exeCache.Clear();

        var fg = GetForegroundWindow();
        if (fg != Foreground) { Foreground = fg; ForegroundChanged?.Invoke(); }
        if (changed) Changed?.Invoke();
    }

    bool IsTaskbarWindow(IntPtr h)
    {
        if (!IsWindowVisible(h)) return false;
        long ex = GetExStyle(h);
        bool appWindow = (ex & WS_EX_APPWINDOW) != 0;
        if (!appWindow)
        {
            if ((ex & WS_EX_TOOLWINDOW) != 0) return false;
            if ((ex & WS_EX_NOACTIVATE) != 0) return false;
            if (GetWindow(h, GW_OWNER) != IntPtr.Zero) return false;
        }
        if (IsCloaked(h)) return false;
        if (GetWindowTextLength(h) == 0) return false;
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == _ownPid) return false;
        var cls = GetClassNameOf(h);
        return cls is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow");
    }

    // ---------- actions ----------

    /// <summary>Taskbar semantics: activate, or minimize if it's already in front.</summary>
    public void ActivateOrMinimize(AppWindow w)
    {
        if (w.Hwnd == Foreground && !IsIconic(w.Hwnd)) { ShowWindowAsync(w.Hwnd, SW_MINIMIZE); return; }
        Activate(w.Hwnd);
    }

    public static void Activate(IntPtr h)
    {
        if (IsIconic(h)) ShowWindowAsync(h, SW_RESTORE);
        if (!SetForegroundWindow(h)) SwitchToThisWindow(h, true);
    }

    public static void Close(IntPtr h) => PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    public static void Launch(string path, string? args = null)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Arguments = args ?? "" }); }
        catch (Exception ex) { Log.Error($"Launch failed: {path}", ex); }
    }

    public static void OpenUri(string uri) => Launch(uri);

    /// <summary>
    /// Re-fits maximized windows on the dock's monitor that still extend past the work area (they were
    /// maximized while the Windows taskbar was hidden, before the dock reserved its strip). Only resizable
    /// (WS_THICKFRAME) windows: borderless fullscreen games / F11 browsers are left alone.
    /// </summary>
    static readonly Dictionary<IntPtr, int> FitAttempts = new();

    public static void FitMaximizedToWorkArea(IntPtr dock)
    {
        try
        {
            var mi = GetMonitorFor(dock);
            var work = mi.rcWork;
            int fixedCount = 0;
            EnumWindows((h, _) =>
            {
                if (h == dock || !IsWindowVisible(h) || IsIconic(h) || !IsZoomed(h) || IsCloaked(h)) return true;
                long style = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
                if ((style & 0x00040000L) == 0) return true; // WS_THICKFRAME
                if (MonitorFromWindow(h, 2) != MonitorFromWindow(dock, 2)) return true;
                if (!GetWindowRect(h, out var r)) return true;
                if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT f, Marshal.SizeOf<RECT>()) != 0) f = r;
                if (f.Bottom <= work.Bottom + 1 && f.Top >= work.Top - 1) return true;
                // Some apps insist on their size; try a couple of times, then leave them alone.
                FitAttempts.TryGetValue(h, out int n);
                if (n >= 2) return true;
                FitAttempts[h] = n + 1;
                // Keep the window's invisible resize borders outside the work area, as Windows does.
                int dl = f.Left - r.Left, dt = f.Top - r.Top, dr = r.Right - f.Right, db = r.Bottom - f.Bottom;
                SetWindowPos(h, IntPtr.Zero, work.Left - dl, work.Top - dt,
                    work.Right - work.Left + dl + dr, work.Bottom - work.Top + dt + db, SWP_NOZORDER | SWP_NOACTIVATE);
                fixedCount++;
                Log.Info($"Fitted a maximized {GetClassNameOf(h)} window to the work area (attempt {n + 1})");
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) { Log.Error("FitMaximizedToWorkArea", ex); }
    }
}

/// <summary>
/// One out-of-context WinEvent hook for "something moved on screen" (window move / resize / minimize / restore,
/// top-level windows only). Consumers (adaptive docks) throttle on their side. Hooked lazily, unhooked when unused.
/// </summary>
public static class ScreenEvents
{
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B, EVENT_SYSTEM_MINIMIZEEND = 0x0017, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;

    static readonly WinEventProc Proc = OnEvent; // kept alive for the native side
    static IntPtr _move, _loc;
    static Action? _changed;

    public static event Action? Changed
    {
        add
        {
            bool first = _changed == null;
            _changed += value;
            if (!first) return;
            _move = SetWinEventHook(EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, Proc, 0, 0, 0 /*OUTOFCONTEXT*/);
            _loc = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, Proc, 0, 0, 0);
        }
        remove
        {
            _changed -= value;
            if (_changed != null) return;
            if (_move != IntPtr.Zero) UnhookWinEvent(_move);
            if (_loc != IntPtr.Zero) UnhookWinEvent(_loc);
            _move = _loc = IntPtr.Zero;
        }
    }

    static void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return; // OBJID_WINDOW of a window, not carets/cursors
        if (ev == EVENT_OBJECT_LOCATIONCHANGE && GetAncestor(hwnd, 2 /*GA_ROOT*/) != hwnd) return;
        _changed?.Invoke();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
}
