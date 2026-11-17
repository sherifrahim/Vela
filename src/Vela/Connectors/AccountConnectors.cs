using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Vela.Core;

namespace Vela.Connectors;

// Account widgets (reference: Finance, Analytics, Developer, Online, Stocks, AI balance).
// Same rules as every connector: straight from this PC to the service, read-only keys, keys in Secrets (DPAPI).
// Connectors that need two values (a site and a key, a store and a token) take them in one field, separated by a space.

public sealed record MetricRow(string Label, string Value, string? Url = null);

/// <summary>
/// A connector that boils down to one headline number with a subtitle, optional detail rows and an optional
/// 14-point series. <see cref="Widgets.MetricWidget"/> renders any of them (tile, chart tile, card, connect form).
/// </summary>
public abstract class MetricConnector : PollingConnector
{
    public override ConnectorAuth Auth => ConnectorAuth.ApiKey;
    public override string? SecretName => Id + ".key";
    public override bool IsConnected => Secrets.Has(SecretName!);
    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);

    /// <summary>Badge: brand colour and a short mark (letter or two). Generic marks, not the companies' logos.</summary>
    public abstract string BadgeColor { get; }
    public abstract string BadgeText { get; }
    /// <summary>What the headline number means ("visitors today").</summary>
    public abstract string Unit { get; }
    /// <summary>Shown greyed out, labelled "sample", until the account is connected.</summary>
    public abstract string SampleValue { get; }
    public virtual string? OpenUrl => null;

    public string Value { get; protected set; } = "—";
    public string Sub { get; protected set; } = "";
    public List<MetricRow> Rows { get; protected set; } = new();
    public List<double> Series { get; protected set; } = new();
    public virtual string SeriesLabel => "Last 14 days";

    /// <summary>Second step after the key is saved (Google sign-in in the browser). The card shows a button while true.</summary>
    public virtual bool NeedsSignIn => false;
    public virtual string SignInLabel => "Sign in";
    public virtual Task SignInAsync() => Task.CompletedTask;

    /// <summary>Forget the key (and anything derived from it, e.g. a refresh token).</summary>
    public virtual void Disconnect() { if (SecretName != null) Secrets.Set(SecretName, null); }

    protected override async Task FetchAsync()
    {
        // Local connectors (no key) pass an empty string; key connectors stop quietly until a key is saved.
        var key = SecretName == null ? "" : Secrets.Get(SecretName)?.Trim();
        if (key == null || (SecretName != null && key.Length == 0) || !IsConnected) { Error = null; return; }
        if (NeedsSignIn) { Error = null; return; }
        await FetchMetricAsync(key).ConfigureAwait(true);
        Error = null;
        LastUpdated = DateTime.Now;
    }

    protected abstract Task FetchMetricAsync(string key);

    /// <summary>Split "first second" input (site + key, store + token, token + symbols).</summary>
    protected static (string first, string rest) Two(string key, string what)
    {
        var parts = key.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new InvalidOperationException($"Enter {what}, separated by a space");
        return (parts[0], parts[1].Trim());
    }

    protected static Action<HttpRequestMessage> Bearer(string token, string? accept = null) => r =>
    {
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (accept != null) r.Headers.Accept.ParseAdd(accept);
    };

    protected static string Count(double n) => n >= 1_000_000 ? $"{n / 1e6:0.#}M" : n >= 10_000 ? $"{n / 1e3:0.#}k" : n.ToString("#,0", CultureInfo.CurrentCulture);

    protected static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    protected static double Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => 0,
        };
    }
}

/// <summary>A sale: amount in major units (dollars, not cents).</summary>
public readonly record struct Sale(DateTime When, decimal Amount, string Currency);

/// <summary>
/// Payment providers: each one only lists its sales since a date; the base turns them into "revenue · 30 days"
/// and a 14-day series, like the Stripe tile. Refunds come back as negative amounts where the API reports them.
/// </summary>
public abstract class RevenueConnector : MetricConnector
{
    public override string Unit => "revenue · 30 days";
    public override string SampleValue => "$1,284";
    public decimal Net30 { get; private set; }
    public string Currency { get; private set; } = "usd";

    protected abstract Task<List<Sale>> SalesSinceAsync(string key, DateTime sinceUtc);

