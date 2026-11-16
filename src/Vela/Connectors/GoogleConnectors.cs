using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vela.Core;

namespace Vela.Connectors;

/// <summary>
/// Google sign-in for one connector, the way the reference does it: the user's OWN Google Cloud OAuth client
/// ("Desktop app" type) — Vela has no client of its own and no server. Installed-app flow with PKCE: the browser
/// signs in and redirects to a one-shot listener on 127.0.0.1; the refresh token is stored with Secrets (DPAPI).
/// Read-only scopes only. While the user's client is in Google's "testing" mode, refresh tokens expire after
/// 7 days and the tile asks to sign in again.
/// </summary>
public sealed class GoogleAuth(string tokenSecret, string scope)
{
    string? _access;
    DateTime _expires;

    public bool SignedIn => Secrets.Has(tokenSecret);
    public void Forget() { Secrets.Set(tokenSecret, null); _access = null; }

    public static (string id, string secret) ParseClient(string key)
    {
        var parts = key.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new InvalidOperationException("Enter the OAuth client ID and client secret, separated by a space");
        return (parts[0], parts[1]);
    }

    public async Task SignInAsync(string clientKey)
    {
        var (clientId, clientSecret) = ParseClient(clientKey);
        var verifier = B64(RandomNumberGenerator.GetBytes(32));
        var challenge = B64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = B64(RandomNumberGenerator.GetBytes(16));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirect = $"http://127.0.0.1:{port}";
            var url = "https://accounts.google.com/o/oauth2/v2/auth?" + Query(new()
            {
                ["client_id"] = clientId, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = scope,
                ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["access_type"] = "offline",
                ["prompt"] = "consent", ["state"] = state,
            });
            Shell.WindowTracker.OpenUri(url);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            Dictionary<string, string>? answer = null;
            while (answer == null)
            {
                using var client = await listener.AcceptTcpClientAsync(cts.Token);
                answer = await ReadRedirectAsync(client, cts.Token); // browsers may open idle pre-connections: skip them
            }
            if (answer.GetValueOrDefault("state") != state) throw new InvalidOperationException("Sign-in answer didn't match; try again");
            if (answer.TryGetValue("error", out var err)) throw new InvalidOperationException("Google said: " + err);
            if (!answer.TryGetValue("code", out var code)) throw new InvalidOperationException("No sign-in code received");

            using var doc = await TokenAsync(new()
            {
                ["code"] = code, ["client_id"] = clientId, ["client_secret"] = clientSecret, ["redirect_uri"] = redirect,
                ["grant_type"] = "authorization_code", ["code_verifier"] = verifier,
            });
            var refresh = doc.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
            if (string.IsNullOrEmpty(refresh)) throw new InvalidOperationException("Google returned no refresh token");
            Remember(doc.RootElement);
            Secrets.Set(tokenSecret, refresh);
        }
        finally { listener.Stop(); }
    }

    /// <summary>A valid access token, refreshed when it has (nearly) expired.</summary>
    public async Task<string> AccessTokenAsync(string clientKey)
    {
        if (_access != null && DateTime.UtcNow < _expires) return _access;
        var refresh = Secrets.Get(tokenSecret) ?? throw new InvalidOperationException("Sign in with Google");
        var (clientId, clientSecret) = ParseClient(clientKey);
        try
        {
            using var doc = await TokenAsync(new()
            {
                ["refresh_token"] = refresh, ["client_id"] = clientId, ["client_secret"] = clientSecret, ["grant_type"] = "refresh_token",
            });
            Remember(doc.RootElement);
            return _access!;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            Forget(); // revoked, or the 7-day testing-mode limit: ask to sign in again
            throw new InvalidOperationException("Google sign-in expired; sign in again");
        }
    }

    void Remember(JsonElement token)
    {
        _access = token.GetProperty("access_token").GetString();
        int seconds = token.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
        _expires = DateTime.UtcNow.AddSeconds(seconds - 60);
    }

    static async Task<JsonDocument> TokenAsync(Dictionary<string, string> form)
    {
        using var res = await Http.Client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form));
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"{(int)res.StatusCode}: {(body.Length > 200 ? body[..200] : body)}", null, res.StatusCode);
        return JsonDocument.Parse(body);
    }

    static async Task<Dictionary<string, string>?> ReadRedirectAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var stream = client.GetStream();
            using var lineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lineCts.CancelAfter(TimeSpan.FromSeconds(10));
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
            var line = await reader.ReadLineAsync(lineCts.Token);
            if (line == null || !line.StartsWith("GET ")) return null;
            var path = line.Split(' ')[1];
            int q = path.IndexOf('?');
            if (q < 0) { await Respond(stream, 404, "Not found"); return null; }
            var args = path[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "");
            if (!args.ContainsKey("code") && !args.ContainsKey("error")) { await Respond(stream, 404, "Not found"); return null; }
            await Respond(stream, 200, args.ContainsKey("code") ? "Signed in. You can close this tab and go back to Vela." : "Sign-in was cancelled. You can close this tab.");
            return args;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; } // idle pre-connection
    }

    static async Task Respond(NetworkStream stream, int status, string message)
    {
        var html = $"<!doctype html><meta charset=utf-8><title>Vela</title><body style=\"font:16px system-ui;padding:40px;background:#111;color:#eee\">{WebUtility.HtmlEncode(message)}</body>";
        var bytes = Encoding.UTF8.GetBytes($"HTTP/1.1 {status} OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(html)}\r\nConnection: close\r\n\r\n{html}");
        await stream.WriteAsync(bytes);
    }

    static string Query(Dictionary<string, string> q) => string.Join("&", q.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Base for Google connectors: the key field holds "client-id client-secret"; a browser sign-in follows.</summary>
public abstract class GoogleConnector : MetricConnector
{
    readonly GoogleAuth _auth;
    protected GoogleConnector(string scope) => _auth = new GoogleAuth(Id + ".refresh", scope);

    public override string KeyHint => "client-id.apps.googleusercontent.com client-secret";
    public override string? HelpUrl => "https://console.cloud.google.com/apis/credentials";
    public override bool NeedsSignIn => IsConnected && !_auth.SignedIn;
    public override string SignInLabel => "Sign in with Google";
    public override string Status => NeedsSignIn ? "Client saved · sign in next" : base.Status;

    public override async Task SignInAsync()
    {
        var key = Secrets.Get(SecretName!) ?? throw new InvalidOperationException("Save the OAuth client first");
        await _auth.SignInAsync(key);
        await RefreshAsync();
    }

    public override void Disconnect() { _auth.Forget(); base.Disconnect(); }

    protected Task<string> TokenAsync(string key) => _auth.AccessTokenAsync(key);
}

/// <summary>Unread mail in the Gmail inbox and the latest unread senders (read-only scope).</summary>
public sealed class GmailConnector : GoogleConnector
{
    public GmailConnector() : base("https://www.googleapis.com/auth/gmail.readonly") { }
    public override string Id => "gmail";
    public override string Name => "Gmail";
    public override string Description => "Unread mail in your inbox. Create a Google Cloud OAuth client (Desktop app), enable the Gmail API, add yourself as a test user, then paste the client ID and secret separated by a space and sign in. Read-only.";
    public override string BadgeColor => "#EA4335";
    public override string BadgeText => "M";
    public override string Unit => "unread";
    public override string SampleValue => "12";
    public override string? OpenUrl => "https://mail.google.com/mail/u/0/#inbox";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(2);

    protected override async Task FetchMetricAsync(string key)
    {
        var token = await TokenAsync(key);
        var auth = Bearer(token);
        using var inbox = await Http.GetJsonAsync("https://gmail.googleapis.com/gmail/v1/users/me/labels/INBOX", auth);
        Value = Count(Num(inbox.RootElement, "messagesUnread"));
        Sub = "unread · inbox";
        using var list = await Http.GetJsonAsync("https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=5&q=" + Uri.EscapeDataString("is:unread in:inbox"), auth);
        var rows = new List<MetricRow>();
        if (list.RootElement.TryGetProperty("messages", out var msgs))
            foreach (var m in msgs.EnumerateArray())
            {
                var id = Str(m, "id");
                using var msg = await Http.GetJsonAsync($"https://gmail.googleapis.com/gmail/v1/users/me/messages/{id}?format=metadata&metadataHeaders=From&metadataHeaders=Subject", auth);
                string from = "", subject = "";
                foreach (var h in msg.RootElement.GetProperty("payload").GetProperty("headers").EnumerateArray())
                {
                    if (Str(h, "name") == "From") from = Str(h, "value") ?? "";
                    if (Str(h, "name") == "Subject") subject = Str(h, "value") ?? "";
                }
                int lt = from.IndexOf('<');
                if (lt > 0) from = from[..lt].Trim().Trim('"');
                rows.Add(new MetricRow(from.Length > 0 ? from : "(unknown)", subject.Length > 40 ? subject[..40] + "…" : subject,
                    $"https://mail.google.com/mail/u/0/#inbox/{Str(msg.RootElement, "threadId")}"));
            }
        Rows = rows;
    }
}

/// <summary>Search Console clicks, impressions, CTR and position for your first verified site (read-only scope).</summary>
public sealed class SearchConsoleConnector : GoogleConnector
{
    public SearchConsoleConnector() : base("https://www.googleapis.com/auth/webmasters.readonly") { }
    public override string Id => "searchconsole";
    public override string Name => "Search Console";
    public override string Description => "Google search clicks for your site over 28 days. Google Cloud OAuth client (Desktop app) with the Search Console API enabled: paste the client ID and secret separated by a space, then sign in. Read-only.";
    public override string BadgeColor => "#4285F4";
    public override string BadgeText => "G";
    public override string Unit => "clicks · 28 days";
    public override string SampleValue => "3.2k";
    public override string SeriesLabel => "Clicks per day (Google reports with a 2–3 day delay)";
    public override string? OpenUrl => "https://search.google.com/search-console";
    protected override TimeSpan Interval => TimeSpan.FromMinutes(60);

    protected override async Task FetchMetricAsync(string key)
    {
        var token = await TokenAsync(key);
        var auth = Bearer(token);
        using var sites = await Http.GetJsonAsync("https://www.googleapis.com/webmasters/v3/sites", auth);
        var site = sites.RootElement.TryGetProperty("siteEntry", out var entries)
            ? entries.EnumerateArray().Select(e => (url: Str(e, "siteUrl"), perm: Str(e, "permissionLevel"))).FirstOrDefault(e => e.perm != "siteUnverifiedUser").url
            : null;
        if (site == null) throw new InvalidOperationException("No verified site in Search Console");
        var end = DateTime.Today.AddDays(-2);
        var body = new { startDate = end.AddDays(-27).ToString("yyyy-MM-dd"), endDate = end.ToString("yyyy-MM-dd"), dimensions = new[] { "date" }, rowLimit = 40 };
        using var q = await Http.PostJsonAsync($"https://www.googleapis.com/webmasters/v3/sites/{Uri.EscapeDataString(site)}/searchAnalytics/query", body, auth);
        var rows = q.RootElement.TryGetProperty("rows", out var rs) ? rs.EnumerateArray().ToList() : new();
        double clicks = rows.Sum(r => Num(r, "clicks")), impressions = rows.Sum(r => Num(r, "impressions"));
        double position = rows.Count == 0 ? 0 : rows.Average(r => Num(r, "position"));
        Value = Count(clicks);
        Sub = $"clicks · 28 days · {site.Replace("sc-domain:", "").Replace("https://", "").TrimEnd('/')}";
        Series = rows.Skip(Math.Max(0, rows.Count - 14)).Select(r => Num(r, "clicks")).ToList();
        Rows = new()
        {
            new("Impressions", Count(impressions)),
            new("Click-through rate", impressions > 0 ? $"{clicks / impressions * 100:0.0}%" : "—"),
            new("Average position", position > 0 ? position.ToString("0.0", CultureInfo.CurrentCulture) : "—"),
        };
    }
}
