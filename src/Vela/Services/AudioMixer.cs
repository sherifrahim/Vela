using System.Diagnostics;
using System.Runtime.InteropServices;
using Vela.Core;
using Vela.Interop;

namespace Vela.Services;

/// <summary>A playback device ("Speakers", "Headphones"…).</summary>
public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// One app in the mixer. An app can own several Core Audio sessions (browsers do); they move together.
/// Holds COM references only while the volume card is open; <see cref="Release"/> drops them.
/// </summary>
public sealed class AppAudio
{
    readonly List<ISimpleAudioVolume> _volumes;
    Guid _ctx = Guid.Empty;

    internal AppAudio(string name, string? exePath, bool system, List<ISimpleAudioVolume> volumes)
    {
        Name = name; ExePath = exePath; IsSystemSounds = system; _volumes = volumes;
        if (volumes.Count > 0)
        {
            volumes[0].GetMasterVolume(out var v);
            volumes[0].GetMute(out var m);
            Volume = v * 100; Muted = m;
        }
    }

    public string Name { get; }
    public string? ExePath { get; }
    public bool IsSystemSounds { get; }
    public double Volume { get; private set; }  // 0..100
    public bool Muted { get; private set; }

    public void SetVolume(double percent)
    {
        Volume = Math.Clamp(percent, 0, 100);
        foreach (var v in _volumes) { try { v.SetMasterVolume((float)(Volume / 100), ref _ctx); } catch { } }
        if (Muted && Volume > 0) SetMuted(false);
    }

    public void SetMuted(bool muted)
    {
        Muted = muted;
        foreach (var v in _volumes) { try { v.SetMute(muted, ref _ctx); } catch { } }
    }

    public void Release()
    {
        foreach (var v in _volumes) { try { Marshal.ReleaseComObject(v); } catch { } }
        _volumes.Clear();
    }
}

/// <summary>
/// Output devices and per-app volume (Core Audio sessions), read on demand when the volume card opens:
/// nothing here polls or subscribes, so it costs nothing while the card is closed.
/// </summary>
public static class AudioMixer
{
    const int eRender = 0, DEVICE_STATE_ACTIVE = 1, CLSCTX_ALL = 23;

    /// <summary>Active playback devices, default first.</summary>
    public static List<AudioDevice> OutputDevices()
    {
        var list = new List<AudioDevice>();
        var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
        try
        {
            string? defaultId = null;
            if (en.GetDefaultAudioEndpoint(eRender, 1, out var def) == 0 && def != null)
            {
                def.GetId(out defaultId);
                Marshal.ReleaseComObject(def);
            }
            if (en.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out var ptr) != 0 || ptr == IntPtr.Zero) return list;
            var col = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(ptr);
            Marshal.Release(ptr);
            try
            {
                col.GetCount(out var n);
                for (uint i = 0; i < n; i++)
                {
                    if (col.Item(i, out var dev) != 0 || dev == null) continue;
                    try
                    {
                        dev.GetId(out var id);
                        string name = "";
                        if (dev.OpenPropertyStore(0, out var props) == 0 && props != null)
                        {
                            name = ShellNative.ReadString(props, PROPERTYKEY.DeviceFriendlyName) ?? "";
                            Marshal.ReleaseComObject(props);
                        }
                        list.Add(new AudioDevice(id, name.Length > 0 ? name : "Audio device", id == defaultId));
                    }
                    finally { Marshal.ReleaseComObject(dev); }
                }
            }
            finally { Marshal.ReleaseComObject(col); }
        }
        catch (Exception ex) { Log.Warn("Audio devices: " + ex.Message); }
        finally { Marshal.ReleaseComObject(en); }
        return list.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name).ToList();
    }

    /// <summary>Make <paramref name="id"/> the default playback device for every role (console, multimedia, communications).</summary>
    public static bool SetDefaultOutput(string id)
    {
        object? pc = null;
        try
        {
            pc = new PolicyConfigClient();
            var policy = (IPolicyConfig)pc;
            int hr = 0;
            for (int role = 0; role < 3; role++) hr |= policy.SetDefaultEndpoint(id, role);
            if (hr != 0) Log.Warn($"Default audio device: 0x{hr:X8}");
            return hr == 0;
        }
        catch (Exception ex) { Log.Warn("Default audio device: " + ex.Message); return false; }
        finally { if (pc != null) Marshal.ReleaseComObject(pc); }
    }

    /// <summary>Apps currently holding an audio session on the default playback device (system sounds last).</summary>
    public static List<AppAudio> Apps()
    {
        var byKey = new Dictionary<string, (string name, string? exe, bool sys, List<ISimpleAudioVolume> vols, bool active)>(StringComparer.OrdinalIgnoreCase);
        var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
        IMMDevice? dev = null;
        IAudioSessionManager2? mgr = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            if (en.GetDefaultAudioEndpoint(eRender, 1, out dev) != 0 || dev == null) return new();
            var iid = typeof(IAudioSessionManager2).GUID;
            if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var obj) != 0) return new();
            mgr = (IAudioSessionManager2)obj;
            if (mgr.GetSessionEnumerator(out sessions) != 0 || sessions == null) return new();
            sessions.GetCount(out var count);
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var s) != 0 || s == null) continue;
                bool keep = false;
                try
                {
                    s.GetState(out var state);           // 0 inactive, 1 active, 2 expired
                    if (state == 2) continue;
                    bool sys = s.IsSystemSoundsSession() == 0;
                    s.GetProcessId(out var pid);
                    string name, key; string? exe = null;
                    if (sys) { name = "System sounds"; key = "|system"; }
                    else
                    {
                        if (pid == 0) continue;
                        (name, exe) = Describe(pid, s);
                        if (name.Length == 0) continue;
                        key = exe ?? name;
                    }
                    var vol = (ISimpleAudioVolume)s;     // QueryInterface on the same session object
                    if (!byKey.TryGetValue(key, out var e)) e = (name, exe, sys, new List<ISimpleAudioVolume>(), false);
                    e.vols.Add(vol);
                    byKey[key] = (e.name, e.exe, e.sys, e.vols, e.active || state == 1);
                    keep = true;
                }
                catch { }
                finally { if (!keep) Marshal.ReleaseComObject(s); }
            }
        }
        catch (Exception ex) { Log.Warn("Audio sessions: " + ex.Message); }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            if (mgr != null) Marshal.ReleaseComObject(mgr);
            if (dev != null) Marshal.ReleaseComObject(dev);
            Marshal.ReleaseComObject(en);
        }
        return byKey.Values
            .OrderBy(e => e.sys).ThenByDescending(e => e.active).ThenBy(e => e.name, StringComparer.CurrentCultureIgnoreCase)
            .Select(e => new AppAudio(e.name, e.exe, e.sys, e.vols)).ToList();
    }

    /// <summary>Friendly app name: the session's own display name, else the exe's description, else the process name.</summary>
    static (string name, string? exe) Describe(uint pid, IAudioSessionControl2 s)
    {
        string? exe = null, desc = null, proc = null;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            proc = p.ProcessName;
            exe = p.MainModule?.FileName;
            if (exe != null) desc = FileVersionInfo.GetVersionInfo(exe).FileDescription;
        }
        catch { } // exited, or protected (elevated / system process): fall back to what we have
        string? display = null;
        try { if (s.GetDisplayName(out var dn) == 0 && !string.IsNullOrWhiteSpace(dn) && !dn.StartsWith('@')) display = dn; } catch { }
        var name = display ?? (string.IsNullOrWhiteSpace(desc) ? null : desc.Trim()) ?? proc ?? "";
        return (name, exe);
    }
}