    protected override async Task FetchMetricAsync(string key)
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var sales = await SalesSinceAsync(key, since).ConfigureAwait(true);
        // Several currencies: report the one with the most sales, and say so.
        var main = sales.GroupBy(s => s.Currency.ToLowerInvariant()).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "usd";
        var mine = sales.Where(s => s.Currency.Equals(main, StringComparison.OrdinalIgnoreCase) && s.When >= since.ToLocalTime()).ToList();
        Net30 = mine.Sum(s => s.Amount);
        Currency = main;
        var daily = new double[14];
        foreach (var s in mine)
        {
            int idx = 13 - (int)(DateTime.Today - s.When.Date).TotalDays;
            if (idx is >= 0 and < 14) daily[idx] += (double)s.Amount;
        }
        Series = daily.ToList();
        Value = Fmt.Money(Net30, Currency);
        int others = sales.Select(s => s.Currency.ToLowerInvariant()).Distinct().Count() - 1;
        Sub = $"{mine.Count} sales · 30 days" + (others > 0 ? $" · +{others} other currenc{(others == 1 ? "y" : "ies")}" : "");
        Rows = new()
        {
            new("Today", Fmt.Money(mine.Where(s => s.When.Date == DateTime.Today).Sum(s => s.Amount), Currency)),
            new("Last 7 days", Fmt.Money(mine.Where(s => s.When.Date > DateTime.Today.AddDays(-7)).Sum(s => s.Amount), Currency)),
            new("Sales (30 days)", mine.Count.ToString()),
        };
    }

    protected static DateTime When(string? iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime() : DateTime.MinValue;
}

// ------------------------------------------------------------------ Finance

public sealed class PolarConnector : RevenueConnector
{
    public override string Id => "polar";
    public override string Name => "Polar";
    public override string Description => "Orders from the last 30 days. Organization access token with orders:read.";
    public override string KeyHint => "polar_oat_…";
    public override string? HelpUrl => "https://polar.sh/dashboard";
    public override string BadgeColor => "#0062FF";
    public override string BadgeText => "P";
    public override string? OpenUrl => "https://polar.sh/dashboard";

    protected override async Task<List<Sale>> SalesSinceAsync(string key, DateTime since)
    {
        var list = new List<Sale>();
        for (int page = 1; page <= 20; page++)
        {
            using var doc = await Http.GetJsonAsync($"https://api.polar.sh/v1/orders/?limit=100&page={page}&sorting=-created_at", Bearer(key));
            var items = doc.RootElement.GetProperty("items");
            bool older = false;
            foreach (var o in items.EnumerateArray())
            {
                var when = When(Str(o, "created_at"));
                if (when < since.ToLocalTime()) { older = true; break; }
                double cents = o.TryGetProperty("net_amount", out _) ? Num(o, "net_amount") : o.TryGetProperty("total_amount", out _) ? Num(o, "total_amount") : Num(o, "amount");
                list.Add(new Sale(when, (decimal)cents / 100m, Str(o, "currency") ?? "usd"));
            }
            if (older || items.GetArrayLength() < 100) break;
        }
        return list;
    }
}

public sealed class LemonSqueezyConnector : RevenueConnector
{
    public override string Id => "lemonsqueezy";
    public override string Name => "Lemon Squeezy";
    public override string Description => "Paid orders from the last 30 days. API key from Settings → API.";
    public override string KeyHint => "eyJ0eXAi…";
    public override string? HelpUrl => "https://app.lemonsqueezy.com/settings/api";
    public override string BadgeColor => "#FFC233";
    public override string BadgeText => "L";
    public override string? OpenUrl => "https://app.lemonsqueezy.com/orders";

    protected override async Task<List<Sale>> SalesSinceAsync(string key, DateTime since)
    {
        var list = new List<Sale>();
        string? url = "https://api.lemonsqueezy.com/v1/orders?page[size]=100";
        for (int page = 0; page < 20 && url != null; page++)
        {
            using var doc = await Http.GetJsonAsync(url, Bearer(key, "application/vnd.api+json"));
            bool older = false;
            foreach (var o in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var a = o.GetProperty("attributes");
                var when = When(Str(a, "created_at"));
                if (when < since.ToLocalTime()) { older = true; continue; }
                var status = Str(a, "status");
                if (status is "pending" or "failed") continue;
                double cents = Num(a, "total") - Num(a, "refunded_amount");
                list.Add(new Sale(when, (decimal)cents / 100m, Str(a, "currency") ?? "usd"));
            }
            url = doc.RootElement.TryGetProperty("links", out var links) ? Str(links, "next") : null;
            if (older) break;
        }
        return list;
    }
}

public sealed class GumroadConnector : RevenueConnector
{
    public override string Id => "gumroad";
    public override string Name => "Gumroad";
    public override string Description => "Sales from the last 30 days. Access token from Settings → Advanced → Applications.";
    public override string KeyHint => "access token";
    public override string? HelpUrl => "https://app.gumroad.com/settings/advanced";
    public override string BadgeColor => "#FF90E8";
    public override string BadgeText => "G";
    public override string? OpenUrl => "https://app.gumroad.com/sales";

