using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Vela.Interop;

// Minimal shell COM interfaces: item images, shortcuts, property stores, Core Audio.
// Method order MUST match the native vtable; do not reorder or remove members.

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
    [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
    [PreserveSig] int Commit();
}

[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr pfd, uint flags);
    [PreserveSig] int GetIDList(out IntPtr ppidl);
    [PreserveSig] int SetIDList(IntPtr pidl);
    [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
    [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
    [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
    [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
    [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
    [PreserveSig] int GetHotkey(out short hotkey);
    [PreserveSig] int SetHotkey(short hotkey);
    [PreserveSig] int GetShowCmd(out int cmd);
    [PreserveSig] int SetShowCmd(int cmd);
    [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int icon);
    [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int icon);
    [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string rel, uint reserved);
    [PreserveSig] int Resolve(IntPtr hwnd, uint flags);
    [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
}

[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkCoClass { }

// ---------- Core Audio ----------
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorCoClass { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore props);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid ctx);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
    [PreserveSig] int GetMasterVolumeLevel(out float level);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint ch, float level, ref Guid ctx);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
    [PreserveSig] int GetChannelVolumeLevel(uint ch, out float level);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

internal static class ShellNative
{
    public const int SIIGBF_RESIZETOFIT = 0x0;
    public const int SIIGBF_BIGGERSIZEOK = 0x1;
    public const int SIIGBF_ICONONLY = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shell32.dll")]
    public static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    public static Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    public static Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    public static string? ReadString(IPropertyStore store, PROPERTYKEY key)
    {
        try
        {
            if (store.GetValue(ref key, out var pv) != 0) return null;
            try { return pv.AsString(); }
            finally { Native.PropVariantClear(ref pv); }
        }
        catch { return null; }
    }

    /// <summary>AppUserModelID of a top-level window (groups UWP frames and multi-process apps).</summary>
    public static string? GetWindowAumid(IntPtr hwnd)
    {
        IPropertyStore? store = null;
        try
        {
            var iid = IID_IPropertyStore;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store == null) return null;
            var s = ReadString(store, PROPERTYKEY.AppUserModelId);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch { return null; }
        finally { if (store != null) Marshal.ReleaseComObject(store); }
    }

    public sealed record ShortcutInfo(string? Target, string? Arguments, string? Aumid);

    public static ShortcutInfo? ReadShortcut(string lnkPath)
    {
        object? link = null;
        try
        {
            link = new ShellLinkCoClass();
            ((IPersistFile)link).Load(lnkPath, 0);
            var sl = (IShellLinkW)link;
            var sb = new StringBuilder(1024);
            sl.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            string? target = sb.Length > 0 ? sb.ToString() : null;
            var args = new StringBuilder(1024);
            sl.GetArguments(args, args.Capacity);
            string? aumid = link is IPropertyStore ps ? ReadString(ps, PROPERTYKEY.AppUserModelId) : null;
            return new ShortcutInfo(target, args.Length > 0 ? args.ToString() : null, string.IsNullOrWhiteSpace(aumid) ? null : aumid);
        }
        catch { return null; }
        finally { if (link != null) Marshal.ReleaseComObject(link); }
    }
}
