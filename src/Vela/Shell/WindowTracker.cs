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
}
