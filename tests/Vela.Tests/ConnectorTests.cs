using Vela.Connectors;
using Xunit;

namespace Vela.Tests;

public sealed class ConnectorTests
{
    static string Msg(string role, DateTime created, long input, long output, double cost = 0, string model = "m1", long cacheRead = 0) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            id = "msg_1", role, modelID = model, cost,
            time = new { created = new DateTimeOffset(created).ToUnixTimeMilliseconds() },
            tokens = new { input, output, reasoning = 0, cache = new { read = cacheRead, write = 0 } },
        });

    [Fact]
    public void OpenCodeSummaryCountsAssistantTokensByDay()
    {
        var now = new DateTime(2026, 11, 17, 15, 0, 0);
        var s = OpenCodeConnector.Summarize(new[]
        {
            Msg("assistant", now.AddHours(-1), 100, 50, 0.10, "sonnet", cacheRead: 25), // today: 175
            Msg("assistant", now.AddDays(-2), 1000, 0, 0.50, "opus"),                    // this week
            Msg("assistant", now.AddDays(-10), 5000, 0, 9, "opus"),                       // in the chart, not the week
            Msg("assistant", now.AddDays(-30), 9999, 0, 9),                               // too old
            Msg("user", now, 777, 0),                                                     // not a model answer
            "not json",
        }, now);

        Assert.Equal(175, s.TokensToday);
        Assert.Equal(1175, s.Tokens7);
        Assert.Equal(0.60m, s.Cost7);
        Assert.Equal("opus", s.TopModel);
        Assert.Equal(175, s.Daily[13]);
        Assert.Equal(1000, s.Daily[11]);
        Assert.Equal(5000, s.Daily[3]);
    }

    [Fact]
    public async Task GoogleRedirectIsReadFromTheLoopbackSocket()
    {
        var read = typeof(GoogleAuth).GetMethod("ReadRedirectAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            using var browser = new System.Net.Sockets.TcpClient();
            await browser.ConnectAsync(System.Net.IPAddress.Loopback, port);
            var request = System.Text.Encoding.ASCII.GetBytes("GET /?state=abc&code=4%2F0Ab-x&scope=gmail HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
            await browser.GetStream().WriteAsync(request);

            using var server = await listener.AcceptTcpClientAsync();
            var answer = await (Task<Dictionary<string, string>?>)read.Invoke(null, new object[] { server, CancellationToken.None })!;

            Assert.NotNull(answer);
            Assert.Equal("abc", answer!["state"]);
            Assert.Equal("4/0Ab-x", answer["code"]);
            var reply = new System.IO.StreamReader(browser.GetStream()).ReadLine();
            Assert.StartsWith("HTTP/1.1 200", reply);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void GoogleClientNeedsIdAndSecret()
    {
        Assert.Equal(("id.apps.googleusercontent.com", "secret"), GoogleAuth.ParseClient("  id.apps.googleusercontent.com   secret "));
        Assert.Throws<InvalidOperationException>(() => GoogleAuth.ParseClient("only-one-part"));
    }
}