    protected override async Task<List<Sale>> SalesSinceAsync(string key, DateTime since)
    {
        var list = new List<Sale>();
        string? url = $"https://api.gumroad.com/v2/sales?after={since:yyyy-MM-dd}";
        for (int page = 0; page < 20 && url != null; page++)
        {
            using var doc = await Http.GetJsonAsync(url, Bearer(key));
            foreach (var s in doc.RootElement.GetProperty("sales").EnumerateArray())
            {
                if (s.TryGetProperty("refunded", out var r) && r.ValueKind == JsonValueKind.True) continue;
                list.Add(new Sale(When(Str(s, "created_at")), (decimal)Num(s, "price") / 100m, Str(s, "currency") ?? "usd"));
            }
            var next = Str(doc.RootElement, "next_page_url");
            url = string.IsNullOrEmpty(next) ? null : next.StartsWith("http") ? next : "https://api.gumroad.com" + next;
        }
        return list;
    }
}

public sealed class PaddleConnector : RevenueConnector
{
    public override string Id => "paddle";
    public override string Name => "Paddle";
    public override string Description => "Completed transactions from the last 30 days (Paddle Billing). API key with transaction read; sandbox keys work too.";
    public override string KeyHint => "pdl_live_apikey_…";
    public override string? HelpUrl => "https://vendors.paddle.com/authentication-v2";
    public override string BadgeColor => "#FDDD35";
    public override string BadgeText => "Pd";
    public override string? OpenUrl => "https://vendors.paddle.com/transactions-v2";

    protected override async Task<List<Sale>> SalesSinceAsync(string key, DateTime since)
    {
        var host = key.Contains("_sdbx_") ? "https://sandbox-api.paddle.com" : "https://api.paddle.com";
        var list = new List<Sale>();
        string? url = $"{host}/transactions?status=completed,billed&per_page=100&order_by=created_at[DESC]&created_at[GTE]={since:yyyy-MM-ddTHH:mm:ssZ}";
        for (int page = 0; page < 20 && url != null; page++)
        {
            using var doc = await Http.GetJsonAsync(url, Bearer(key));
            foreach (var t in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                double total = t.TryGetProperty("details", out var d) && d.TryGetProperty("totals", out var tot) ? Num(tot, "grand_total") : 0;
                list.Add(new Sale(When(Str(t, "billed_at") ?? Str(t, "created_at")), (decimal)total / 100m, Str(t, "currency_code") ?? "USD"));
            }
            var meta = doc.RootElement.GetProperty("meta").GetProperty("pagination");
            url = meta.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True ? Str(meta, "next") : null;
        }
        return list;
    }
}

public sealed class ShopifyConnector : RevenueConnector
{
    public override string Id => "shopify";
    public override string Name => "Shopify";
    public override string Description => "Paid orders from the last 30 days. Enter your store domain and an Admin API token (read_orders), separated by a space.";
    public override string KeyHint => "mystore.myshopify.com shpat_…";
    public override string? HelpUrl => "https://help.shopify.com/en/manual/apps/app-types/custom-apps";
    public override string BadgeColor => "#95BF47";
    public override string BadgeText => "S";

    protected override async Task<List<Sale>> SalesSinceAsync(string key, DateTime since)
    {
        var (store, token) = Two(key, "your store domain and the Admin API token");
        store = store.Replace("https://", "").TrimEnd('/');
        var url = $"https://{store}/admin/api/2024-10/orders.json?status=any&financial_status=paid&limit=250&fields=created_at,total_price,currency&created_at_min={since:yyyy-MM-ddTHH:mm:ssZ}";
        using var doc = await Http.GetJsonAsync(url, r => r.Headers.Add("X-Shopify-Access-Token", token));
        return doc.RootElement.GetProperty("orders").EnumerateArray()
            .Select(o => new Sale(When(Str(o, "created_at")), (decimal)Num(o, "total_price"), Str(o, "currency") ?? "usd")).ToList();
    }
}

public sealed class DodoConnector : RevenueConnector
{
    public override string Id => "dodo";
    public override string Name => "Dodo Payments";
    public override string Description => "Succeeded payments from the last 30 days. API key from Developer → API keys. Test keys: put \"test\" and a space before the key.";
    public override string KeyHint => "API key";
    public override string? HelpUrl => "https://app.dodopayments.com/developer/api-keys";
    public override string BadgeColor => "#1C1C1C";
    public override string BadgeText => "D";

    protected override async Task<List<Sale>> SalesSinceAsync(string key, DateTime since)
    {
        var host = "https://live.dodopayments.com";
        if (key.StartsWith("test ", StringComparison.OrdinalIgnoreCase)) { host = "https://test.dodopayments.com"; key = key[5..].Trim(); }
        var list = new List<Sale>();
        for (int page = 0; page < 20; page++)
        {
            using var doc = await Http.GetJsonAsync($"{host}/payments?page_size=100&page_number={page}&created_at_gte={since:yyyy-MM-ddTHH:mm:ssZ}", Bearer(key));
            var items = doc.RootElement.GetProperty("items");
            foreach (var p in items.EnumerateArray())
            {
                if (Str(p, "status") is { } st && st != "succeeded") continue;
                list.Add(new Sale(When(Str(p, "created_at")), (decimal)Num(p, "total_amount") / 100m, Str(p, "currency") ?? "USD"));
            }
            if (items.GetArrayLength() < 100) break;
        }
        return list;
    }
}

