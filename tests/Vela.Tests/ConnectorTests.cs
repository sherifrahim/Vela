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

public sealed class IcsTests
{
    static string Cal(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("", events) + "END:VCALENDAR\r\n";

    static string Ev(params string[] lines) => "BEGIN:VEVENT\r\n" + string.Join("\r\n", lines) + "\r\nEND:VEVENT\r\n";

    [Fact]
    public void FloatingAllDayFoldedAndCancelledEvents()
    {
        var text = Cal(
            Ev("UID:a", "SUMMARY:Design re", " view", "DTSTART:20261118T140000", "DTEND:20261118T150000"),
            Ev("UID:b", "SUMMARY:Holiday", "DTSTART;VALUE=DATE:20261120", "DTEND;VALUE=DATE:20261121"),
            Ev("UID:c", "SUMMARY:Gone", "STATUS:CANCELLED", "DTSTART:20261118T090000", "DTEND:20261118T100000"));
        var e = Ics.Expand(text, new DateTime(2026, 11, 17), new DateTime(2026, 12, 1)).OrderBy(x => x.Start).ToList();

        Assert.Equal(2, e.Count);
        Assert.Equal("Design review", e[0].Title);          // folded line joined
        Assert.Equal(new DateTime(2026, 11, 18, 14, 0, 0), e[0].Start);
        Assert.True(e[1].AllDay);
        Assert.Equal(new DateTime(2026, 11, 21), e[1].End);
    }

    [Fact]
    public void WeeklyRuleWithDaysCountExdateAndMovedInstance()
    {
        var text = Cal(
            Ev("UID:s", "SUMMARY:Standup", "DTSTART:20261116T090000", "DTEND:20261116T091500",
               "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=6", "EXDATE:20261118T090000"),
            Ev("UID:s", "SUMMARY:Standup (moved)", "RECURRENCE-ID:20261120T090000", "DTSTART:20261120T110000", "DTEND:20261120T111500"));
        var e = Ics.Expand(text, new DateTime(2026, 11, 16), new DateTime(2026, 12, 31)).OrderBy(x => x.Start).ToList();

        // Occurrences: 16, 18 (excluded), 20 (moved to 11:00), 23, 25, 27 → COUNT=6 stops there.
        Assert.Equal(new[] { 16, 20, 23, 25, 27 }, e.Select(x => x.Start.Day).ToArray());
        Assert.Equal("Standup (moved)", e[1].Title);
        Assert.Equal(11, e[1].Start.Hour);
        Assert.All(e, x => Assert.Equal(TimeSpan.FromMinutes(15), x.End - x.Start));
    }

    [Fact]
    public void UtcAndZonedTimesBecomeLocal()
    {
        var text = Cal(
            Ev("UID:u", "SUMMARY:UTC", "DTSTART:20261118T120000Z", "DURATION:PT30M"),
            Ev("UID:z", "SUMMARY:Tokyo", "DTSTART;TZID=Asia/Tokyo:20261118T090000", "DTEND;TZID=Asia/Tokyo:20261118T100000"));
        var e = Ics.Expand(text, new DateTime(2026, 11, 17), new DateTime(2026, 11, 20)).ToDictionary(x => x.Title);

        Assert.Equal(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc).ToLocalTime(), e["UTC"].Start);
        Assert.Equal(TimeSpan.FromMinutes(30), e["UTC"].End - e["UTC"].Start);
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        Assert.Equal(TimeZoneInfo.ConvertTime(new DateTime(2026, 11, 18, 9, 0, 0), tokyo, TimeZoneInfo.Local), e["Tokyo"].Start);
    }
}

public sealed class BadgeTests
{
    [Theory]
    [InlineData("OpenAI.CodexBeta_2p2nqsd0c76g0!App", @"C:\Program Files\WindowsApps\OpenAI.CodexBeta_26.727.4816.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe", true)]
    [InlineData("OpenAI.CodexBeta_2p2nqsd0c76g0!App", @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1.0.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe", false)]
    [InlineData("OpenAI.CodexBeta_2p2nqsd0c76g0!App", @"C:\Program Files\WindowsApps\OpenAI.CodexBeta_26.727.4816.0_x64__otherpublisher\app.exe", false)]
    [InlineData("com.squirrel.Discord.Discord", @"C:\Users\x\AppData\Local\Discord\app-1.0\Discord.exe", false)]
    public void PackagedAppsMatchTheirInstallFolder(string aumid, string exe, bool expected) =>
        Assert.Equal(expected, Vela.Services.NotificationService.FromPackage(aumid, exe));
}
