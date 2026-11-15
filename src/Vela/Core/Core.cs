using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using Vela.Interop;

namespace Vela.Core;

/// <summary>Everything Vela writes lives under %LOCALAPPDATA%\Vela.</summary>
public static class Paths
{
    public static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vela");

    public static string File(string name)
    {
        Directory.CreateDirectory(Root);
        return Path.Combine(Root, name);
    }

    public static string Dir(string name)
    {
        var d = Path.Combine(Root, name);
        Directory.CreateDirectory(d);
        return d;
    }

    public static string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

public static class Log
{
    static readonly object Gate = new();
    static string? _path;

    static string LogPath => _path ??= Path.Combine(Paths.Dir("logs"), "vela.log");

    public static void Info(string msg) => Write("INFO ", msg);
    public static void Warn(string msg) => Write("WARN ", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex == null ? msg : $"{msg}: {ex}");

    static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    var old = LogPath + ".1";
                    if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                    System.IO.File.Move(LogPath, old);
                }
                System.IO.File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {msg}{Environment.NewLine}");
            }
            Debug.WriteLine($"[Vela] {level} {msg}");
        }
        catch { /* logging must never throw */ }
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (!System.IO.File.Exists(path)) return new T();
            var text = System.IO.File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(text, Options) ?? new T();
        }
        catch (Exception ex)
        {
            Log.Error($"Corrupt JSON at {path}, keeping a backup", ex);
            try { System.IO.File.Copy(path, path + ".corrupt", true); } catch { }
            return new T();
        }
    }

    /// <summary>Write to a temp file then swap, so a crash never leaves a half-written file.</summary>
    public static void SaveAtomic<T>(string path, T value)
    {
        try
        {
            var tmp = path + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            System.IO.File.Move(tmp, path, true);
        }
        catch (Exception ex) { Log.Error($"Save failed {path}", ex); }
    }
}

/// <summary>Runs an action once, a short delay after the last Trigger(). UI thread.</summary>
public sealed class Debouncer
{
    readonly DispatcherTimer _timer;
    readonly Action _action;

    public Debouncer(TimeSpan delay, Action action)
    {
        _action = action;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = delay };
        _timer.Tick += (_, _) => { _timer.Stop(); Run(); };
    }

    public void Trigger() { _timer.Stop(); _timer.Start(); }

    public void Flush()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        Run();
    }

    void Run()
    {
        try { _action(); } catch (Exception ex) { Log.Error("Debounced action failed", ex); }
    }
}

/// <summary>
/// One shared 1 Hz clock for every widget, aligned to the wall-clock second.
/// Paused while the dock is hidden (fullscreen app, locked session) so idle cost is ~0.
/// </summary>
public sealed class Ticker
{
    readonly DispatcherTimer _timer;
    bool _paused;
    public event Action<DateTime>? Tick;

    public Ticker()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        _timer.Interval = NextDelay();
        _timer.Start();
    }

    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            if (value) _timer.Stop();
            else { _timer.Interval = NextDelay(); _timer.Start(); OnTick(null, EventArgs.Empty); }
        }
    }

    static TimeSpan NextDelay() => TimeSpan.FromMilliseconds(1000 - DateTime.Now.Millisecond + 15);

    void OnTick(object? sender, EventArgs e)
    {
        _timer.Interval = NextDelay();
        var handlers = Tick;
        if (handlers == null) return;
        var now = DateTime.Now;
        foreach (Action<DateTime> h in handlers.GetInvocationList())
        {
            try { h(now); }
            catch (Exception ex) { Log.Error("Tick handler failed", ex); }
        }
    }
}

/// <summary>One HttpClient for the whole app.</summary>
public static class Http
{
    public static readonly HttpClient Client = Create();

    static HttpClient Create()
    {
        var c = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        })
        { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Vela/0.1 (+desktop widget dock)");
        return c;
    }

    public static async Task<JsonDocument> GetJsonAsync(string url, Action<HttpRequestMessage>? configure = null, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        configure?.Invoke(req);
        using var res = await Client.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)res.StatusCode} {res.ReasonPhrase}: {Truncate(body, 200)}", null, res.StatusCode);
        return JsonDocument.Parse(body);
    }

    static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];
}

/// <summary>Per-user encrypted secrets (API keys, tokens) using Windows DPAPI. Never leaves this PC.</summary>
public static class Secrets
{
    static readonly string FilePath = Paths.File("secrets.json");
    static Dictionary<string, string>? _cache;
    static Dictionary<string, string> Store => _cache ??= Json.Load<Dictionary<string, string>>(FilePath);

    public static event Action<string>? Changed;

    public static string? Get(string name)
    {
        if (!Store.TryGetValue(name, out var b64)) return null;
        try { return Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(b64))); }
        catch (Exception ex) { Log.Error($"Secret {name} unreadable", ex); return null; }
    }

    public static bool Has(string name) => Store.ContainsKey(name);

    public static void Set(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) Store.Remove(name);
        else Store[name] = Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(value.Trim())));
        Json.SaveAtomic(FilePath, Store);
        Changed?.Invoke(name);
    }

    static byte[] Protect(byte[] data) => Crypt(data, true);
    static byte[] Unprotect(byte[] data) => Crypt(data, false);

    static byte[] Crypt(byte[] data, bool protect)
    {
        var inBlob = new Native.DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
        var outBlob = new Native.DATA_BLOB();
        try
        {
            Marshal.Copy(data, 0, inBlob.pbData, data.Length);
            bool ok = protect
                ? Native.CryptProtectData(ref inBlob, "Vela", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1 /*UI_FORBIDDEN*/, ref outBlob)
                : Native.CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref outBlob);
            if (!ok) throw new InvalidOperationException("DPAPI failed: " + Marshal.GetLastWin32Error());
            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(inBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero) Native.LocalFree(outBlob.pbData);
        }
    }
}

public static class Fmt
{
    public static string Duration(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return $"{(int)t.TotalSeconds}s";
        if (t.TotalHours < 1) return $"{(int)t.TotalMinutes}m";
        return $"{(int)t.TotalHours}h {t.Minutes}m";
    }

    public static string Countdown(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    public static string ResetsIn(DateTimeOffset? at)
    {
        if (at == null) return "";
        var d = at.Value - DateTimeOffset.Now;
        if (d <= TimeSpan.Zero) return "resets now";
        if (d.TotalHours < 24) return $"resets {(int)d.TotalHours}h {d.Minutes}m";
        return $"resets {at.Value.LocalDateTime:ddd d}";
    }

    public static string Bytes(double b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return i == 0 ? $"{b:0} {u[i]}" : $"{b:0.#} {u[i]}";
    }

    public static string Rate(double bytesPerSec) => Bytes(bytesPerSec) + "/s";

    public static string Tokens(long n) => n switch
    {
        >= 1_000_000_000 => $"{n / 1e9:0.#}B",
        >= 1_000_000 => $"{n / 1e6:0.#}M",
        >= 1_000 => $"{n / 1e3:0.#}K",
        _ => n.ToString(),
    };

    public static string Money(decimal amount, string currency)
    {
        var sym = currency.ToUpperInvariant() switch
        {
            "USD" => "$", "EUR" => "€", "GBP" => "£", "JPY" => "¥", "INR" => "₹", "CAD" => "CA$", "AUD" => "A$",
            _ => currency.ToUpperInvariant() + " ",
        };
        return amount >= 10000 ? $"{sym}{amount / 1000:0.#}k" : $"{sym}{amount:0.##}";
    }
}