// ------------------------------------------------------------------ AI

public sealed class DeepSeekConnector : MetricConnector
{
    public override string Id => "deepseek";
    public override string Name => "DeepSeek";
    public override string Description => "Account balance. Any DeepSeek API key.";
    public override string KeyHint => "sk-…";
    public override string? HelpUrl => "https://platform.deepseek.com/api_keys";
    public override string BadgeColor => "#4D6BFE";
    public override string BadgeText => "DS";
    public override string Unit => "balance";
    public override string SampleValue => "$12.40";
    public override string? OpenUrl => "https://platform.deepseek.com/usage";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(10);

    protected override async Task FetchMetricAsync(string key)
    {
        using var doc = await Http.GetJsonAsync("https://api.deepseek.com/user/balance", Bearer(key, "application/json"));
        var infos = doc.RootElement.GetProperty("balance_infos").EnumerateArray().ToList();
        Rows = infos.Select(i => new MetricRow($"Balance ({Str(i, "currency")})",
            $"{Fmt.Money((decimal)Num(i, "total_balance"), Str(i, "currency") ?? "usd")} · {Fmt.Money((decimal)Num(i, "topped_up_balance"), Str(i, "currency") ?? "usd")} topped up")).ToList();
        var first = infos.FirstOrDefault();
        Value = infos.Count == 0 ? "—" : Fmt.Money((decimal)Num(first, "total_balance"), Str(first, "currency") ?? "usd");
        Sub = doc.RootElement.TryGetProperty("is_available", out var ok) && ok.ValueKind == JsonValueKind.False ? "balance · not enough for calls" : "balance";
    }
}

// ------------------------------------------------------------------ Analytics & developer

public sealed class PlausibleConnector : MetricConnector
{
    public override string Id => "plausible";
    public override string Name => "Plausible";
    public override string Description => "Visitors today and the last 14 days. Enter your site domain and a Stats API key, separated by a space.";
    public override string KeyHint => "example.com key";
    public override string? HelpUrl => "https://plausible.io/settings/api-keys";
    public override string BadgeColor => "#5850EC";
    public override string BadgeText => "P";
    public override string Unit => "visitors today";
    public override string SampleValue => "1,204";

    protected override async Task FetchMetricAsync(string key)
    {
        var (site, token) = Two(key, "your site domain and the API key");
        var b = Bearer(token);
        using var agg = await Http.GetJsonAsync($"https://plausible.io/api/v1/stats/aggregate?site_id={Uri.EscapeDataString(site)}&period=day&metrics=visitors,pageviews,bounce_rate,visit_duration", b);
        var r = agg.RootElement.GetProperty("results");
        double V(string m) => r.TryGetProperty(m, out var x) ? Num(x, "value") : 0;
        Value = Count(V("visitors"));
        Sub = $"visitors today · {site}";
        Rows = new() { new("Pageviews today", Count(V("pageviews"))), new("Bounce rate", $"{V("bounce_rate"):0}%"), new("Visit duration", $"{V("visit_duration") / 60:0.#} min") };
        using var ts = await Http.GetJsonAsync($"https://plausible.io/api/v1/stats/timeseries?site_id={Uri.EscapeDataString(site)}&period=custom&date={DateTime.Today.AddDays(-13):yyyy-MM-dd},{DateTime.Today:yyyy-MM-dd}&metrics=visitors", b);
        Series = ts.RootElement.GetProperty("results").EnumerateArray().Select(p => Num(p, "visitors")).ToList();
    }
    public override string? OpenUrl => "https://plausible.io/sites";
}

public sealed class DubConnector : MetricConnector
{
    public override string Id => "dub";
    public override string Name => "Dub";
    public override string Description => "Link clicks in the last 24 hours and 14 days. Workspace API key.";
    public override string KeyHint => "dub_…";
    public override string? HelpUrl => "https://app.dub.co/settings/tokens";
    public override string BadgeColor => "#111111";
    public override string BadgeText => "d";
    public override string Unit => "clicks · 24 h";
    public override string SampleValue => "342";
    public override string? OpenUrl => "https://app.dub.co/analytics";

