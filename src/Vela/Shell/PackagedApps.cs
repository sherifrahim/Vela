using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Vela.Core;

namespace Vela.Shell;

/// <summary>
/// Store (MSIX) apps such as Claude or ChatGPT: their .exe lives in a versioned folder under
/// "C:\Program Files\WindowsApps" that changes with every update and can't be started directly (access denied).
/// Their stable identity is the AppUserModelID ("Family!AppId"), launched like Start does: shell:AppsFolder\AUMID.
/// </summary>
public static class PackagedApps
{
    const string AppsFolder = "shell:AppsFolder\\";

    public static bool IsPackagedPath(string? path) =>
        path != null && path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    public static string LaunchPathFor(string aumid) => AppsFolder + aumid;

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder? aumid);

    /// <summary>The AUMID of a packaged process (null for ordinary desktop apps).</summary>
    public static string? AumidForProcess(uint pid)
    {
        var h = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            uint len = 131; // APPLICATION_USER_MODEL_ID_MAX_LENGTH
            var sb = new StringBuilder((int)len);
            return GetApplicationUserModelId(h, ref len, sb) == 0 && sb.Length > 0 ? sb.ToString() : null;
        }
        catch (EntryPointNotFoundException) { return null; }
        finally { CloseHandle(h); }
    }

    /// <summary>
    /// The AUMID for an .exe path inside WindowsApps, even when that version is gone: the folder name gives the package
    /// family ("Name_Version_Arch_Resource_Publisher" → "Name_Publisher"); the installed version's manifest gives the app id.
    /// </summary>
    public static string? AumidForPackagedPath(string exePath)
    {
        try
        {
            int i = exePath.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            var rest = exePath[(i + @"\WindowsApps\".Length)..];
            int slash = rest.IndexOf('\\');
            if (slash <= 0) return null;
            var folder = rest[..slash];
            var relExe = rest[(slash + 1)..];
            var parts = folder.Split('_');
            if (parts.Length < 5) return null;
            var family = parts[0] + "_" + parts[^1];
            var pm = new Windows.Management.Deployment.PackageManager();
            foreach (var pkg in pm.FindPackagesForUser(string.Empty, family))
            {
                var manifest = Path.Combine(pkg.InstalledLocation.Path, "AppxManifest.xml");
                if (!File.Exists(manifest)) continue;
                var apps = XDocument.Load(manifest).Descendants().Where(e => e.Name.LocalName == "Application").ToList();
                var app = apps.FirstOrDefault(a => string.Equals((string?)a.Attribute("Executable"), relExe, StringComparison.OrdinalIgnoreCase)) ?? apps.FirstOrDefault();
                if ((string?)app?.Attribute("Id") is { Length: > 0 } id) return family + "!" + id;
            }
        }
        catch (Exception ex) { Log.Warn($"Packaged app lookup for {exePath}: {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// Pins saved with a versioned WindowsApps path move to the stable AUMID, and duplicates (the same app pinned before
    /// and after an update) merge, keeping the first position. Returns true when something changed.
    /// </summary>
    public static bool FixPins(List<PinnedApp> pins)
    {
        bool changed = false;
        foreach (var p in pins)
        {
            if (p.Aumid != null && p.LaunchPath.StartsWith(AppsFolder, StringComparison.OrdinalIgnoreCase)) continue;
            var path = IsPackagedPath(p.LaunchPath) ? p.LaunchPath : IsPackagedPath(p.TargetPath) ? p.TargetPath : null;
            if (path == null) continue;
            var aumid = p.Aumid ?? AumidForPackagedPath(path);
            if (aumid == null) continue;
            Log.Info($"Pin '{p.Name}': {path} → {aumid}");
            p.Aumid = aumid;
            p.LaunchPath = LaunchPathFor(aumid);
            p.TargetPath = null; // versioned; windows match by AUMID
            changed = true;
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int before = pins.Count;
        pins.RemoveAll(p => p.Aumid != null && !seen.Add(p.Aumid));
        if (pins.Count != before) { Log.Info($"Merged {before - pins.Count} duplicate pin(s)"); changed = true; }
        return changed;
    }
}
