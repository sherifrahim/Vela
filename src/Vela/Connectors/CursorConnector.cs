// Cursor plan usage. Sign-in flow, endpoints and response shape ported from OpenTaskbar (MIT License,
// Copyright (c) 2026 OpenTaskbar contributors, github.com/alztrk/opentaskbar; full notice in THIRD_PARTY_NOTICES.md):
// Services/App/CursorAuthService.cs and the Cursor part of Services/App/AiUsageService.cs.
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vela.Core;

namespace Vela.Connectors;

/// <summary>
/// How much of the Cursor plan's included usage is spent this period. Browser sign-in with PKCE: Vela opens
/// cursor.com, polls Cursor's auth endpoint until the login completes, and keeps the tokens in Secrets (DPAPI).
/// Never reads Cursor's own files.
/// </summary>
public sealed class CursorConnector : MetricConnector
{
    const string ClientId = "KbZUR41cY7W6zRSdpSUJ7I7mLYBKOCmB";
    const string Api = "https://api2.cursor.sh";
    const string Secret = "cursor.auth";

    public override string Id => "cursor";
    public override string Name => "Cursor";
    public override string Description => "How much of your Cursor plan's included usage is spent this period. Sign in with Cursor in the browser; Vela keeps the sign-in encrypted on this PC.";
    public override ConnectorAuth Auth => ConnectorAuth.LocalSignIn;
    public override string? SecretName => Secret;
    public override bool IsConnected => true;                 // nothing to paste: the card goes straight to "Sign in"
    public override bool NeedsSignIn => !Secrets.Has(Secret);
    public override bool CanSignOut => Secrets.Has(Secret);
    public override string SignInLabel => "Sign in with Cursor";
    public override string Status => NeedsSignIn ? "Not signed in" : base.Status;
    public override string? HelpUrl => "https://cursor.com/dashboard";
    public override string BadgeColor => "#0B0B0B";
    public override string BadgeText => "Cu";
    public override string Unit => "of included usage";
    public override string SampleValue => "42%";
    public override string? OpenUrl => "https://cursor.com/dashboard?tab=usage";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(10);

    public override async Task SignInAsync()
    {
        var verifier = B64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var uuid = Guid.NewGuid().ToString("D");
        Shell.WindowTracker.OpenUri($"https://cursor.com/loginDeepControl?challenge={challenge}&uuid={uuid}&mode=login");
        var poll = $"{Api}/auth/poll?uuid={Uri.EscapeDataString(uuid)}&verifier={Uri.EscapeDataString(verifier)}";
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(2000);
            try
            {
                using var doc = await Http.GetJsonAsync(poll);
                if (Str(doc.RootElement, "accessToken") is { Length: > 0 } access)
                {
                    Save(access, Str(doc.RootElement, "refreshToken"));
                    _ = RefreshAsync();
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException) { } // still waiting
        }
        throw new TimeoutException("The browser sign-in didn't finish within 3 minutes");
    }

    public override void Disconnect() => Secrets.Set(Secret, null);

    static void Save(string access, string? refresh) =>
        Secrets.Set(Secret, JsonSerializer.Serialize(new { accessToken = access, refreshToken = refresh }));

    protected override async Task FetchMetricAsync(string key)
    {
        using var stored = JsonDocument.Parse(key);
        string? access = Str(stored.RootElement, "accessToken"), refresh = Str(stored.RootElement, "refreshToken");
        if (access == null || ExpiresSoon(access)) access = await RenewAsync(refresh) ?? access;
        if (access == null) throw new InvalidOperationException("Sign in again");

        JsonDocument usage;
        try { usage = await Dashboard(access, "GetCurrentPeriodUsage"); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && refresh != null)
        {
            access = await RenewAsync(refresh) ?? throw new InvalidOperationException("Sign in again");
            usage = await Dashboard(access, "GetCurrentPeriodUsage");
        }
        JsonDocument? plan = null;
        try { plan = await Dashboard(access, "GetPlanInfo"); } catch (HttpRequestException) { }
        using (usage) using (plan)
        {
            var r = Parse(usage.RootElement, plan?.RootElement);
            Value = $"{r.Percent:0}%";
            Sub = (r.Weekly ? "used this week" : "used this month") + (r.ResetsAt is { } at ? " · " + Fmt.ResetsIn(at) : "");
            var rows = new List<MetricRow>();
            if (r.Plan != null) rows.Add(new MetricRow("Plan", r.Plan));
            if (r.Auto is { } a) rows.Add(new MetricRow("Auto", $"{a:0}%"));
            if (r.ApiShare is { } p) rows.Add(new MetricRow("API", $"{p:0}%"));
            Rows = rows;
        }
    }