    protected override async Task FetchMetricAsync(string key)
    {
        using var day = await Http.GetJsonAsync("https://api.dub.co/analytics?event=clicks&interval=24h", Bearer(key));
        Value = Count(Num(day.RootElement, "clicks"));
        Sub = "clicks · 24 h";
        using var ts = await Http.GetJsonAsync("https://api.dub.co/analytics?event=clicks&groupBy=timeseries&interval=30d", Bearer(key));
        var points = ts.RootElement.EnumerateArray().Select(p => Num(p, "clicks")).ToList();
        Series = points.Skip(Math.Max(0, points.Count - 14)).ToList();
        using var top = await Http.GetJsonAsync("https://api.dub.co/analytics?event=clicks&groupBy=top_links&interval=7d", Bearer(key));
        Rows = top.RootElement.EnumerateArray().Take(5)
            .Select(l => new MetricRow(Str(l, "shortLink") ?? Str(l, "link") ?? "link", Count(Num(l, "clicks")), Str(l, "shortLink"))).ToList();
    }
}

public sealed class LinearConnector : MetricConnector
{
    public override string Id => "linear";
    public override string Name => "Linear";
    public override string Description => "Open issues assigned to you. Personal API key (Settings → Security & access).";
    public override string KeyHint => "lin_api_…";
    public override string? HelpUrl => "https://linear.app/settings/account/security";
    public override string BadgeColor => "#5E6AD2";
    public override string BadgeText => "Li";
    public override string Unit => "open issues";
    public override string SampleValue => "7";
    public override string? OpenUrl => "https://linear.app/my-issues";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    protected override async Task FetchMetricAsync(string key)
    {
        const string q = "query { viewer { assignedIssues(first: 50, filter: { state: { type: { nin: [\"completed\", \"canceled\"] } } }) { nodes { identifier title url priorityLabel } } } }";
        using var doc = await Http.PostJsonAsync("https://api.linear.app/graphql", new { query = q }, r => r.Headers.TryAddWithoutValidation("Authorization", key));
        if (doc.RootElement.TryGetProperty("errors", out var errs)) throw new InvalidOperationException(Str(errs[0], "message") ?? "Linear error");
        var nodes = doc.RootElement.GetProperty("data").GetProperty("viewer").GetProperty("assignedIssues").GetProperty("nodes").EnumerateArray().ToList();
        Value = nodes.Count >= 50 ? "50+" : nodes.Count.ToString();
        Sub = "open issues · assigned to you";
        Rows = nodes.Take(6).Select(n => new MetricRow($"{Str(n, "identifier")}  {Str(n, "title")}", Str(n, "priorityLabel") ?? "", Str(n, "url"))).ToList();
    }
}

public sealed class SupabaseConnector : MetricConnector
{
    public override string Id => "supabase";
    public override string Name => "Supabase";
    public override string Description => "Your projects and whether they are healthy. Personal access token (Account → Access tokens).";
    public override string KeyHint => "sbp_…";
    public override string? HelpUrl => "https://supabase.com/dashboard/account/tokens";
    public override string BadgeColor => "#3ECF8E";
    public override string BadgeText => "Sb";
    public override string Unit => "projects healthy";
    public override string SampleValue => "3/3";
    public override string? OpenUrl => "https://supabase.com/dashboard/projects";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    protected override async Task FetchMetricAsync(string key)
    {
        using var doc = await Http.GetJsonAsync("https://api.supabase.com/v1/projects", Bearer(key));
        var projects = doc.RootElement.EnumerateArray().ToList();
        int healthy = projects.Count(p => Str(p, "status") == "ACTIVE_HEALTHY");
        Value = $"{healthy}/{projects.Count}";
        Sub = "projects healthy";
        Rows = projects.Select(p => new MetricRow(Str(p, "name") ?? "project", Pretty(Str(p, "status")), $"https://supabase.com/dashboard/project/{Str(p, "id")}")).ToList();
    }

    static string Pretty(string? s) => s switch
    {
        "ACTIVE_HEALTHY" => "Healthy", "INACTIVE" => "Paused", "COMING_UP" => "Starting", "GOING_DOWN" => "Pausing",
        null => "", _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.Replace('_', ' ').ToLowerInvariant()),
    };
}

public sealed class ResendConnector : MetricConnector
{
    public override string Id => "resend";
    public override string Name => "Resend";
    public override string Description => "Emails sent today and your sending domains. API key (full access is needed to list emails; sending-only keys show domains).";
    public override string KeyHint => "re_…";
    public override string? HelpUrl => "https://resend.com/api-keys";
    public override string BadgeColor => "#000000";
    public override string BadgeText => "R";
    public override string Unit => "emails today";
    public override string SampleValue => "128";
    public override string? OpenUrl => "https://resend.com/emails";

