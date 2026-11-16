using System.Globalization;
using System.Text.Json;
using Vela.Core;

namespace Vela.Connectors;

/// <summary>
/// GLM Coding Plan (Z.ai / Zhipu BigModel): how much of each plan window is used, from the plan's quota endpoint.
/// China-region keys (open.bigmodel.cn): put "cn" and a space before the key.
/// </summary>
public sealed class GlmConnector : MetricConnector
{
    public override string Id => "glm";
    public override string Name => "GLM Coding Plan";
    public override string Description => "Usage of your GLM Coding Plan windows (Z.ai). API key from the Z.ai console; for open.bigmodel.cn keys put \"cn\" and a space first.";
    public override string KeyHint => "API key";
    public override string? HelpUrl => "https://z.ai/manage-apikey/apikey-list";
    public override string BadgeColor => "#1F63EC";
    public override string BadgeText => "Z";
    public override string Unit => "used · 5-hour window";
    public override string SampleValue => "34%";
    public override string? OpenUrl => "https://z.ai/manage-apikey/subscription";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    protected override async Task FetchMetricAsync(string key)
    {
        var host = "https://api.z.ai";
        if (key.StartsWith("cn ", StringComparison.OrdinalIgnoreCase)) { host = "https://open.bigmodel.cn"; key = key[3..].Trim(); }
        var url = host + "/api/monitor/usage/quota/limit";
        // The endpoint answers 200 with success=false when it doesn't like the header; try the raw key, then Bearer.
        var doc = await Http.GetJsonAsync(url, r => r.Headers.TryAddWithoutValidation("Authorization", key));
        if (!Ok(doc)) { doc.Dispose(); doc = await Http.GetJsonAsync(url, Bearer(key)); }
        using (doc)
        {
            if (!Ok(doc)) throw new InvalidOperationException(Str(doc.RootElement, "msg") ?? "Key rejected");
            var limits = doc.RootElement.GetProperty("data").GetProperty("limits").EnumerateArray().ToList();
            Rows = limits.Select(l =>
            {
                double pct = l.TryGetProperty("percentage", out _) ? Num(l, "percentage")
                    : Num(l, "usage") > 0 ? Num(l, "currentValue") / Num(l, "usage") * 100 : 0;
                var reset = l.TryGetProperty("nextResetTime", out var t) && t.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeMilliseconds(t.GetInt64()) : (DateTimeOffset?)null;
                return new MetricRow(Label(Str(l, "type")), $"{pct:0}%" + (reset is { } r ? " · " + Fmt.ResetsIn(r) : ""));
            }).ToList();
            var tokens = limits.FirstOrDefault(l => Str(l, "type") == "TOKENS_LIMIT");
            double main = tokens.ValueKind == JsonValueKind.Object ? Num(tokens, "percentage") : limits.Count > 0 ? limits.Max(l => Num(l, "percentage")) : 0;
            Value = $"{main:0}%";
            Sub = "used · 5-hour window";
        }
    }

    static bool Ok(JsonDocument d) => d.RootElement.TryGetProperty("success", out var s) ? s.ValueKind == JsonValueKind.True : d.RootElement.TryGetProperty("data", out _);

    static string Label(string? type) => type switch
    {
        "TOKENS_LIMIT" => "Prompts (5-hour window)",
        "TIME_LIMIT" => "Tools: web search & reader (month)",
        null => "Limit",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(type.Replace('_', ' ').ToLowerInvariant()),
    };
}

/// <summary>
/// OpenCode tokens and cost, summed from its local message history (no key; never writes OpenCode's files).
/// OpenCode keeps one JSON file per message under &lt;data&gt;/opencode/storage/message/&lt;session&gt;/.
/// </summary>
public sealed class OpenCodeConnector : MetricConnector
{
    public override string Id => "opencode";
    public override string Name => "OpenCode";
    public override string Description => "Tokens and cost from OpenCode's local history on this PC. Nothing to paste: use OpenCode and this fills in.";
    public override ConnectorAuth Auth => ConnectorAuth.LocalSignIn;
    public override string? SecretName => null;
    public override string? HelpUrl => "https://opencode.ai/docs";
    public override string BadgeColor => "#211E1E";
    public override string BadgeText => "oc";
    public override string Unit => "tokens today";
    public override string SampleValue => "1.2M";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);
    public override bool IsConnected => MessageDir != null;

    static string? MessageDir
    {
        get
        {
            var home = Paths.UserHome;
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? Path.Combine(x, "opencode") : null,
                Path.Combine(home, ".local", "share", "opencode"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode"),
            };
            foreach (var c in candidates)
                if (c != null && Directory.Exists(Path.Combine(c, "storage", "message"))) return Path.Combine(c, "storage", "message");
            return null;
        }
    }

    public sealed record Summary(long TokensToday, long Tokens7, decimal Cost7, string? TopModel, double[] Daily);

    protected override async Task FetchMetricAsync(string key)
    {
        var dir = MessageDir;
        if (dir == null) return;
        var since = DateTime.Now.Date.AddDays(-13);
        var s = await Task.Run(() =>
        {
            // Only files touched in the last 14 days: old sessions are never opened.
            var texts = new DirectoryInfo(dir).EnumerateFiles("*.json", SearchOption.AllDirectories)
                .Where(f => f.LastWriteTime >= since)
                .Select(f => { try { return File.ReadAllText(f.FullName); } catch { return null; } })
                .Where(t => t != null).Cast<string>();
            return Summarize(texts, DateTime.Now);
        });
        Value = Fmt.Tokens(s.TokensToday);
        Sub = $"tokens today · {Fmt.Tokens(s.Tokens7)} this week";
        Series = s.Daily.ToList();
        Rows = new()
        {
            new("Tokens, 7 days", Fmt.Tokens(s.Tokens7)),
            new("Cost, 7 days", s.Cost7 > 0 ? Fmt.Money(s.Cost7, "usd") : "—"),
            new("Most used model", s.TopModel ?? "—"),
        };
    }

    /// <summary>Sum assistant messages: tokens (input + output + reasoning + cache) per day, cost and the top model.</summary>
    public static Summary Summarize(IEnumerable<string> messageJson, DateTime now)
    {
        long today = 0, week = 0;
        decimal cost = 0;
        var daily = new double[14];
        var models = new Dictionary<string, long>();
        foreach (var text in messageJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                var m = doc.RootElement;
                if (Str(m, "role") != "assistant" || !m.TryGetProperty("tokens", out var t)) continue;
                long n = (long)(Num(t, "input") + Num(t, "output") + Num(t, "reasoning"));
                if (t.TryGetProperty("cache", out var c)) n += (long)(Num(c, "read") + Num(c, "write"));
                var created = m.TryGetProperty("time", out var time) && time.TryGetProperty("created", out var cr) && cr.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeMilliseconds(cr.GetInt64()).LocalDateTime : DateTime.MinValue;
                int idx = 13 - (int)(now.Date - created.Date).TotalDays;
                if (idx is < 0 or > 13) continue;
                daily[idx] += n;
                if (created.Date == now.Date) today += n;
                if (idx >= 7) { week += n; cost += (decimal)Num(m, "cost"); }
                if (Str(m, "modelID") is { } model) models[model] = models.GetValueOrDefault(model) + n;
            }
            catch (JsonException) { }
        }
        return new Summary(today, week, cost, models.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault(), daily);
    }
}
