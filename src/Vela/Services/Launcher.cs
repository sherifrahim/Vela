// Search launcher: what it finds and how it ranks. The design follows WacOS's Spotlight as published under the MIT
// License at commit 2beced5 (Copyright (c) 2026 renzyxx; notice in THIRD_PARTY_NOTICES.md), src/shell/spotlight.cpp:
// apps from the Applications folder, settings pages, a calculator, files from the Windows Search index, the tiered
// name score (whole name > start > word start > initials > anywhere) and learning from what you open. Vela's own C#.
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Vela.Core;
using Vela.Shell;

namespace Vela.Services;

public enum ResultKind { App, Setting, Command, Calc, File, Folder, Web }

public sealed class SearchResult
{
    public required ResultKind Kind { get; init; }
    public required string Name { get; init; }
    public string Detail { get; init; } = "";
    /// <summary>For the icon cache: an exe/lnk/file path or "shell:AppsFolder\…". Null: <see cref="Glyph"/>.</summary>
    public string? IconSource { get; init; }
    public string? Glyph { get; init; }
    public int Score { get; set; }
    /// <summary>Usage key (learning), null when not learned.</summary>
    public string? Key { get; init; }
    /// <summary>File or folder path (Ctrl+Enter shows it in Explorer).</summary>
    public string? Path { get; init; }
    public required Action Open { get; init; }
}

public static class Launcher
{
    // ---------------------------------------------------------------- matching

