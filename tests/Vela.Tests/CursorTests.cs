using System.Text.Json;
using Vela.Connectors;
using Xunit;

namespace Vela.Tests;

public sealed class CursorTests
{
    [Fact]
    public void ParsesMonthlyUsageWithPlanAndSplit()
    {
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var end = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        using var usage = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            billingCycleStart = start.ToString(), billingCycleEnd = end.ToString(),
            planUsage = new { totalPercentUsed = 37.4, autoPercentUsed = 30.0, apiPercentUsed = 7.4 },
        }));
        using var plan = JsonDocument.Parse("""{"planInfo":{"planName":"PRO","includedUsagePeriod":"INCLUDED_USAGE_PERIOD_MONTHLY"}}""");
        var r = CursorConnector.Parse(usage.RootElement, plan.RootElement);
        Assert.Equal(37.4, r.Percent, 3);
        Assert.False(r.Weekly);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(end), r.ResetsAt);
        Assert.Equal("Pro", r.Plan);
        Assert.Equal(30.0, r.Auto);
        Assert.Equal(7.4, r.ApiShare);
    }

    [Fact]
    public void SevenDayCycleIsWeeklyAndNoSplitWhenAllAuto()
    {
        var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var usage = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            billingCycleStart = start, billingCycleEnd = start + 7 * 86400_000L,
            planUsage = new { totalPercentUsed = 150.0, autoPercentUsed = 150.0, apiPercentUsed = 0.0 },
        }));
        var r = CursorConnector.Parse(usage.RootElement, null);
        Assert.True(r.Weekly);
        Assert.Equal(100, r.Percent);
        Assert.Null(r.Auto);
        Assert.Null(r.ApiShare);
    }

    [Fact]
    public void JwtExpiry()
    {
        static string Jwt(long exp) => "x." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{{\"exp\":{exp}}}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".y";
        Assert.True(CursorConnector.ExpiresSoon(Jwt(DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds())));
        Assert.False(CursorConnector.ExpiresSoon(Jwt(DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds())));
        Assert.False(CursorConnector.ExpiresSoon("not-a-jwt"));
    }
}
