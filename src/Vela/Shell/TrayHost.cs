using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vela.Core;

namespace Vela.Shell;

/// <summary>An app's notification-area ("tray") icon, as the app last described it.</summary>
public sealed class TrayIcon
{
    public required IntPtr Hwnd { get; init; }
    public required uint Id { get; init; }
    public Guid Guid { get; init; }
    public uint CallbackMessage { get; set; }
    public uint Version { get; set; }
    public string Tip { get; set; } = "";
    public ImageSource? Image { get; set; }
    public bool Hidden { get; set; }
    internal string Key => Guid != Guid.Empty ? Guid.ToString() : $"{Hwnd}:{Id}";

    const uint WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205,
               WM_CONTEXTMENU = 0x7B, NIN_SELECT = 0x400;

    /// <summary>Tell the app its icon was clicked, the way the Windows tray does (versions 0–3 and 4 differ).</summary>
    public void Click(bool right, bool twice = false)
    {
        if (CallbackMessage == 0 || !TrayHost.IsWindow(Hwnd)) return;
        TrayHost.GetWindowThreadProcessId(Hwnd, out uint pid);
        TrayHost.AllowSetForegroundWindow(pid); // so its menu / window can come to the front
        TrayHost.GetCursorPos(out var pt);
        var msgs = right ? new[] { WM_RBUTTONDOWN, WM_RBUTTONUP } : twice ? new[] { WM_LBUTTONDOWN, WM_LBUTTONUP, WM_LBUTTONDBLCLK, WM_LBUTTONUP } : new[] { WM_LBUTTONDOWN, WM_LBUTTONUP };
        if (Version >= 4)
        {
            var wParam = (IntPtr)((pt.Y << 16) | (pt.X & 0xFFFF));
            foreach (var m in msgs.Append(right ? WM_CONTEXTMENU : NIN_SELECT))
                TrayHost.PostMessage(Hwnd, CallbackMessage, wParam, (IntPtr)((Id << 16) | m));
        }
        else
            foreach (var m in msgs) TrayHost.PostMessage(Hwnd, CallbackMessage, (IntPtr)Id, (IntPtr)m);
    }
}

/// <summary>
/// Shows apps' tray icons inside Vela while Explorer keeps its own tray (approach used by other taskbar replacements):
/// a hidden top-most window of class "Shell_TrayWnd" on its own thread receives what apps send with Shell_NotifyIcon
/// (WM_COPYDATA, dwData 1), keeps a copy, and FORWARDS every message to Explorer's real tray unchanged — including
/// appbar messages (dwData 0) and icon-position queries — so Windows behaves exactly as before. "TaskbarCreated" is
/// broadcast once so running apps re-add their icons. Runs only while a Tray widget is on a dock; when it stops, the
/// window is destroyed and Explorer is the only tray again (nothing to restore).
/// </summary>
public sealed class TrayHost
{
    readonly Dictionary<string, TrayIcon> _icons = new();
    readonly object _lock = new();
    Action? _changed;
    Thread? _thread;
    IntPtr _hwnd;
    uint _threadId;
    WndProc? _proc; // kept alive for the native side
    static uint _taskbarCreated;
    /// <summary>Vela's own TaskbarCreated broadcast; the docks ignore it (it is not an Explorer restart).</summary>
    public static DateTime SelfBroadcastUntil { get; private set; }

    public IReadOnlyList<TrayIcon> Icons { get { lock (_lock) return _icons.Values.Where(i => !i.Hidden).ToList(); } }
    public bool Running => _hwnd != IntPtr.Zero;

    public event Action? Changed
    {
        add { bool first = _changed == null; _changed += value; if (first) Start(); }
        remove { _changed -= value; if (_changed == null) Stop(); }
    }

    void Start()
    {
        if (_thread != null) return;
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Vela tray host" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(3000);
        Svc.Ticker.Tick += KeepOnTop;
    }

    /// <summary>Destroy the window; Explorer, which got every message all along, is the tray again.</summary>
    public void Stop()
    {
        Svc.Ticker.Tick -= KeepOnTop;
        var t = _thread;
        if (t == null) return;
        if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        else if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        t.Join(3000);
        _thread = null;
        lock (_lock) _icons.Clear();
    }

