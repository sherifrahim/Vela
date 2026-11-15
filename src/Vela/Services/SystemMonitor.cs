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
