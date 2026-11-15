using System.Runtime.InteropServices;
using Vela.Core;
using Vela.Interop;

namespace Vela.Services;

/// <summary>
/// Default speaker / microphone volume via Core Audio (IAudioEndpointVolume).
/// Polled at 1 Hz while a widget listens; the endpoint is re-resolved every few seconds
/// so switching output devices is picked up.
/// </summary>
public sealed class AudioEndpoint
{
    readonly int _flow; // 0 = render (speakers), 1 = capture (mic)
    IAudioEndpointVolume? _vol;
    int _refreshCountdown;
    Guid _ctx = Guid.Empty;

    public string DeviceName { get; private set; } = "";
    public double Volume { get; private set; }  // 0..100
    public bool Muted { get; private set; }
    public bool Available => _vol != null;

    Action? _changed;
    public event Action? Changed
    {
        add { bool first = _changed == null; _changed += value; if (first) { Poll(); Services.Ticker.Tick += OnTick; } }
        remove { _changed -= value; if (_changed == null) Services.Ticker.Tick -= OnTick; }
    }

    public AudioEndpoint(int flow) => _flow = flow;

    void OnTick(DateTime _)
    {
        double v = Volume; bool m = Muted; string n = DeviceName;
        Poll();
        if (v != Volume || m != Muted || n != DeviceName) _changed?.Invoke();
    }

    void Resolve()
    {
        try
        {
            if (_vol != null) { Marshal.ReleaseComObject(_vol); _vol = null; }
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
            try
            {
                if (en.GetDefaultAudioEndpoint(_flow, 1 /*eMultimedia*/, out var dev) != 0 || dev == null) { DeviceName = ""; return; }
                try
                {
                    var iid = typeof(IAudioEndpointVolume).GUID;
                    if (dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out var obj) == 0) _vol = (IAudioEndpointVolume)obj;
                    if (dev.OpenPropertyStore(0, out var props) == 0 && props != null)
                    {
                        DeviceName = ShellNative.ReadString(props, PROPERTYKEY.DeviceFriendlyName) ?? "";
                        Marshal.ReleaseComObject(props);
                    }
                }
                finally { Marshal.ReleaseComObject(dev); }
            }
            finally { Marshal.ReleaseComObject(en); }
        }
        catch (Exception ex) { Log.Warn("Audio endpoint resolve failed: " + ex.Message); _vol = null; }
    }

    public void Poll()
    {
        if (_vol == null || --_refreshCountdown <= 0) { Resolve(); _refreshCountdown = 5; }
        if (_vol == null) return;
        try
        {
            if (_vol.GetMasterVolumeLevelScalar(out float v) == 0) Volume = Math.Round(v * 100);
            if (_vol.GetMute(out bool m) == 0) Muted = m;
        }
        catch { _vol = null; }
    }

    public void SetVolume(double percent)
    {
        if (_vol == null) Resolve();
        if (_vol == null) return;
        _vol.SetMasterVolumeLevelScalar((float)Math.Clamp(percent / 100.0, 0, 1), ref _ctx);
        if (Muted && percent > 0) _vol.SetMute(false, ref _ctx);
        Poll();
        _changed?.Invoke();
    }

    public void ToggleMute()
    {
        if (_vol == null) Resolve();
        if (_vol == null) return;
        _vol.SetMute(!Muted, ref _ctx);
        Poll();
        _changed?.Invoke();
    }

    /// <summary>Short name like "Speakers" from "Speakers (Realtek(R) Audio)".</summary>
    public string ShortName
    {
        get
        {
            if (string.IsNullOrEmpty(DeviceName)) return _flow == 0 ? "No output" : "No microphone";
            int p = DeviceName.IndexOf(" (", StringComparison.Ordinal);
            return p > 0 ? DeviceName[..p] : DeviceName;
        }
    }
}