    protected override async Task FetchMetricAsync(string key)
    {
        var rows = new List<MetricRow>();
        try
        {
            using var doms = await Http.GetJsonAsync("https://api.resend.com/domains", Bearer(key));
            foreach (var d in doms.RootElement.GetProperty("data").EnumerateArray())
                rows.Add(new MetricRow(Str(d, "name") ?? "domain", Str(d, "status") ?? ""));
        }
        catch (HttpRequestException) { } // restricted keys can't list domains
        try
        {
            using var mails = await Http.GetJsonAsync("https://api.resend.com/emails?limit=100", Bearer(key));
            var list = mails.RootElement.GetProperty("data").EnumerateArray().ToList();
            var today = list.Where(m => LocalTime(Str(m, "created_at")).Date == DateTime.Today).ToList();
            Value = today.Count >= 100 ? "100+" : today.Count.ToString();
            Sub = "emails sent today";
            var series = new double[14];
            foreach (var m in list) { int i = 13 - (int)(DateTime.Today - LocalTime(Str(m, "created_at")).Date).TotalDays; if (i is >= 0 and < 14) series[i]++; }
            Series = series.ToList();
        }
        catch (HttpRequestException)
        {
            Value = rows.Count.ToString();
            Sub = "sending domains";
        }
        Rows = rows;
    }

    static DateTime LocalTime(string? iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d.ToLocalTime() : DateTime.MinValue;
}

// ------------------------------------------------------------------ Online & social

public sealed class YouTubeConnector : MetricConnector
{
    public override string Id => "youtube";
    public override string Name => "YouTube";
    public override string Description => "Subscribers, views and videos of a channel. Enter a YouTube Data API key and the channel handle, separated by a space.";
    public override string KeyHint => "AIza… @handle";
    public override string? HelpUrl => "https://console.cloud.google.com/apis/library/youtube.googleapis.com";
    public override string BadgeColor => "#FF0033";
    public override string BadgeText => "▶";
    public override string Unit => "subscribers";
    public override string SampleValue => "12.4k";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(30);
    string? _handle;
    public override string? OpenUrl => _handle != null ? $"https://www.youtube.com/{_handle}" : "https://studio.youtube.com";

    protected override async Task FetchMetricAsync(string key)
    {
        var (apiKey, handle) = Two(key, "the API key and the channel handle");
        _handle = handle.StartsWith('@') ? handle : "@" + handle;
        using var doc = await Http.GetJsonAsync($"https://www.googleapis.com/youtube/v3/channels?part=statistics,snippet&forHandle={Uri.EscapeDataString(_handle)}&key={Uri.EscapeDataString(apiKey)}");
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0) throw new InvalidOperationException("Channel not found");
        var ch = items[0];
        var st = ch.GetProperty("statistics");
        Value = Count(Num(st, "subscriberCount"));
        Sub = $"subscribers · {Str(ch.GetProperty("snippet"), "title")}";
        Rows = new() { new("Views", Count(Num(st, "viewCount"))), new("Videos", Count(Num(st, "videoCount"))) };
    }
}

public sealed class LeetCodeConnector : MetricConnector
{
    public override string Id => "leetcode";
    public override string Name => "LeetCode";
    public override string Description => "Problems solved (public profile, no key). Enter your LeetCode username.";
    public override string KeyHint => "username";
    public override string? HelpUrl => "https://leetcode.com/profile/";
    public override string BadgeColor => "#FFA116";
    public override string BadgeText => "LC";
    public override string Unit => "solved";
    public override string SampleValue => "214";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(30);
    string? _user;
    public override string? OpenUrl => _user != null ? $"https://leetcode.com/u/{_user}/" : "https://leetcode.com";

    protected override async Task FetchMetricAsync(string key)
    {
        _user = key.Trim();
        const string q = "query($u: String!) { matchedUser(username: $u) { submitStatsGlobal { acSubmissionNum { difficulty count } } } }";
        using var doc = await Http.PostJsonAsync("https://leetcode.com/graphql", new { query = q, variables = new { u = _user } },
            r => r.Headers.Referrer = new Uri("https://leetcode.com/"));
        var user = doc.RootElement.GetProperty("data").GetProperty("matchedUser");
        if (user.ValueKind == JsonValueKind.Null) throw new InvalidOperationException("No such LeetCode user");
        var counts = user.GetProperty("submitStatsGlobal").GetProperty("acSubmissionNum").EnumerateArray()
            .ToDictionary(e => Str(e, "difficulty") ?? "", e => Num(e, "count"));
        Value = Count(counts.GetValueOrDefault("All"));
        Sub = $"solved · {_user}";
        Rows = new() { new("Easy", Count(counts.GetValueOrDefault("Easy"))), new("Medium", Count(counts.GetValueOrDefault("Medium"))), new("Hard", Count(counts.GetValueOrDefault("Hard"))) };
    }
}

