using System.Net.Http.Headers;
using System.Text.Json;
using Vela.Core;

namespace Vela.Connectors;

/// <summary>
/// Claude plan limits + local token counts.
///
/// Limits: reads the OAuth sign-in Claude Code saved (~/.claude/.credentials.json, or $CLAUDE_CONFIG_DIR)
/// and calls the same usage endpoint Claude Code's /usage uses. Read-only: we never refresh or
/// rewrite the token. When it expires the user runs `claude` once and it's valid again.
/// NOTE: this endpoint is not a documented public API; parsing is deliberately defensive.
///
/// Tokens: summed from Claude Code's local session logs (~/.claude/projects/**/*.jsonl),
/// incrementally (only new bytes are read on each pass). Works offline, no sign-in needed.
/// </summary>
public sealed class ClaudeConnector : PollingConnector
{
    public override string Id => "claude";
    public override string Name => "Claude";
    public override string Description => "Plan limits from your Claude Code sign-in, tokens from its local logs.";
    public override ConnectorAuth Auth => ConnectorAuth.LocalSignIn;
    public override string? HelpUrl => "https://docs.claude.com/en/docs/claude-code/quickstart";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    static string ConfigDir => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } d
        ? d : Path.Combine(Paths.UserHome, ".claude");
    static string CredentialsPath => Path.Combine(ConfigDir, ".credentials.json");

    public override bool IsConnected => File.Exists(CredentialsPath);
    public string? Plan { get; private set; }
    public List<UsageWindow> Windows { get; private set; } = new();
    public UsageWindow? Session => Windows.FirstOrDefault(w => w.Label == "Session");
    public UsageWindow? Weekly => Windows.FirstOrDefault(w => w.Label == "Weekly");

    public long TokensToday { get; private set; }
    public long TokensWeek { get; private set; }
    public string? TopModel { get; private set; }
    public long TopModelTokens { get; private set; }

    readonly LocalLogScanner _logs = new();

    protected override async Task FetchAsync()
    {
        var scan = Task.Run(_logs.Scan);
        string? err = null;

        if (IsConnected)
        {
            try { await FetchLimitsAsync(); }
            catch (Exception ex) { err = ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } ? "Sign-in expired — run claude" : "Limits unavailable"; Log.Warn("Claude limits: " + ex.Message); }
        }
        else err = "Sign in to Claude Code";

        var t = await scan;
        TokensToday = t.Today; TokensWeek = t.Week; TopModel = t.TopModel; TopModelTokens = t.TopModelTokens;
        Error = err;
        LastUpdated = DateTime.Now;
    }

    async Task FetchLimitsAsync()
    {
        string token;
        using (var cred = JsonDocument.Parse(await File.ReadAllTextAsync(CredentialsPath)))
        {
            var oauth = cred.RootElement.GetProperty("claudeAiOauth");
            token = oauth.GetProperty("accessToken").GetString() ?? throw new InvalidOperationException("no token");
            Plan = oauth.TryGetProperty("subscriptionType", out var st) ? Capitalize(st.GetString()) : null;
            if (oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number
                && DateTimeOffset.FromUnixTimeMilliseconds(exp.GetInt64()) < DateTimeOffset.UtcNow)
                throw new HttpRequestException("expired", null, System.Net.HttpStatusCode.Unauthorized);
        }

        using var doc = await Http.GetJsonAsync("https://api.anthropic.com/api/oauth/usage", req =>
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        });

        var list = new List<UsageWindow>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object || !prop.Value.TryGetProperty("utilization", out var u)
                || u.ValueKind != JsonValueKind.Number) continue;
            DateTimeOffset? reset = prop.Value.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(r.GetString(), out var rr) ? rr : null;
            list.Add(new UsageWindow(Label(prop.Name), Math.Clamp(u.GetDouble(), 0, 100), reset));
        }
        Windows = list.OrderBy(w => w.Label switch { "Session" => 0, "Weekly" => 1, _ => 2 }).ToList();
    }

    static string Label(string key) => key switch
    {
        "five_hour" => "Session",
        "seven_day" => "Weekly",
        "seven_day_opus" => "Opus weekly",
        "seven_day_sonnet" => "Sonnet weekly",
        _ => Capitalize(key.Replace('_', ' ')) ?? key,
    };

    static string? Capitalize(string? s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>"claude-opus-5-5-20261001" → "Opus 5.5".</summary>
    public static string PrettyModel(string? model)
    {
        if (string.IsNullOrEmpty(model)) return "—";
        var parts = model.Replace("claude-", "").Split('-');
        var family = parts.FirstOrDefault(p => p is "opus" or "sonnet" or "haiku" or "fable");
        if (family == null) return model;
        var nums = parts.Where(p => p.All(char.IsDigit) && p.Length <= 2).ToList();
        return Capitalize(family) + (nums.Count > 0 ? " " + string.Join(".", nums) : "");
    }

    // ---------------------------------------------------------------------------------------
    sealed class LocalLogScanner
    {
        sealed class FileState
        {
            public long Offset;
            public readonly HashSet<int> Seen = new();
            public readonly Dictionary<(DateTime day, string model), long> Totals = new();
        }

        readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);

        public (long Today, long Week, string? TopModel, long TopModelTokens) Scan()
        {
            var root = Path.Combine(ConfigDir, "projects");
            var cutoff = DateTime.Today.AddDays(-6);
            if (Directory.Exists(root))
            {
                foreach (var f in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (File.GetLastWriteTime(f) < cutoff) { _files.Remove(f); continue; }
                        if (!_files.TryGetValue(f, out var st)) _files[f] = st = new FileState();
                        ReadNew(f, st);
                    }
                    catch (Exception ex) { Log.Warn($"Claude log read {f}: {ex.Message}"); }
                }
            }

            long today = 0, week = 0;
            var byModel = new Dictionary<string, long>();
            foreach (var st in _files.Values)
                foreach (var ((day, model), n) in st.Totals)
                {
                    if (day < cutoff) continue;
                    week += n;
                    if (day == DateTime.Today) today += n;
                    byModel[model] = byModel.GetValueOrDefault(model) + n;
                }
            var top = byModel.OrderByDescending(kv => kv.Value).FirstOrDefault();
            return (today, week, top.Key == null ? null : PrettyModel(top.Key), top.Value);
        }

        static void ReadNew(string path, FileState st)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < st.Offset) { st.Offset = 0; st.Seen.Clear(); st.Totals.Clear(); }
            if (fs.Length == st.Offset) return;
            fs.Position = st.Offset;
            using var reader = new StreamReader(fs);
            long consumed = st.Offset;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                // Only count complete lines; a partial last line is re-read next pass.
                if (reader.EndOfStream && !line.EndsWith('}')) break;
                consumed += System.Text.Encoding.UTF8.GetByteCount(line) + 1;
                if (!line.Contains("\"usage\"", StringComparison.Ordinal)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var r = doc.RootElement;
                    if (!r.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) continue;
                    if (!msg.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) continue;
                    var id = (msg.TryGetProperty("id", out var mid) ? mid.GetString() : null) + "|" +
                             (r.TryGetProperty("requestId", out var rid) ? rid.GetString() : null);
                    if (!st.Seen.Add(id.GetHashCode())) continue; // streaming writes duplicates
                    long n = I(u, "input_tokens") + I(u, "output_tokens") + I(u, "cache_creation_input_tokens") + I(u, "cache_read_input_tokens");
                    var model = msg.TryGetProperty("model", out var m) ? m.GetString() ?? "unknown" : "unknown";
                    if (model == "<synthetic>") continue;
                    var ts = r.TryGetProperty("timestamp", out var t) && t.TryGetDateTime(out var dt) ? dt.ToLocalTime().Date : DateTime.Today;
                    st.Totals[(ts, model)] = st.Totals.GetValueOrDefault((ts, model)) + n;
                }
                catch { /* malformed line */ }
            }
            st.Offset = consumed;
        }

        static long I(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
    }
}
