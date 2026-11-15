using System.Runtime.InteropServices;
using Microsoft.Win32;
using Vela.Core;
using Vela.Interop;
using static Vela.Interop.Native;

namespace Vela.Shell;

/// <summary>Small one-shot Windows actions used by widgets (Start, Show desktop, theme...).</summary>
public static class WinActions
{
    const ushort VK_LWIN = 0x5B;

    public static void SendKeys(params ushort[] vks)
    {
        var inputs = new List<INPUT>();
        foreach (var vk in vks) inputs.Add(Key(vk, false));
        foreach (var vk in vks.Reverse()) inputs.Add(Key(vk, true));
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    static INPUT Key(ushort vk, bool up) => new()
    {
        type = 1,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
    };

    public static void OpenStart() => SendKeys(VK_LWIN);
    public static void ShowDesktop() => SendKeys(VK_LWIN, 0x44 /*D*/);
    public static void OpenNotificationCenter() => SendKeys(VK_LWIN, 0x4E /*N*/);
    public static void OpenQuickSettings() => SendKeys(VK_LWIN, 0x41 /*A*/);
    public static void OpenTaskManager() => WindowTracker.Launch("taskmgr.exe");
    public static void OpenSettings(string page) => WindowTracker.OpenUri("ms-settings:" + page);

    // ---------- light / dark ----------
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsSystemLight()
    {
        using var k = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
    }

    public static void SetSystemLight(bool light)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(PersonalizeKey))
        {
            k.SetValue("AppsUseLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
            k.SetValue("SystemUsesLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
        }
        SendMessageTimeout(new IntPtr(0xFFFF), WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", 2, 200, out _);
    }

    // ---------- recycle bin ----------
    public static (long items, long bytes) RecycleBinInfo()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? (info.i64NumItems, info.i64Size) : (0, 0);
    }

    /// <summary>Empties the bin. Windows shows its own confirmation dialog (we don't pass NOCONFIRMATION).</summary>
    public static void EmptyRecycleBin() => SHEmptyRecycleBin(IntPtr.Zero, null, 0);

    public static void TrimWorkingSet()
    {
        try { SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); }
        catch (Exception ex) { Log.Warn("Trim failed: " + ex.Message); }
    }
}

/// <summary>Reads the user's own Windows taskbar pins (one-time import).</summary>
public static class TaskbarPins
{
    static string PinFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    public static List<PinnedApp> Read()
    {
        var list = new List<PinnedApp>();
        try
        {
            if (!Directory.Exists(PinFolder)) return list;
            foreach (var lnk in Directory.GetFiles(PinFolder, "*.lnk").OrderBy(File.GetCreationTime))
            {
                var info = ShellNative.ReadShortcut(lnk);
                list.Add(new PinnedApp
                {
                    Name = Path.GetFileNameWithoutExtension(lnk),
                    LaunchPath = lnk,
                    TargetPath = info?.Target,
                    Aumid = info?.Aumid,
                });
            }
        }
        catch (Exception ex) { Log.Error("Reading taskbar pins failed", ex); }
        return list;
    }
}