    void Run(ManualResetEventSlim ready)
    {
        try
        {
            _threadId = GetCurrentThreadId();
            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            _proc = WndProcImpl;
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                hInstance = GetModuleHandle(null), lpszClassName = "Shell_TrayWnd",
            };
            if (RegisterClassEx(ref wc) == 0) { Log.Warn("Tray host: RegisterClassEx failed " + Marshal.GetLastWin32Error()); return; }
            _hwnd = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_TOPMOST, "Shell_TrayWnd", "", WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) { Log.Warn("Tray host: CreateWindowEx failed " + Marshal.GetLastWin32Error()); return; }
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            ready.Set();
            // Ask running apps to re-add their icons (they do this whenever a new taskbar appears).
            SelfBroadcastUntil = DateTime.Now.AddSeconds(3);
            SendNotifyMessage(HWND_BROADCAST, _taskbarCreated, IntPtr.Zero, IntPtr.Zero);
            Log.Info("Tray host started");
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
        }
        catch (Exception ex) { Log.Error("Tray host", ex); }
        finally
        {
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
            UnregisterClass("Shell_TrayWnd", GetModuleHandle(null));
            ready.Set();
            Log.Info("Tray host stopped");
        }
    }

    /// <summary>Explorer restarts put its own tray window above ours: stay first in line (cheap, 1 Hz).</summary>
    void KeepOnTop(DateTime now)
    {
        if (_hwnd == IntPtr.Zero) return;
        var first = FindWindow("Shell_TrayWnd", null);
        if (first != _hwnd) SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Explorer's own Shell_TrayWnd (never ours): the window everything is forwarded to.</summary>
    public static IntPtr ExplorerTray()
    {
        var h = IntPtr.Zero;
        while ((h = FindWindowEx(IntPtr.Zero, h, "Shell_TrayWnd", null)) != IntPtr.Zero)
        {
            GetWindowThreadProcessId(h, out uint pid);
            try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); if (p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return h; }
            catch { }
        }
        return IntPtr.Zero;
    }

    IntPtr WndProcImpl(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CLOSE) { DestroyWindow(hwnd); return IntPtr.Zero; }
        if (msg == WM_DESTROY) { _hwnd = IntPtr.Zero; PostQuitMessage(0); return IntPtr.Zero; }
        if (msg == WM_COPYDATA)
        {
            try
            {
                var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
                if (cds.dwData == (IntPtr)1 && cds.lpData != IntPtr.Zero) OnNotifyIcon(cds);
            }
            catch (Exception ex) { Log.Warn("Tray host: " + ex.Message); }
            return Forward(msg, wParam, lParam, fallback: (IntPtr)1);
        }
        if (msg >= WM_USER) return Forward(msg, wParam, lParam, fallback: IntPtr.Zero);
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>Pass the message on to Explorer's tray and return its answer (so appbars, icon positions… keep working).</summary>
    static IntPtr Forward(uint msg, IntPtr wParam, IntPtr lParam, IntPtr fallback)
    {
        var explorer = ExplorerTray();
        if (explorer == IntPtr.Zero) return fallback;
        return SendMessageTimeout(explorer, msg, wParam, lParam, SMTO_ABORTIFHUNG | SMTO_BLOCK, 2000, out var result) != IntPtr.Zero ? result : fallback;
    }

    // ---------- Shell_NotifyIcon payload: SHELLTRAYDATA { int signature; uint message; NOTIFYICONDATAW32 nid } ----------

    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_STATE = 0x8, NIF_GUID = 0x20;
    const uint NIS_HIDDEN = 0x1;

    void OnNotifyIcon(COPYDATASTRUCT cds)
    {
        int size = cds.cbData;
        if (size < 8 + 24) return;
        var p = cds.lpData;
        if (Marshal.ReadInt32(p, 0) != 0x34753423) return; // SHELLTRAYDATA signature
        uint message = (uint)Marshal.ReadInt32(p, 4);
        var n = p + 8;
        int nSize = Math.Min(size - 8, Marshal.ReadInt32(n, 0));
        uint U(int off) => off + 4 <= nSize ? (uint)Marshal.ReadInt32(n, off) : 0;
        var hwnd = (IntPtr)(int)U(4);            // 32-bit handles, sign-extended like Windows does
        uint id = U(8), flags = U(12), callback = U(16), hIcon = U(20);
        string? tip = (flags & NIF_TIP) != 0 && nSize >= 24 + 256 ? Marshal.PtrToStringUni(n + 24, 128).Split('\0')[0] : null;
        uint state = U(280), stateMask = U(284), version = U(800);
        var guid = (flags & NIF_GUID) != 0 && nSize >= 952 ? Marshal.PtrToStructure<Guid>(n + 936) : Guid.Empty;
        ImageSource? image = (flags & NIF_ICON) != 0 && hIcon != 0 ? ToImage((IntPtr)(int)hIcon) : null;

        var key = guid != Guid.Empty ? guid.ToString() : $"{hwnd}:{id}";
        bool changed = false;
        lock (_lock)
        {
            switch (message)
            {
                case NIM_ADD:
                case NIM_MODIFY:
                    if (!_icons.TryGetValue(key, out var icon))
                    {
                        icon = new TrayIcon { Hwnd = hwnd, Id = id, Guid = guid };
                        _icons[key] = icon;
                    }
                    if ((flags & NIF_MESSAGE) != 0) icon.CallbackMessage = callback;
                    if (image != null) icon.Image = image;
                    if (tip != null) icon.Tip = tip;
                    if ((flags & NIF_STATE) != 0 && (stateMask & NIS_HIDDEN) != 0) icon.Hidden = (state & NIS_HIDDEN) != 0;
                    changed = true;
                    break;
                case NIM_DELETE:
                    changed = _icons.Remove(key);
                    break;
                case NIM_SETVERSION:
                    if (_icons.TryGetValue(key, out var v)) v.Version = version;
                    break;
            }
            // Forget icons whose app has gone away without saying so.
            foreach (var dead in _icons.Where(kv => !IsWindow(kv.Value.Hwnd)).Select(kv => kv.Key).ToList()) { _icons.Remove(dead); changed = true; }
        }
        if (changed) Application.Current?.Dispatcher.BeginInvoke(() => _changed?.Invoke());
    }

    /// <summary>Copy the app's icon right away (it may destroy its handle later) into a frozen WPF image.</summary>
    static ImageSource? ToImage(IntPtr hIcon)
    {
        var copy = CopyIcon(hIcon);
        if (copy == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(copy, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally { DestroyIcon(copy); }
    }

    // ---------- Win32 ----------

    delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }

    const uint WM_CLOSE = 0x10, WM_DESTROY = 0x2, WM_COPYDATA = 0x4A, WM_USER = 0x400, WM_QUIT = 0x12;
    const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
    const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SMTO_BLOCK = 0x1, SMTO_ABORTIFHUNG = 0x2;
    static readonly IntPtr HWND_TOPMOST = new(-1), HWND_BROADCAST = new(0xFFFF);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool UnregisterClass(string cls, IntPtr hInstance);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool SendNotifyMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? name);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern bool AllowSetForegroundWindow(uint pid);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] static extern IntPtr CopyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
