using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Vela.Core;

namespace Vela.Connectors;

/// <summary>
/// ChatGPT / Codex plan limits, read offline from the Codex CLI's own session logs
/// (~/.codex/sessions/YYYY/MM/DD/*.jsonl contain "rate_limits" in token_count events).
/// Nothing is sent anywhere. Numbers are as of your last Codex turn.
/// </summary>
public sealed class CodexConnector : PollingConnector
{
    public override string Id => "codex";
    public override string Name => "ChatGPT (Codex)";
    public override string Description => "Codex 5-hour and weekly limits, read from the Codex CLI's local session logs.";
    public override ConnectorAuth Auth => ConnectorAuth.LocalSignIn;
    public override string? HelpUrl => "https://github.com/openai/codex";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(2);

    static string Home => Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } d ? d : Path.Combine(Paths.UserHome, ".codex");

    public override bool IsConnected => File.Exists(Path.Combine(Home, "auth.json")) || Directory.Exists(Path.Combine(Home, "sessions"));
    public List<UsageWindow> Windows { get; private set; } = new();
    public DateTime? AsOf { get; private set; }
    public string? Plan { get; private set; }

    protected override async Task FetchAsync()
    {
        var result = await Task.Run(ReadLatest);
        if (result == null)
        {
            Error = IsConnected ? "No Codex activity yet" : "Sign in with `codex login`";
            return;
        }
        (Windows, AsOf, Plan) = result.Value;
        Error = null;
        LastUpdated = DateTime.Now;
    }

    static (List<UsageWindow>, DateTime?, string?)? ReadLatest()
    {
        var sessions = Path.Combine(Home, "sessions");
        if (!Directory.Exists(sessions)) return null;

        var files = new List<FileInfo>();
        for (int i = 0; i < 14 && files.Count < 6; i++)
        {
            var d = DateTime.Today.AddDays(-i);
            var dir = Path.Combine(sessions, d.ToString("yyyy"), d.ToString("MM"), d.ToString("dd"));
            if (Directory.Exists(dir)) files.AddRange(new DirectoryInfo(dir).GetFiles("*.jsonl"));
        }
        foreach (var f in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(6))
        {
            var line = LastLineContaining(f.FullName, "\"rate_limits\"");
            if (line == null) continue;
            try { return Parse(line); } catch { }
        }
        return null;
    }

    static (List<UsageWindow>, DateTime?, string?) Parse(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        DateTime? ts = root.TryGetProperty("timestamp", out var t) && t.TryGetDateTime(out var dt) ? dt.ToLocalTime() : null;
        var payload = root.TryGetProperty("payload", out var p) ? p : root;
        var rl = payload.GetProperty("rate_limits");
        string? plan = rl.TryGetProperty("plan_type", out var pt) && pt.ValueKind == JsonValueKind.String ? pt.GetString() : null;
        var list = new List<UsageWindow>();
        foreach (var key in new[] { "primary", "secondary" })
        {
            if (!rl.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object) continue;
            double pct = w.TryGetProperty("used_percent", out var up) && up.ValueKind == JsonValueKind.Number ? up.GetDouble() : 0;
            int minutes = w.TryGetProperty("window_minutes", out var wm) && wm.ValueKind == JsonValueKind.Number ? wm.GetInt32() : 0;
            DateTimeOffset? reset = null;
            if (w.TryGetProperty("resets_at", out var ra) && ra.ValueKind == JsonValueKind.Number)
                reset = DateTimeOffset.FromUnixTimeSeconds(ra.GetInt64());
            else if (w.TryGetProperty("resets_in_seconds", out var rs) && rs.ValueKind == JsonValueKind.Number && ts != null)
                reset = new DateTimeOffset(ts.Value).AddSeconds(rs.GetDouble());
            if (reset != null && reset < DateTimeOffset.Now) pct = 0; // window already rolled over
            string label = minutes switch { <= 360 and > 0 => "Session", >= 10000 => "Weekly", _ => key == "primary" ? "Session" : "Weekly" };
            list.Add(new UsageWindow(label, Math.Clamp(pct, 0, 100), reset));
        }
        return (list, ts, plan);
    }

    /// <summary>Reads the file tail backwards in chunks; session logs can be large.</summary>
    static string? LastLineContaining(string path, string needle)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long len = fs.Length;
        int chunk = (int)Math.Min(len, 1 << 20);
        fs.Seek(len - chunk, SeekOrigin.Begin);
        var buf = new byte[chunk];
        fs.ReadExactly(buf, 0, chunk);
        var text = System.Text.Encoding.UTF8.GetString(buf);
        var lines = text.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
            if (lines[i].Contains(needle, StringComparison.Ordinal) && lines[i].TrimStart().StartsWith('{')) return lines[i];
        return null;
    }
}