/// <summary>GitHub contributions (the green grid), reusing the GitHub connector's token (read:user is enough).</summary>
public sealed class GitHubActivityConnector : MetricConnector
{
    public override string Id => "githubactivity";
    public override string Name => "GitHub Activity";
    public override string Description => "Your contributions: today, this year, last 14 days. Uses the GitHub token (classic, read:user).";
    public override string? SecretName => "github.token";
    public override string KeyHint => "ghp_…";
    public override string? HelpUrl => "https://github.com/settings/tokens/new?scopes=notifications,read:user&description=Vela";
    public override string BadgeColor => "#24292F";
    public override string BadgeText => "GH";
    public override string Unit => "contributions · year";
    public override string SampleValue => "1,342";
    public override string? OpenUrl => "https://github.com";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(30);
    /// <summary>Every day of the last year (oldest first), for the card's grid.</summary>
    public List<(DateTime Day, int Count)> Days { get; private set; } = new();

    protected override async Task FetchMetricAsync(string key)
    {
        const string q = "query { viewer { login contributionsCollection { contributionCalendar { totalContributions weeks { contributionDays { contributionCount date } } } } } }";
        using var doc = await Http.PostJsonAsync("https://api.github.com/graphql", new { query = q }, Bearer(key));
        if (doc.RootElement.TryGetProperty("errors", out var errs)) throw new InvalidOperationException(Str(errs[0], "message") ?? "GitHub error");
        var viewer = doc.RootElement.GetProperty("data").GetProperty("viewer");
        var cal = viewer.GetProperty("contributionsCollection").GetProperty("contributionCalendar");
        Days = cal.GetProperty("weeks").EnumerateArray()
            .SelectMany(w => w.GetProperty("contributionDays").EnumerateArray())
            .Select(d => (DateTime.Parse(Str(d, "date")!, CultureInfo.InvariantCulture), (int)Num(d, "contributionCount"))).ToList();
        Value = Count(Num(cal, "totalContributions"));
        Sub = $"contributions · year · {Str(viewer, "login")}";
        Series = Days.Skip(Math.Max(0, Days.Count - 14)).Select(d => (double)d.Count).ToList();
        int streak = 0;
        for (int i = Days.Count - 1; i >= 0 && (Days[i].Count > 0 || Days[i].Day == DateTime.Today); i--) if (Days[i].Count > 0) streak++;
        Rows = new() { new("Today", Days.LastOrDefault().Count.ToString()), new("Current streak", $"{streak} days") };
    }
}

// ------------------------------------------------------------------ Stocks

public sealed class FinnhubConnector : MetricConnector
{
    public override string Id => "finnhub";
    public override string Name => "Stocks (Finnhub)";
    public override string Description => "Live quotes for a watchlist. Free Finnhub API key, then the symbols, separated by a space: KEY AAPL,MSFT,NVDA";
    public override string KeyHint => "key AAPL,MSFT,NVDA";
    public override string? HelpUrl => "https://finnhub.io/dashboard";
    public override string BadgeColor => "#1DB954";
    public override string BadgeText => "$";
    public override string Unit => "watchlist";
    public override string SampleValue => "AAPL 231.4";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    protected override async Task FetchMetricAsync(string key)
    {
        var (token, symbolsText) = Two(key, "the API key and your symbols");
        var symbols = symbolsText.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.ToUpperInvariant()).Take(8).ToList();
        var rows = new List<MetricRow>();
        foreach (var sym in symbols)
        {
            using var q = await Http.GetJsonAsync($"https://finnhub.io/api/v1/quote?symbol={Uri.EscapeDataString(sym)}", r => r.Headers.Add("X-Finnhub-Token", token));
            double price = Num(q.RootElement, "c"), pct = Num(q.RootElement, "dp");
            rows.Add(new MetricRow(sym, $"{price:0.##}  {(pct >= 0 ? "+" : "")}{pct:0.##}%", $"https://finance.yahoo.com/quote/{sym}"));
        }
        Rows = rows;
        Value = rows.Count > 0 ? $"{rows[0].Label} {rows[0].Value.Split(' ')[0]}" : "—";
        Sub = rows.Count > 0 ? rows[0].Value.Split("  ").Last() + " today" : "add symbols";
    }
}

/// <summary>GitHub at a glance: stars across your repositories, followers, repos, open issues. Shares the GitHub token.</summary>
public sealed class GitHubOverviewConnector : MetricConnector
{
    public override string Id => "githuboverview";
    public override string Name => "GitHub Overview";
    public override string Description => "Stars, followers, repositories and open issues. Uses the GitHub token (classic, read:user; add repo for private repos).";
    public override string? SecretName => "github.token";
    public override string KeyHint => "ghp_…";
    public override string? HelpUrl => "https://github.com/settings/tokens/new?scopes=notifications,read:user&description=Vela";
    public override string BadgeColor => "#24292F";
    public override string BadgeText => "GH";
    public override string Unit => "stars";
    public override string SampleValue => "★ 482";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(30);
    string? _login;
    public override string? OpenUrl => _login != null ? $"https://github.com/{_login}" : "https://github.com";

