using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace Vela.Tests;

public sealed class HardwareTests(ITestOutputHelper output)
{
    /// <summary>The GPU sensor reads a plausible temperature, or reports none: never garbage, never a crash.</summary>
    [Fact]
    public void GpuTemperatureIsPlausibleOrMissing()
    {
        var type = typeof(Vela.Services.SystemMonitor).Assembly.GetType("Vela.Services.GpuThermal")!;
        var value = (double?)type.GetMethod("Hottest", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        output.WriteLine($"GPU temperature: {(value is { } t ? $"{t:0.0} °C" : "not exposed")}");
        if (value is { } c) Assert.InRange(c, 1, 129);
    }
}