public sealed record GitHubNotification(string Repo, string Title, string Type, string Url, DateTime Updated);

/// <summary>GitHub unread notifications with a classic PAT (scopes: notifications, read:user).</summary>
public sealed class GitHubConnector : PollingConnector
{
    public override string Id => "github";
    public override string Name => "GitHub";
    public override string Description => "Unread notifications. Classic token with the notifications and read:user scopes.";
    public override ConnectorAuth Auth => ConnectorAuth.ApiKey;
    public override string? SecretName => "github.token";
    public override string KeyHint => "ghp_…";
    public override string? HelpUrl => "https://github.com/settings/tokens/new?scopes=notifications,read:user&description=Vela";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(2);

    public override bool IsConnected => Secrets.Has(SecretName!);
    public string? Login { get; private set; }
    public List<GitHubNotification> Items { get; private set; } = new();

    protected override async Task FetchAsync()
    {
        var token = Secrets.Get(SecretName!);
        if (token == null) { Error = null; Items = new(); return; }
        void Auth(HttpRequestMessage r)
        {
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            r.Headers.Accept.ParseAdd("application/vnd.github+json");
            r.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }
        if (Login == null)
        {
            using var me = await Http.GetJsonAsync("https://api.github.com/user", Auth);
            Login = me.RootElement.GetProperty("login").GetString();
        }
        using var doc = await Http.GetJsonAsync("https://api.github.com/notifications?per_page=50", Auth);
        Items = doc.RootElement.EnumerateArray().Select(n =>
        {
            var subject = n.GetProperty("subject");
            var repo = n.GetProperty("repository");
            var apiUrl = subject.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            var html = apiUrl?.Replace("https://api.github.com/repos/", "https://github.com/").Replace("/pulls/", "/pull/")
                       ?? repo.GetProperty("html_url").GetString() ?? "https://github.com/notifications";
            return new GitHubNotification(
                repo.GetProperty("full_name").GetString() ?? "",
                subject.GetProperty("title").GetString() ?? "",
                subject.GetProperty("type").GetString() ?? "",
                html,
                n.GetProperty("updated_at").GetDateTime().ToLocalTime());
        }).ToList();
        Error = null;
        LastUpdated = DateTime.Now;
    }
}

/// <summary>Stripe net revenue (last 30 days) with a restricted read-only key (Balance + Balance transactions: Read).</summary>
public sealed class StripeConnector : PollingConnector
{
    public override string Id => "stripe";
    public override string Name => "Stripe";
    public override string Description => "Net revenue after fees and refunds, last 30 days. Restricted key: Balance + Balance transaction sources = Read.";
    public override ConnectorAuth Auth => ConnectorAuth.ApiKey;
    public override string? SecretName => "stripe.key";
    public override string KeyHint => "rk_live_…";
    public override string? HelpUrl => "https://dashboard.stripe.com/apikeys/create";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);

    public override bool IsConnected => Secrets.Has(SecretName!);
    public decimal Net30 { get; private set; }
    public string Currency { get; private set; } = "usd";
    public bool TestMode { get; private set; }
    public List<double> Daily { get; private set; } = new(); // last 14 days

    static readonly HashSet<string> RevenueTypes = new() { "charge", "payment", "refund", "payment_refund", "payment_failure_refund" };

    protected override async Task FetchAsync()
    {
        var key = Secrets.Get(SecretName!);
        if (key == null) { Error = null; return; }
        TestMode = key.Contains("_test_");
        var since = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds();
        decimal net = 0;
        var daily = new double[14];
        string? currency = null;
        string? after = null;
        for (int page = 0; page < 30; page++)
        {
            var url = $"https://api.stripe.com/v1/balance_transactions?limit=100&created[gte]={since}" + (after != null ? $"&starting_after={after}" : "");
            using var doc = await Http.GetJsonAsync(url, r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key));
            var data = doc.RootElement.GetProperty("data");
            foreach (var tx in data.EnumerateArray())
            {
                after = tx.GetProperty("id").GetString();
                if (!RevenueTypes.Contains(tx.GetProperty("type").GetString() ?? "")) continue;
                currency ??= tx.GetProperty("currency").GetString();
                decimal amount = tx.GetProperty("net").GetInt64() / 100m;
                net += amount;
                var created = DateTimeOffset.FromUnixTimeSeconds(tx.GetProperty("created").GetInt64()).LocalDateTime.Date;
                int idx = 13 - (int)(DateTime.Today - created).TotalDays;
                if (idx is >= 0 and < 14) daily[idx] += (double)amount;
            }
            if (!doc.RootElement.GetProperty("has_more").GetBoolean()) break;
        }
        Net30 = net;
        Currency = currency ?? "usd";
        Daily = daily.ToList();
        Error = null;
        LastUpdated = DateTime.Now;
    }

    public string Display => Fmt.Money(Net30, Currency);
}