    /// <summary>Lower case, no accents ("configuracoes" finds "Configurações").</summary>
    public static string Fold(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>0 = no match. Whole name 120 › start 100 › start of a word 80 › initials 70 › anywhere 50. <paramref name="q"/> folded.</summary>
    public static int Score(string name, string q)
    {
        if (q.Length == 0) return 0;
        var n = Fold(name);
        if (n == q) return 120;
        if (n.StartsWith(q, StringComparison.Ordinal)) return 100;
        for (int i = 1; i < n.Length; i++)
            if (n[i - 1] is ' ' or '-' or '.' or '_' or '(' && string.CompareOrdinal(n, i, q, 0, q.Length) == 0) return 80;
        if (q.Length >= 2)
        {
            var ini = new StringBuilder();
            bool start = true;
            foreach (var c in n) { if (start && char.IsLetterOrDigit(c)) ini.Append(c); start = c is ' ' or '-'; }
            if (ini.ToString().StartsWith(q, StringComparison.Ordinal)) return 70;
        }
        return n.Contains(q, StringComparison.Ordinal) ? 50 : 0;
    }

    /// <summary>Best of the name and its keywords (a keyword hit ranks just under a name hit of the same kind).</summary>
    static int ScoreWithWords(string name, string words, string q)
    {
        int best = Score(name, q);
        if (best >= 100 || words.Length == 0) return best;
        foreach (var w in words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (w == q) best = Math.Max(best, 90);
            else if (w.StartsWith(q, StringComparison.Ordinal)) best = Math.Max(best, q.Length >= 2 ? 75 : 0);
        }
        return best;
    }

    // ---------------------------------------------------------------- apps (shell:AppsFolder)

    public sealed record AppEntry(string Name, string Parsing, int Use);

    static volatile IReadOnlyList<AppEntry> _apps = Array.Empty<AppEntry>();
    static DateTime _appsAt;
    static int _loading;

    /// <summary>Read the Applications folder now, on this (STA) thread: off-screen renders.</summary>
    internal static void LoadAppsNow() { _apps = LoadApps(); _appsAt = DateTime.UtcNow; }

    /// <summary>Re-read the Applications folder on a background thread when older than 10 minutes (or never read).</summary>
    public static void RefreshApps(Action? done = null)
    {
        if ((DateTime.UtcNow - _appsAt).TotalMinutes < 10 && _apps.Count > 0) return;
        if (Interlocked.Exchange(ref _loading, 1) == 1) return;
        var t = new Thread(() =>
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _apps = LoadApps();
                _appsAt = DateTime.UtcNow;
                Log.Info($"Launcher: {_apps.Count} apps in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex) { Log.Warn("Launcher apps: " + ex.Message); }
            finally { Interlocked.Exchange(ref _loading, 0); }
            if (done != null) System.Windows.Application.Current?.Dispatcher.BeginInvoke(done);
        }) { IsBackground = true, Name = "Vela launcher apps" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    static readonly string[] SkipWords = { "uninstall", "readme", "read me", "website", "manual", "documentation", "release notes", "license", "help" };

    static List<AppEntry> LoadApps()
    {
        var found = new Dictionary<string, (string Name, string Parsing)>();
        var iid = typeof(IShellItem).GUID;
        if (Interop.ShellNative.SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ref iid, out var obj) != 0) return new();
        var folder = (IShellItem)obj;
        try
        {
            var bhid = BHID_EnumItems; var iidEnum = typeof(IEnumShellItems).GUID;
            folder.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out var e);
            var en = (IEnumShellItems)e;
            try
            {
                var one = new IShellItem[1];
                while (en.Next(1, one, out uint got) == 0 && got == 1)
                {
                    var it = one[0];
                    try
                    {
                        it.GetDisplayName(SIGDN_NORMALDISPLAY, out var pName);
                        it.GetDisplayName(SIGDN_PARENTRELATIVEPARSING, out var pParse);
                        var name = Take(pName); var parse = Take(pParse);
                        if (name.Length == 0 || parse.Length == 0) continue;
                        var low = name.ToLowerInvariant();
                        if (SkipWords.Any(low.Contains)) continue;
                        found.TryAdd(low, (name, parse)); // the same name twice (shortcut + Store entry): keep one
                    }
                    finally { Marshal.ReleaseComObject(it); }
                }
            }
            finally { Marshal.ReleaseComObject(en); }
        }
        finally { Marshal.ReleaseComObject(folder); }

        var ua = UserAssistCounts();
        return found.Values.Select(a =>
        {
            var k = a.Parsing.ToLowerInvariant();
            int use = ua.GetValueOrDefault(k);
            if (use == 0 && k.LastIndexOfAny(new[] { '\\', '/' }) is int s and >= 0) use = ua.GetValueOrDefault(k[(s + 1)..]);
            if (use == 0) use = ua.GetValueOrDefault(a.Name.ToLowerInvariant() + ".lnk");
            return new AppEntry(a.Name, a.Parsing, use);
        }).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string Take(IntPtr p)
    {
        if (p == IntPtr.Zero) return "";
        try { return Marshal.PtrToStringUni(p) ?? ""; } finally { Marshal.FreeCoTaskMem(p); }
    }

    /// <summary>How often Windows saw each program / shortcut / Store app opened (UserAssist, names in ROT13). Local, read-only.</summary>
    static Dictionary<string, int> UserAssistCounts()
    {
        var o = new Dictionary<string, int>();
        foreach (var g in new[] { "{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}", "{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}" })
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{g}\Count");
                if (k == null) continue;
                foreach (var raw in k.GetValueNames())
                {
                    if (k.GetValue(raw) is not byte[] data || data.Length < 8) continue;
                    int count = BitConverter.ToInt32(data, 4);
                    if (count <= 0 || count > 100000) continue;
                    var name = Rot13(raw).ToLowerInvariant();
                    o[name] = o.GetValueOrDefault(name) + count;
                    int s = name.LastIndexOfAny(new[] { '\\', '/' });
                    if (s >= 0) o[name[(s + 1)..]] = o.GetValueOrDefault(name[(s + 1)..]) + count;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { }
        }
        return o;
    }

    static string Rot13(string s)
    {
        var a = s.ToCharArray();
        for (int i = 0; i < a.Length; i++)
        {
            char c = a[i];
            if (c is >= 'a' and <= 'z') a[i] = (char)('a' + (c - 'a' + 13) % 26);
            else if (c is >= 'A' and <= 'Z') a[i] = (char)('A' + (c - 'A' + 13) % 26);
        }
        return new string(a);
    }

    // ---------------------------------------------------------------- learning (what you open, with what you typed)

    sealed class Learned { public int Opens { get; set; } public long Last { get; set; } public List<string> Queries { get; set; } = new(); }
    static Dictionary<string, Learned>? _learned;
    static string LearnPath => Paths.File("search-usage.json");
    static Dictionary<string, Learned> LearnedMap => _learned ??= Json.Load<Dictionary<string, Learned>>(LearnPath);

    /// <summary>Remember that <paramref name="key"/> was opened after typing <paramref name="q"/> (folded); at most 8 texts each.</summary>
    public static void Learn(string key, string q)
    {
        var m = LearnedMap;
        if (!m.TryGetValue(key, out var l)) m[key] = l = new Learned();
        l.Opens++;
        l.Last = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (q.Length > 0)
        {
            l.Queries.Remove(q);
            l.Queries.Insert(0, q);
            if (l.Queries.Count > 8) l.Queries.RemoveRange(8, l.Queries.Count - 8);
            foreach (var (k, other) in m) if (k != key) other.Queries.Remove(q); // the last pick owns a text
        }
        try { Json.SaveAtomic(LearnPath, m); } catch (Exception ex) { Log.Warn("Launcher usage: " + ex.Message); }
    }

    /// <summary>Use (up to +30), a text already used for this item (+60), opened in the last 3 days (+8).</summary>
    static int UsageBonus(string key, int use, string q)
    {
        int bonus = Math.Min(30, (int)Math.Round(7 * Math.Log2(1 + use)));
        if (LearnedMap.TryGetValue(key, out var l))
        {
            bonus += Math.Min(30, l.Opens * 3);
            if (q.Length > 0 && l.Queries.Any(lq => lq.StartsWith(q, StringComparison.Ordinal) || q.StartsWith(lq, StringComparison.Ordinal))) bonus += 60;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - l.Last < 3 * 86400) bonus += 8;
        }
        return bonus;
    }

    /// <summary>On the dock: pinned +20, open now +12 (the strongest "I use this" signal).</summary>
    static int DockBonus(AppEntry a)
    {
        var p = a.Parsing;
        bool Same(string? x) => x != null && (x.Equals(p, StringComparison.OrdinalIgnoreCase) || x.Equals("shell:AppsFolder\\" + p, StringComparison.OrdinalIgnoreCase));
        int b = 0;
        if (Settings.Current.PinnedApps.Any(pin => Same(pin.Aumid) || Same(pin.TargetPath) || Same(pin.LaunchPath))) b += 20;
        if (Svc.Windows.Windows.Any(w => Same(w.Aumid) || Same(w.ExePath))) b += 12;
        return b;
    }

    static SearchResult AppResult(AppEntry a, int score) => new()
    {
        Kind = ResultKind.App, Name = a.Name, Detail = "Application", IconSource = "shell:AppsFolder\\" + a.Parsing,
        Key = "app:" + a.Parsing, Score = score, Open = () => WindowTracker.Launch("shell:AppsFolder\\" + a.Parsing),
    };

    // ---------------------------------------------------------------- settings pages and commands

    static readonly (string Name, string Words, string Page)[] SettingsPages =
    {
        ("Wi-Fi", "wifi wireless network internet", "network-wifi"), ("Bluetooth & devices", "bluetooth headphones mouse keyboard pair", "bluetooth"),
        ("Network & internet", "ethernet ip proxy internet", "network"), ("VPN", "vpn", "network-vpn"),
        ("Mobile hotspot", "hotspot tethering share", "network-mobilehotspot"), ("Airplane mode", "flight", "network-airplanemode"),
        ("Display", "monitor brightness resolution scale night light hdr", "display"), ("Sound", "volume speakers microphone audio output input", "sound"),
        ("Notifications", "do not disturb focus alerts", "notifications"), ("Power & battery", "battery sleep energy saver power", "powersleep"),
        ("Storage", "disk space cleanup", "storagesense"), ("Multitasking", "snap windows desktops alt tab", "multitasking"),
        ("Clipboard", "clipboard history paste", "clipboard"), ("Personalization", "theme colors look", "personalization"),
        ("Background", "wallpaper picture", "personalization-background"), ("Colors", "dark mode light accent", "colors"),
        ("Taskbar", "taskbar", "taskbar"), ("Installed apps", "uninstall programs remove apps", "appsfeatures"),
        ("Default apps", "browser default open with", "defaultapps"), ("Startup apps", "startup login boot", "startupapps"),
        ("Your info", "account user profile", "yourinfo"), ("Sign-in options", "password pin hello fingerprint face", "signinoptions"),
        ("Date & time", "clock time zone", "dateandtime"), ("Language & region", "language region locale", "regionlanguage"),
        ("Typing", "keyboard typing autocorrect", "typing"), ("Gaming", "game mode xbox game bar", "gaming-gamemode"),
        ("Accessibility", "narrator magnifier contrast", "easeofaccess"), ("Privacy & security", "privacy permissions camera microphone location", "privacy"),
        ("Windows Update", "update upgrade patch", "windowsupdate"), ("Mouse", "pointer cursor scroll", "mousetouchpad"),
        ("Touchpad", "trackpad gestures", "devices-touchpad"), ("Printers & scanners", "printer scanner print", "printers"),
        ("About this PC", "system info pc name specs version", "about"), ("Recovery", "reset recovery restore", "recovery"),
        ("Remote Desktop", "rdp remote", "remotedesktop"), ("Activation", "license product key", "activation"),
    };

    /// <summary>Vela's own pages and actions (set by the app: they open windows that live in Views).</summary>
    public static Action? OpenVelaSettings, OpenWidgetPicker;

    static IEnumerable<SearchResult> Commands()
    {
        if (OpenVelaSettings != null)
            yield return new() { Kind = ResultKind.Command, Name = "Vela Settings", Detail = "Vela", Glyph = UI.Glyphs.Settings, Key = "cmd:vela-settings", Open = OpenVelaSettings };
        if (OpenWidgetPicker != null)
            yield return new() { Kind = ResultKind.Command, Name = "Add widgets", Detail = "Vela", Glyph = UI.Glyphs.Grid, Key = "cmd:vela-widgets", Open = OpenWidgetPicker };
        foreach (var (name, hint, run) in Widgets.CommandsWidget.Quick)
            yield return new() { Kind = ResultKind.Command, Name = name.TrimEnd('…'), Detail = hint, Glyph = UI.Glyphs.Lightning, Key = "cmd:" + name, Open = run };
    }

    // ---------------------------------------------------------------- the search

    /// <summary>
    /// Everything except files (those come from <see cref="SearchFilesAsync"/>), best first. Empty query: the apps you use
    /// most. <paramref name="query"/> is what was typed.
    /// </summary>
    public static List<SearchResult> Search(string query)
    {
        var q = Fold(query.Trim());
        var list = new List<SearchResult>();
        if (q.Length == 0)
        {
            foreach (var a in _apps.OrderByDescending(a => UsageBonus("app:" + a.Parsing, a.Use, "") + DockBonus(a)).Take(8))
                list.Add(AppResult(a, 0));
            return list;
        }

        if (Calculate(query, out var value))
            list.Add(new SearchResult
            {
                Kind = ResultKind.Calc, Name = value, Detail = "Calculator · Enter copies the result", Glyph = UI.Glyphs.Calculator, Score = 200,
                Open = () => { try { System.Windows.Clipboard.SetText(value); } catch (Exception ex) { Log.Warn("Clipboard: " + ex.Message); } },
            });

        foreach (var a in _apps)
        {
            int s = Score(a.Name, q);
            if (s == 0) continue;
            list.Add(AppResult(a, s + UsageBonus("app:" + a.Parsing, a.Use, q) + DockBonus(a)));
        }
        foreach (var (name, words, page) in SettingsPages)
        {
            int s = ScoreWithWords(name, words, q);
            if (s == 0) continue;
            var key = "set:" + page;
            list.Add(new SearchResult
            {
                Kind = ResultKind.Setting, Name = name, Detail = "Windows Settings", Glyph = UI.Glyphs.Settings, Key = key,
                Score = s - 5 + UsageBonus(key, 0, q), Open = () => WinActions.OpenSettings(page),
            });
        }
        foreach (var c in Commands())
        {
            if (list.Any(r => r.Kind == ResultKind.App && r.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))) continue; // "Settings": the app is enough
            int s = ScoreWithWords(c.Name, Fold(c.Detail), q);
            if (s == 0) continue;
            c.Score = s - 5 + UsageBonus(c.Key!, 0, q);
            list.Add(c);
        }
        // A path, URL or command line runs directly (like the Run box).
        var raw = query.Trim();
        if (raw.Contains("://") || raw.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) || (raw.Length > 2 && raw[1] == ':' && raw[2] == '\\'))
            list.Add(new SearchResult { Kind = ResultKind.Command, Name = "Open " + raw, Detail = "Run", Glyph = UI.Glyphs.Lightning, Score = 110, Open = () => Widgets.CommandsWidget.Run(raw) });

