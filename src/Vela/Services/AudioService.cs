using System.Runtime.InteropServices;
using System.Windows;
using Vela.Core;
using Vela.Interop;

namespace Vela.Services;

/// <summary>
/// Default speaker / microphone volume via Core Audio (IAudioEndpointVolume).
/// Native callbacks report volume and default-device changes, so there is no idle polling.
/// The COM callbacks arrive on audio threads and are coalesced onto the WPF dispatcher.
/// </summary>
public sealed class AudioEndpoint
{
    readonly int _flow; // 0 = render (speakers), 1 = capture (mic)
    IAudioEndpointVolume? _vol;
    IAudioDeviceEnumeratorEvents? _enumerator;
    EndpointVolumeCallback? _volumeCallback;
    EndpointNotificationCallback? _deviceCallback;
    IntPtr _volumeCallbackPtr;
    int _refreshQueued, _resolveRequested;
    Guid _ctx = Guid.Empty;

    public string DeviceName { get; private set; } = "";
    public double Volume { get; private set; }  // 0..100
    public bool Muted { get; private set; }
    public bool Available => _vol != null;

    Action? _changed;
    public event Action? Changed
    {
        add
        {
            bool first = _changed == null;
            _changed += value;
            if (first) StartNotifications();
        }
        remove
        {
            _changed -= value;
            if (_changed == null) StopNotifications();
        }
    }

    public AudioEndpoint(int flow) => _flow = flow;

    void StartNotifications()
    {
        try
        {
            _volumeCallback ??= new EndpointVolumeCallback(() => QueueRefresh(false));
            _deviceCallback ??= new EndpointNotificationCallback(_flow, () => QueueRefresh(true));
            _enumerator = (IAudioDeviceEnumeratorEvents)new MMDeviceEnumeratorCoClass();
            if (_enumerator.RegisterEndpointNotificationCallback(_deviceCallback) != 0)
                Log.Warn("Audio device notifications unavailable");
        }
        catch (Exception ex)
        {
            Log.Warn("Audio notification setup failed: " + ex.Message);
            ReleaseEnumerator();
        }
        Resolve();
        Poll();
    }

    void StopNotifications()
    {
        ReleaseVolume();
        if (_enumerator != null && _deviceCallback != null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback); } catch { }
        }
        ReleaseEnumerator();
        Interlocked.Exchange(ref _refreshQueued, 0);
        Interlocked.Exchange(ref _resolveRequested, 0);
    }

    void QueueRefresh(bool resolve)
    {
        if (resolve) Interlocked.Exchange(ref _resolveRequested, 1);
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            return;
        }
        dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            if (_changed == null) return;
            double oldVolume = Volume;
            bool oldMuted = Muted;
            string oldName = DeviceName;
            if (Interlocked.Exchange(ref _resolveRequested, 0) != 0) Resolve();
            Poll();
            if (oldVolume != Volume || oldMuted != Muted || oldName != DeviceName) _changed?.Invoke();
        });
    }

    void Resolve()
    {
        ReleaseVolume();
        DeviceName = "";
        IAudioDeviceEnumeratorEvents? temporary = null;
        try
        {
            var en = _enumerator ?? (temporary = (IAudioDeviceEnumeratorEvents)new MMDeviceEnumeratorCoClass());
            if (en.GetDefaultAudioEndpoint(_flow, 1 /*eMultimedia*/, out var dev) != 0 || dev == null) return;
            try
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                if (dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out var obj) == 0)
                {
                    _vol = (IAudioEndpointVolume)obj;
                    RegisterVolumeCallback();
                }
                if (dev.OpenPropertyStore(0, out var props) == 0 && props != null)
                {
                    DeviceName = ShellNative.ReadString(props, PROPERTYKEY.DeviceFriendlyName) ?? "";
                    Marshal.ReleaseComObject(props);
                }
            }
            finally { Marshal.ReleaseComObject(dev); }
        }
        catch (Exception ex) { Log.Warn("Audio endpoint resolve failed: " + ex.Message); ReleaseVolume(); }
        finally { if (temporary != null) Marshal.ReleaseComObject(temporary); }
    }

    void RegisterVolumeCallback()
    {
        if (_vol == null || _volumeCallback == null || _changed == null) return;
        try
        {
            _volumeCallbackPtr = Marshal.GetComInterfaceForObject(_volumeCallback, typeof(IAudioEndpointVolumeCallback));
            if (_vol.RegisterControlChangeNotify(_volumeCallbackPtr) != 0)
            {
                Marshal.Release(_volumeCallbackPtr);
                _volumeCallbackPtr = IntPtr.Zero;
                Log.Warn("Audio volume notifications unavailable");
            }
        }
        catch (Exception ex)
        {
            if (_volumeCallbackPtr != IntPtr.Zero) Marshal.Release(_volumeCallbackPtr);
            _volumeCallbackPtr = IntPtr.Zero;
            Log.Warn("Audio volume notification setup failed: " + ex.Message);
        }
    }

    void ReleaseVolume()
    {
        if (_vol != null && _volumeCallbackPtr != IntPtr.Zero)
        {
            try { _vol.UnregisterControlChangeNotify(_volumeCallbackPtr); } catch { }
        }
        if (_volumeCallbackPtr != IntPtr.Zero) Marshal.Release(_volumeCallbackPtr);
        _volumeCallbackPtr = IntPtr.Zero;
        if (_vol != null) { Marshal.ReleaseComObject(_vol); _vol = null; }
    }

    void ReleaseEnumerator()
    {
        if (_enumerator != null) Marshal.ReleaseComObject(_enumerator);
        _enumerator = null;
    }

    public void Poll()
    {
        if (_vol == null) Resolve();
        if (_vol == null) return;
        try
        {
            if (_vol.GetMasterVolumeLevelScalar(out float v) == 0) Volume = Math.Round(v * 100);
            if (_vol.GetMute(out bool m) == 0) Muted = m;
        }
        catch { ReleaseVolume(); }
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

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioDeviceEnumeratorEvents
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IAudioDeviceNotificationClient client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IAudioDeviceNotificationClient client);
}

[ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioDeviceNotificationClient
{
    [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
    [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
    [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, AudioPropertyKey key);
}

[ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(IntPtr data);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class EndpointVolumeCallback(Action changed) : IAudioEndpointVolumeCallback
{
    public int OnNotify(IntPtr data) { changed(); return 0; }
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class EndpointNotificationCallback(int flow, Action changed) : IAudioDeviceNotificationClient
{
    public int OnDeviceStateChanged(string id, int state) { changed(); return 0; }
    public int OnDeviceAdded(string id) { changed(); return 0; }
    public int OnDeviceRemoved(string id) { changed(); return 0; }
    public int OnPropertyValueChanged(string id, AudioPropertyKey key) { changed(); return 0; }
    public int OnDefaultDeviceChanged(int changedFlow, int role, string? id)
    {
        if ((changedFlow == flow || changedFlow == 2 /*eAll*/) && role == 1 /*eMultimedia*/) changed();
        return 0;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct AudioPropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}