    public sealed record Usage(double Percent, bool Weekly, DateTimeOffset? ResetsAt, string? Plan, double? Auto, double? ApiShare);

    /// <summary>GetCurrentPeriodUsage (+ optional GetPlanInfo) → percent used, period and reset.</summary>
    public static Usage Parse(JsonElement usage, JsonElement? planDoc)
    {
        var pu = usage.GetProperty("planUsage");
        double raw = Num(pu, "totalPercentUsed"), total = Math.Clamp(raw, 0, 100);
        string? planName = null, period = null;
        long? planReset = null;
        if (planDoc is { } pd && pd.ValueKind == JsonValueKind.Object)
        {
            var pi = pd.TryGetProperty("planInfo", out var x) && x.ValueKind == JsonValueKind.Object ? x : pd;
            planName = Str(pi, "planName");
            period = Str(pi, "includedUsagePeriod");
            planReset = Ms(pi, "includedUsageResetsAt") ?? Ms(pi, "billingCycleEnd");
        }
        long? start = Ms(usage, "billingCycleStart"), end = Ms(usage, "billingCycleEnd") ?? planReset;
        long? windowSecs = start is { } s && end is { } e && e > s ? (e - s) / 1000 : null;
        bool weekly = period == "INCLUDED_USAGE_PERIOD_WEEKLY"
            || (period != "INCLUDED_USAGE_PERIOD_MONTHLY" && windowSecs is >= 5 * 86400 and <= 9 * 86400);
        double? auto = pu.TryGetProperty("autoPercentUsed", out _) ? Num(pu, "autoPercentUsed") : null;
        double? api = pu.TryGetProperty("apiPercentUsed", out _) ? Num(pu, "apiPercentUsed") : null;
        bool split = api is > 0.05 || (auto is { } au && Math.Abs(au - raw) >= 0.5);
        return new Usage(total, weekly, end is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null,
            planName == null ? null : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(planName.ToLowerInvariant()),
            split ? auto : null, split && api > 0 ? api : null);
    }

    static long? Ms(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l > 0 ? l : null;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l > 0 ? l : null;
        return null;
    }

    static Task<JsonDocument> Dashboard(string access, string method) =>
        Http.PostJsonAsync($"{Api}/aiserver.v1.DashboardService/{method}", new { }, r =>
        {
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            r.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");
        });

    async Task<string?> RenewAsync(string? refresh)
    {
        if (string.IsNullOrEmpty(refresh)) return null;
        try
        {
            using var doc = await Http.PostJsonAsync($"{Api}/oauth/token", new { grant_type = "refresh_token", client_id = ClientId, refresh_token = refresh });
            var root = doc.RootElement;
            bool logout = root.TryGetProperty("shouldLogout", out var lo) && lo.ValueKind == JsonValueKind.True;
            if (logout || Str(root, "access_token") is not { Length: > 0 } access) { Disconnect(); return null; }
            Save(access, Str(root, "refresh_token") ?? refresh);
            return access;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Disconnect();
            return null;
        }
    }

    /// <summary>True when the JWT's exp is within 5 minutes (unreadable: false, just try it).</summary>
    public static bool ExpiresSoon(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return false;
        try
        {
            var p = parts[1].Replace('-', '+').Replace('_', '/');
            p += (p.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            using var doc = JsonDocument.Parse(Convert.FromBase64String(p));
            return doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var secs)
                && DateTimeOffset.FromUnixTimeSeconds(secs) <= DateTimeOffset.UtcNow.AddMinutes(5);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { return false; }
    }

    static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