        list.Sort((x, y) => y.Score != x.Score ? y.Score.CompareTo(x.Score) : x.Name.Length.CompareTo(y.Name.Length));
        list.Add(new SearchResult
        {
            Kind = ResultKind.Web, Name = $"Search the web for “{raw}”", Detail = "Opens your browser", Glyph = UI.Glyphs.Globe, Score = 1,
            Open = () => WindowTracker.OpenUri("https://www.google.com/search?q=" + Uri.EscapeDataString(raw)),
        });
        return list;
    }

    // ---------------------------------------------------------------- calculator: + - * x / ^ %, parentheses, '.' decimals

    public static bool Calculate(string text, out string result)
    {
        result = "";
        var s = text.Trim().TrimEnd('=').Replace('×', '*').Replace('÷', '/').Replace(" ", "");
        if (s.Length == 0 || !s.Any(char.IsDigit) || !s.Any(c => "+-*/x^%()".Contains(c))) return false;
        if (s.Any(c => !char.IsDigit(c) && !"+-*/x^%().".Contains(c))) return false;
        if (s.All(c => char.IsDigit(c) || c == '.' || c == '-') && s.IndexOf('-', 1) < 0) return false; // "-5" alone isn't a sum
        var p = new Calc(s);
        double v = p.Expr();
        if (!p.Ok || p.Pos != s.Length || double.IsNaN(v) || double.IsInfinity(v)) return false;
        result = Math.Round(v, 10).ToString("G12", CultureInfo.InvariantCulture);
        return true;
    }

    sealed class Calc(string s)
    {
        public int Pos;
        public bool Ok = true;
        char Peek => Pos < s.Length ? s[Pos] : '\0';
        double Num()
        {
            if (Peek == '(') { Pos++; double v = Expr(); if (Peek == ')') Pos++; else Ok = false; return Percent(v); }
            if (Peek == '-') { Pos++; return -Num(); }
            if (Peek == '+') { Pos++; return Num(); }
            int start = Pos;
            while (char.IsDigit(Peek) || Peek == '.') Pos++;
            if (start == Pos || !double.TryParse(s.AsSpan(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) { Ok = false; return 0; }
            return Percent(n);
        }
        double Percent(double v) { if (Peek == '%') { Pos++; return v / 100; } return v; }
        double Pow() { double b = Num(); if (Peek == '^') { Pos++; return Math.Pow(b, Pow()); } return b; }
        double Term()
        {
            double v = Pow();
            while (true)
            {
                if (Peek is '*' or 'x') { Pos++; v *= Pow(); }
                else if (Peek == '/') { Pos++; double d = Pow(); if (d == 0) Ok = false; else v /= d; }
                else return v;
            }
        }
        public double Expr()
        {
            double v = Term();
            while (true)
            {
                if (Peek == '+') { Pos++; v += Term(); }
                else if (Peek == '-') { Pos++; v -= Term(); }
                else return v;
            }
        }
    }

    // ---------------------------------------------------------------- files (Windows Search index, by name)

    /// <summary>
    /// Files and folders whose name contains the text, newest first, from Windows' own search index (OLE DB through ADO,
    /// late bound: no package). Runs on its own STA thread; cancelled results are simply dropped by the caller.
    /// </summary>
    public static Task<List<SearchResult>> SearchFilesAsync(string query, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<List<SearchResult>>();
        var t = new Thread(() =>
        {
            try { tcs.SetResult(SearchFiles(query, ct)); }
            catch (Exception ex) { if (!ct.IsCancellationRequested) Log.Warn("Launcher files: " + ex.Message); tcs.SetResult(new()); }
        }) { IsBackground = true, Name = "Vela launcher files" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    static List<SearchResult> SearchFiles(string query, CancellationToken ct)
    {
        var o = new List<SearchResult>();
        var q = query.Trim();
        if (q.Length < 2) return o;
        var like = new StringBuilder();
        foreach (var c in q) like.Append(c switch { '\'' => "''", '%' or '_' or '[' => $"[{c}]", _ => c.ToString() });
        var type = Type.GetTypeFromProgID("ADODB.Connection");
        if (type == null) return o;
        dynamic con = Activator.CreateInstance(type)!;
        try
        {
            con.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
            string sql = "SELECT TOP 40 System.ItemPathDisplay FROM SystemIndex WHERE SCOPE='file:' AND System.FileName LIKE '%" + like +
                         "%' AND System.FileExtension <> '.lnk' ORDER BY System.DateModified DESC";
            dynamic rs = con.Execute(sql);
            var fq = Fold(q);
            while (!(bool)rs.EOF && !ct.IsCancellationRequested)
            {
                if (rs.Fields.Item(0).Value is string path && path.Length > 3)
                {
                    var low = path.ToLowerInvariant();
                    if (!low.Contains(@"\appdata\") && !low.Contains(@"\$recycle.bin"))
                    {
                        bool dir = Directory.Exists(path);
                        if (dir || File.Exists(path))
                        {
                            var name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
                            o.Add(new SearchResult
                            {
                                Kind = dir ? ResultKind.Folder : ResultKind.File, Name = name, Detail = ShortPath(System.IO.Path.GetDirectoryName(path) ?? ""),
                                IconSource = path, Path = path, Score = Math.Max(Score(name, fq), 40),
                                Open = () => WindowTracker.Launch(path),
                            });
                        }
                    }
                }
                rs.MoveNext();
            }
            rs.Close();
        }
        finally { try { con.Close(); } catch { } Marshal.FinalReleaseComObject(con); }
        o.Sort((a, b) => b.Score.CompareTo(a.Score)); // stable enough: newest first within a score
        return o;
    }

    /// <summary>"~\Documents\…\Project\docs": the home folder as ~, long paths keep their start and end.</summary>
    public static string ShortPath(string dir)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0 && dir.StartsWith(home, StringComparison.OrdinalIgnoreCase)) dir = "~" + dir[home.Length..];
        if (dir.Length <= 44) return dir;
        var parts = dir.Split('\\');
        if (parts.Length <= 3) return dir[..20] + "…" + dir[^22..];
        var tail = parts[^1];
        for (int i = parts.Length - 2; i > 1 && (tail.Length + parts[i].Length) < 30; i--) tail = parts[i] + "\\" + tail;
        return parts[0] + "\\" + parts[1] + "\\…\\" + tail;
    }

    // ---------------------------------------------------------------- shell enumeration interop

    const uint SIGDN_NORMALDISPLAY = 0, SIGDN_PARENTRELATIVEPARSING = 0x80018001;
    static Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdn, out IntPtr name);
        void GetAttributes(uint mask, out uint attribs);
        void Compare(IShellItem psi, uint hint, out int order);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumShellItems
    {
        [PreserveSig] int Next(uint celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IShellItem[] items, out uint fetched);
        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems e);
    }
}
