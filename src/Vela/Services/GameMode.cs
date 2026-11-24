// Game Mode, like macOS: while a game is in front Vela turns off what costs the most (the live-capture glass and the
// window scans behind the bar) and runs at below-normal priority in Windows' efficiency mode, until the game closes.
// Detection (engine window classes, store game folders, exclusive Direct3D fullscreen, a user list) ported from WacOS
// as published under the MIT License at commit 2beced5 (Copyright (c) 2026 renzyxx; notice in THIRD_PARTY_NOTICES.md):
// src/shell/jogo.cpp. Only the game's .exe path is read (PROCESS_QUERY_LIMITED_INFORMATION), nothing more, so
// anti-cheat has nothing to object to.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vela.Core;
using static Vela.Interop.Native;

namespace Vela.Services;

public static class GameMode
{
    /// <summary>True while a game is in front (or still running after it was).</summary>
    public static bool Active { get; private set; }
    public static string? Game { get; private set; }
    public static event Action? Changed;

    static uint _pid;
    static int _gone, _tick;
    static List<string> _extra = new();
    static DateTime _extraStamp;

    // Main-window classes of game engines and libraries.
    static readonly string[] Classes = { "UnrealWindow", "UnityWndClass", "SDL_app", "GLFW30", "Valve001", "CryENGINE", "RiotWindowClass",
        "LWJGL", "SFML_Window", "OgreD3D11Wnd", "TankWindowClass" };
    // Install folders of the game stores.
    static readonly string[] StoreFolders = { @"\steamapps\common\", @"\epic games\", @"\riot games\", @"\xboxgames\", @"\ea games\",
        @"\electronic arts\", @"\ubisoft game launcher\games\", @"\gog galaxy\games\", @"\rockstar games\", @"\call of duty\", @"\overwatch\" };
    // Fullscreen things that aren't games (video, slides, browsers, launchers).
    static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase) { "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe",
        "brave.exe", "vivaldi.exe", "vlc.exe", "mpc-hc64.exe", "mpc-be64.exe", "mpv.exe", "potplayermini64.exe", "powerpnt.exe", "obs64.exe",
        "steam.exe", "steamwebhelper.exe", "epicgameslauncher.exe", "explorer.exe", "applicationframehost.exe" };

    /// <summary>Extra games, one .exe name or part of a path per line ('#' comments).</summary>
    public static string ListPath => Paths.File("games.txt");

    public static void Start()
    {
        Svc.Windows.ForegroundChanged += Check;
        Svc.Ticker.Tick += OnTick;
        Settings.Changed += key => { if (key == "gamemode" && !Settings.Current.GameMode && Active) Apply(false); };
    }

    static void OnTick(DateTime _) { if (++_tick % 2 == 0) Check(); } // every 2 s

    static void Check()
    {
        if (!Settings.Current.GameMode) return;
        if (Active)
        {
            if (HasVisibleWindow(_pid)) { _gone = 0; return; }
            if (++_gone < 2) return; // ~4 s without a window: the game closed
            Apply(false);
            return;
        }
        var fg = GetForegroundWindow();
        GetWindowThreadProcessId(fg, out uint pid);
        if (IsGame(fg, pid, out var exe, out var why))
        {
            _pid = pid; Game = exe; _gone = 0;
            Log.Info($"Game Mode: {exe} ({why})");
            Apply(true);
        }
    }

    static void Apply(bool on)
    {
        Active = on;
        if (!on) { Log.Info($"Game Mode off ({Game} closed)"); _pid = 0; _gone = 0; }
        Eco(on);
        Changed?.Invoke();
    }

    static bool IsGame(IntPtr fg, uint pid, out string exe, out string why)
    {
        exe = why = "";
        if (fg == IntPtr.Zero || pid == 0 || pid == (uint)Environment.ProcessId) return false;
        var cls = GetClassNameOf(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        var path = ExePath(pid);
        exe = System.IO.Path.GetFileName(path);
        if (exe.Length == 0 || NotGames.Contains(exe)) return false;
        foreach (var e in Extra()) if (exe.Equals(e, StringComparison.OrdinalIgnoreCase) || path.Contains(e, StringComparison.OrdinalIgnoreCase)) { why = "games.txt"; return true; }
        if (Classes.Contains(cls)) { why = "game engine window"; return true; }
        foreach (var p in StoreFolders) if (path.Contains(p, StringComparison.OrdinalIgnoreCase)) { why = "game store folder"; return true; }
        // Exclusive Direct3D fullscreen (Windows knows): a game.
        if (SHQueryUserNotificationState(out int state) == 0 && state == 3 /*QUNS_RUNNING_D3D_FULL_SCREEN*/ && CoversMonitor(fg)) { why = "Direct3D fullscreen"; return true; }
        return false;
    }

    static IEnumerable<string> Extra()
    {
        try
        {
            var stamp = File.Exists(ListPath) ? File.GetLastWriteTimeUtc(ListPath) : default;
            if (stamp != _extraStamp)
            {
                _extraStamp = stamp;
                _extra = stamp == default ? new() : File.ReadAllLines(ListPath).Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#').ToList();
            }
        }
        catch (IOException) { }
        return _extra;
    }

    static string ExePath(uint pid)
    {
        var h = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int n = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : "";
        }
        finally { CloseHandle(h); }
    }

    static bool CoversMonitor(IntPtr h)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref mi) || !GetWindowRect(h, out RECT r)) return false;
        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }

    static bool HasVisibleWindow(uint pid)
    {
        bool found = false;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            if (p == pid && IsWindowVisible(h)) { found = true; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Below-normal priority + EcoQoS (efficiency cores) while gaming; normal again after.</summary>
    static void Eco(bool on)
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            me.PriorityClass = on ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal;
            var s = new PROCESS_POWER_THROTTLING_STATE { Version = 1, ControlMask = 1 /*EXECUTION_SPEED*/, StateMask = on ? 1u : 0u };
            SetProcessInformation(me.Handle, 4 /*ProcessPowerThrottling*/, ref s, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch (Exception ex) { Log.Warn("Game Mode priority: " + ex.Message); }
    }

    [StructLayout(LayoutKind.Sequential)] struct PROCESS_POWER_THROTTLING_STATE { public uint Version, ControlMask, StateMask; }

    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, System.Text.StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, uint size);
}
