using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Vela.Core;
using static Vela.Interop.Native;

namespace Vela.Services;

/// <summary>
/// CPU / RAM / network / battery from cheap native calls (no PerformanceCounter, no WMI).
/// Samples only while at least one widget is subscribed to <see cref="Updated"/>.
/// </summary>
public sealed class SystemMonitor
{
    const int HistoryLength = 40;
    ulong _lastIdle, _lastTotal;
    long _lastRx, _lastTx;
    DateTime _lastNetSample;
    int _batteryCountdown;

    public double Cpu { get; private set; }
    public double Ram { get; private set; }
    public double RamUsedGb { get; private set; }
    public double RamTotalGb { get; private set; }
    public double NetDown { get; private set; }
    public double NetUp { get; private set; }
    public bool HasBattery { get; private set; }
    public int BatteryPercent { get; private set; } = 100;
    public bool Charging { get; private set; }
    public bool OnAc { get; private set; } = true;

    public Queue<double> CpuHistory { get; } = new();
    public Queue<double> RamHistory { get; } = new();
    public Queue<double> NetHistory { get; } = new();

    Action? _updated;
    /// <summary>Raised on the UI thread once per second while anyone listens.</summary>
    public event Action? Updated
    {
        add { bool first = _updated == null; _updated += value; if (first) { Sample(); Services.Ticker.Tick += OnTick; } }
        remove { _updated -= value; if (_updated == null) Services.Ticker.Tick -= OnTick; }
    }

    public SystemMonitor() => SampleBattery();

    void OnTick(DateTime now)
    {
        Sample();
        _updated?.Invoke();
    }

    void Sample()
    {
        // CPU
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong i = idle.Value, total = kernel.Value + user.Value; // kernel includes idle
            if (_lastTotal != 0 && total > _lastTotal)
            {
                double busy = 1.0 - (double)(i - _lastIdle) / (total - _lastTotal);
                Cpu = Math.Clamp(busy * 100, 0, 100);
            }
            _lastIdle = i; _lastTotal = total;
        }

        // RAM
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            Ram = m.dwMemoryLoad;
            RamTotalGb = m.ullTotalPhys / 1073741824.0;
            RamUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        }

        // Network
        try
        {
            long rx = 0, tx = 0;
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var st = ni.GetIPStatistics();
                rx += st.BytesReceived; tx += st.BytesSent;
            }
            var now = DateTime.UtcNow;
            if (_lastNetSample != default)
            {
                double secs = Math.Max(0.25, (now - _lastNetSample).TotalSeconds);
                NetDown = Math.Max(0, (rx - _lastRx) / secs);
                NetUp = Math.Max(0, (tx - _lastTx) / secs);
            }
            _lastRx = rx; _lastTx = tx; _lastNetSample = now;
        }
        catch { /* adapters can vanish mid-enumeration */ }

        Push(CpuHistory, Cpu);
        Push(RamHistory, Ram);
        Push(NetHistory, NetDown);

        if (--_batteryCountdown <= 0) { SampleBattery(); _batteryCountdown = 10; }
    }

    public void SampleBattery()
    {
        if (!GetSystemPowerStatus(out var s)) return;
        HasBattery = (s.BatteryFlag & 128) == 0 && s.BatteryFlag != 255;
        BatteryPercent = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : 100;
        OnAc = s.ACLineStatus == 1;
        Charging = (s.BatteryFlag & 8) != 0 || (OnAc && BatteryPercent < 100);
    }

    static void Push(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > HistoryLength) q.Dequeue();
    }

    // ---------- drives ----------
    public static IEnumerable<DriveInfo> FixedDrives() =>
        DriveInfo.GetDrives().Where(d => { try { return d.IsReady && d.DriveType == DriveType.Fixed; } catch { return false; } });
}

/// <summary>
/// GPU 3D-engine load and dedicated VRAM use from Windows performance counters (PDH), the same numbers
/// Task Manager shows. Opened lazily, sampled every 2 s and only while someone listens.
/// </summary>
public sealed class GpuMonitor : IDisposable
{
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string? src, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
    const uint PDH_FMT_DOUBLE = 0x200, PDH_FMT_NOCAP100 = 0x8000;
    const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    IntPtr _query, _engine, _vram;
    bool _failed;
    int _skip;
    Action? _updated;

    public double Load { get; private set; } = -1;     // %, -1 = unknown
    public double VramGb { get; private set; } = -1;
    public Queue<double> History { get; } = new();

    public event Action? Updated
    {
        add { bool first = _updated == null; _updated += value; if (first) { Open(); Services.Ticker.Tick += OnTick; } }
        remove { _updated -= value; if (_updated == null) { Services.Ticker.Tick -= OnTick; Close(); } }
    }

    void Open()
    {
        if (_failed || _query != IntPtr.Zero) return;
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) { _failed = true; return; }
            PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _engine);
            PdhAddEnglishCounter(_query, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out _vram);
            PdhCollectQueryData(_query);
        }
        catch (Exception ex) { _failed = true; Log.Warn("GPU counters unavailable: " + ex.Message); }
    }

    void Close()
    {
        if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = _engine = _vram = IntPtr.Zero; }
    }

    void OnTick(DateTime now)
    {
        if (++_skip < 2) return;
        _skip = 0;
        if (_query == IntPtr.Zero) return;
        if (PdhCollectQueryData(_query) != 0) return;
        double load = Sum(_engine, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100);
        double vram = Sum(_vram, PDH_FMT_DOUBLE);
        if (load >= 0) { Load = Math.Clamp(load, 0, 100); History.Enqueue(Load); while (History.Count > 40) History.Dequeue(); }
        if (vram >= 0) VramGb = vram / 1073741824.0;
        _updated?.Invoke();
    }

    /// <summary>Sum of all instances of a wildcard counter, or -1.</summary>
    static double Sum(IntPtr counter, uint format)
    {
        if (counter == IntPtr.Zero) return -1;
        uint bytes = 0;
        int rc = PdhGetFormattedCounterArray(counter, format, ref bytes, out uint n, IntPtr.Zero);
        if (rc != PDH_MORE_DATA || bytes == 0) return -1;
        var buf = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (PdhGetFormattedCounterArray(counter, format, ref bytes, out n, buf) != 0) return -1;
            // PDH_FMT_COUNTERVALUE_ITEM_W: { LPWSTR name; { DWORD status; double value } } = 24 bytes on x64.
            int size = IntPtr.Size + 16;
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                var item = buf + i * size;
                if (Marshal.ReadInt32(item + IntPtr.Size) == 0)
                    sum += BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + IntPtr.Size + 8));
            }
            return sum;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void Dispose() => Close();
}