    protected override async Task FetchMetricAsync(string key)
    {
        void Auth(HttpRequestMessage r)
        {
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            r.Headers.Accept.ParseAdd("application/vnd.github+json");
        }
        using var me = await Http.GetJsonAsync("https://api.github.com/user", Auth);
        _login = Str(me.RootElement, "login");
        using var repos = await Http.GetJsonAsync("https://api.github.com/user/repos?per_page=100&affiliation=owner&sort=updated", Auth);
        var list = repos.RootElement.EnumerateArray().ToList();
        double stars = list.Sum(r => Num(r, "stargazers_count")), issues = list.Sum(r => Num(r, "open_issues_count"));
        Value = "★ " + Count(stars);
        Sub = $"stars · {_login}";
        Rows = new()
        {
            new("Followers", Count(Num(me.RootElement, "followers"))),
            new("Repositories", Count(list.Count)),
            new("Open issues & PRs", Count(issues)),
        };
        Rows.AddRange(list.OrderByDescending(r => Num(r, "stargazers_count")).Take(3)
            .Select(r => new MetricRow(Str(r, "name") ?? "repo", "★ " + Count(Num(r, "stargazers_count")), Str(r, "html_url"))));
    }
}

public sealed record WallPhoto(string Id, string Thumb, string Full, string Author, string AuthorUrl, string DownloadLocation, string Page);

/// <summary>
/// Unsplash photos for the Wallpaper Gallery (free "Access key" from unsplash.com/developers). Follows Unsplash's API
/// guidelines: hotlinked thumbnails, photographer credit with links, and a ping to download_location when one is used.
/// </summary>
public sealed class UnsplashConnector : MetricConnector
{
    public override string Id => "unsplash";
    public override string Name => "Unsplash";
    public override string Description => "Wallpapers from Unsplash. Create a free app at unsplash.com/developers and paste its Access Key.";
    public override string KeyHint => "Access Key";
    public override string? HelpUrl => "https://unsplash.com/oauth/applications";
    public override string BadgeColor => "#111111";
    public override string BadgeText => "U";
    public override string Unit => "wallpapers";
    public override string SampleValue => "6 new";
    protected override TimeSpan Interval => TimeSpan.FromHours(6);
    public List<WallPhoto> Photos { get; private set; } = new();
    public string Topic { get; set; } = "nature,landscape";

    protected override async Task FetchMetricAsync(string key)
    {
        var url = $"https://api.unsplash.com/photos/random?count=8&orientation=landscape&content_filter=high&query={Uri.EscapeDataString(Topic)}";
        using var doc = await Http.GetJsonAsync(url, r => r.Headers.TryAddWithoutValidation("Authorization", "Client-ID " + key));
        Photos = doc.RootElement.EnumerateArray().Select(p =>
        {
            var urls = p.GetProperty("urls");
            var user = p.GetProperty("user");
            return new WallPhoto(Str(p, "id") ?? "", Str(urls, "small") ?? "", Str(urls, "full") ?? Str(urls, "raw") ?? "",
                Str(user, "name") ?? "Unknown", (user.TryGetProperty("links", out var ul) ? Str(ul, "html") : null) ?? "https://unsplash.com",
                p.GetProperty("links").TryGetProperty("download_location", out var dl) ? dl.GetString() ?? "" : "",
                (p.GetProperty("links").TryGetProperty("html", out var ph) ? ph.GetString() : null) ?? "https://unsplash.com");
        }).Where(p => p.Thumb.Length > 0).ToList();
        Value = $"{Photos.Count} new";
        Sub = "wallpapers · " + Topic.Split(',')[0];
    }

    /// <summary>Download the full photo, set it as the desktop wallpaper, and tell Unsplash it was used.</summary>
    public async Task<string> ApplyAsync(WallPhoto photo)
    {
        var key = Secrets.Get(SecretName!) ?? throw new InvalidOperationException("Connect Unsplash first");
        if (photo.DownloadLocation.Length > 0)
            try { using var _ = await Http.GetJsonAsync(photo.DownloadLocation, r => r.Headers.TryAddWithoutValidation("Authorization", "Client-ID " + key)); } catch { }
        var dir = Paths.Dir("wallpapers");
        var file = Path.Combine(dir, $"unsplash-{photo.Id}.jpg");
        if (!File.Exists(file))
        {
            var bytes = await Http.Client.GetByteArrayAsync(photo.Full + (photo.Full.Contains('?') ? "&" : "?") + "w=3840&q=85&fm=jpg");
            await File.WriteAllBytesAsync(file, bytes);
        }
        Shell.WinActions.SetWallpaper(file);
        return file;
    }
}
