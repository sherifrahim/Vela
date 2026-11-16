using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vela.Connectors;
using Vela.Core;
using Vela.Theming;
using Xunit;

namespace Vela.Tests;

public sealed class FmtTests
{
    [Theory]
    [InlineData(42, "42s")]
    [InlineData(90, "1m")]
    [InlineData(7380, "2h 3m")]
    public void Duration_UsesCompactUnits(int seconds, string expected) =>
        Assert.Equal(expected, Fmt.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Countdown_ClampsNegativeAndFormatsHours()
    {
        Assert.Equal("0:00", Fmt.Countdown(TimeSpan.FromSeconds(-1)));
        Assert.Equal("1:02:03", Fmt.Countdown(new TimeSpan(1, 2, 3)));
    }

    [Theory]
    [InlineData(999, "999")]
    [InlineData(12500, "12.5K")]
    [InlineData(2300000, "2.3M")]
    public void Tokens_UsesReadableSuffixes(long value, string expected) => Assert.Equal(expected, Fmt.Tokens(value));
}

public sealed class ConnectorParserTests
{
    [Theory]
    [InlineData("claude-opus-5-5-20261001", "Opus 5.5")]
    [InlineData("claude-sonnet-4-8-20260901", "Sonnet 4.8")]
    [InlineData(null, "—")]
    public void ClaudeModelNames_AreFriendly(string? model, string expected) =>
        Assert.Equal(expected, ClaudeConnector.PrettyModel(model));

    [Fact]
    public void ClaudeLogScanner_CountsUsageAndDeduplicatesStreamingWrites()
    {
        string project = Path.Combine(TestEnvironment.Root, "claude", "projects", "sample");
        Directory.CreateDirectory(project);
        string line = JsonSerializer.Serialize(new
        {
            timestamp = DateTime.UtcNow,
            requestId = "request-1",
            message = new
            {
                id = "message-1",
                model = "claude-opus-5-5-20261001",
                usage = new { input_tokens = 10, output_tokens = 20, cache_creation_input_tokens = 3, cache_read_input_tokens = 4 },
            },
        });
        File.WriteAllText(Path.Combine(project, "session.jsonl"), line + "\n" + line + "\n");

        var scannerType = typeof(ClaudeConnector).GetNestedType("LocalLogScanner", BindingFlags.NonPublic)!;
        var scanner = Activator.CreateInstance(scannerType, nonPublic: true)!;
        var result = (ITuple)scannerType.GetMethod("Scan")!.Invoke(scanner, null)!;

        Assert.Equal(37L, result[0]);
        Assert.Equal(37L, result[1]);
        Assert.Equal("Opus 5.5", result[2]);
        Assert.Equal(37L, result[3]);
    }

    [Fact]
    public void CodexRateLimits_ParseBothWindows()
    {
        long reset = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        string line = JsonSerializer.Serialize(new
        {
            timestamp = DateTime.UtcNow,
            payload = new
            {
                rate_limits = new
                {
                    plan_type = "plus",
                    primary = new { used_percent = 23.5, window_minutes = 300, resets_at = reset },
                    secondary = new { used_percent = 71.0, window_minutes = 10080, resets_at = reset },
                },
            },
        });
        var parse = typeof(CodexConnector).GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (ITuple)parse.Invoke(null, new object[] { line })!;
        var windows = Assert.IsType<List<UsageWindow>>(result[0]);

        Assert.Equal("plus", result[2]);
        Assert.Collection(windows,
            session => { Assert.Equal("Session", session.Label); Assert.Equal(23.5, session.Percent); },
            weekly => { Assert.Equal("Weekly", weekly.Label); Assert.Equal(71, weekly.Percent); });
    }
}

public sealed class SettingsMigrationTests
{
    [Fact]
    public void VersionOne_AddsOneControlCentreBeforeClock()
    {
        string path = Path.Combine(TestEnvironment.Root, "settings.json");
        var old = new AppSettings
        {
            Version = 1,
            FirstRunDone = true,
            Widgets = new() { new() { Type = "apps" }, new() { Type = "clock" } },
        };
        File.WriteAllText(path, JsonSerializer.Serialize(old, Json.Options));

        Settings.Load();
        Settings.Load(); // migration must be idempotent

        Assert.Equal(2, Settings.Current.Version);
        Assert.Equal(new[] { "apps", "controlcenter", "clock" }, Settings.Current.Widgets.Select(w => w.Type));
    }
}

public sealed class WallpaperMappingTests
{
    [Fact]
    public void BrushFor_MapsNegativeMonitorCoordinatesIntoImageViewbox()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var pixels = Enumerable.Repeat((byte)255, 100 * 50 * 4).ToArray();
                var image = BitmapSource.Create(100, 50, 96, 96, PixelFormats.Bgra32, null, pixels, 400);
                image.Freeze();
                SetStatic("_image", image);
                SetStatic("_imgW", 100d);
                SetStatic("_imgH", 50d);

                var brush = Assert.IsType<ImageBrush>(WallpaperGlass.BrushFor(
                    new Rect(-150, 50, 100, 25), new Rect(-200, 0, 200, 100)));

                Assert.Equal(new Rect(0.25, 0.5, 0.5, 0.25), brush.Viewbox);
                Assert.True(brush.IsFrozen);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
    }

    static void SetStatic(string name, object value) =>
        typeof(WallpaperGlass).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, value);
}
