using System.Management;
using Vela.Core;
using Windows.Devices.Radios;

namespace Vela.Services;

/// <summary>Laptop panel brightness via WMI (root\WMI). External monitors (DDC/CI) are a roadmap item.</summary>
public sealed class BrightnessService
{
    int _pending = -1;
    bool _writing;

    public bool Supported { get; private set; } = true;
    public int Level { get; private set; } = -1;

    public int Read()
    {
        if (!Supported) return -1;
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject o in s.Get())
            {
                Level = Convert.ToInt32(o["CurrentBrightness"]);
                o.Dispose();
                return Level;
            }
            Supported = false;
        }
        catch { Supported = false; }
        return -1;
    }

    /// <summary>Coalesces rapid slider moves: only the latest value is written.</summary>
    public void Set(int level)
    {
        Level = level;
        _pending = Math.Clamp(level, 0, 100);
        if (_writing) return;
        _writing = true;
        Task.Run(() =>
        {
            try
            {
                while (_pending >= 0)
                {
                    int v = _pending; _pending = -1;
                    using var cls = new ManagementClass(@"root\WMI", "WmiMonitorBrightnessMethods", null);
                    foreach (ManagementObject o in cls.GetInstances())
                    {
                        o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)v });
                        o.Dispose();
                    }
                }
            }
            catch (Exception ex) { Log.Warn("Brightness set failed: " + ex.Message); }
            finally { _writing = false; }
        });
    }
}

/// <summary>Wi-Fi / Bluetooth radio toggles (Windows.Devices.Radios).</summary>
public sealed class RadioService
{
    IReadOnlyList<Radio>? _radios;
    public event Action? Changed;

    public async Task InitAsync()
    {
        if (_radios != null) return;
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) { Log.Warn("Radio access: " + access); return; }
            _radios = await Radio.GetRadiosAsync();
            foreach (var r in _radios)
                r.StateChanged += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
            Changed?.Invoke();
        }
        catch (Exception ex) { Log.Warn("Radios unavailable: " + ex.Message); }
    }

    Radio? Find(RadioKind k) => _radios?.FirstOrDefault(r => r.Kind == k);

    public bool? IsOn(RadioKind k) => Find(k) is { } r ? r.State == RadioState.On : null;

    public async Task ToggleAsync(RadioKind k)
    {
        var r = Find(k);
        if (r == null) { WinOpen(k); return; }
        try { await r.SetStateAsync(r.State == RadioState.On ? RadioState.Off : RadioState.On); }
        catch { WinOpen(k); }
    }

    static void WinOpen(RadioKind k) => Shell.WinActions.OpenSettings(k == RadioKind.Bluetooth ? "bluetooth" : "network-wifi");
}

/// <summary>Public market data with no key: Binance (crypto), Frankfurter (FX, ECB rates).</summary>
public static class Markets
{
    public sealed record Ticker(string Symbol, decimal Price, decimal ChangePct, List<double> Line);

    public static async Task<Ticker> CryptoAsync(string symbol)
    {
        symbol = symbol.ToUpperInvariant();
        using var t = await Http.GetJsonAsync($"https://api.binance.com/api/v3/ticker/24hr?symbol={symbol}");
        using var k = await Http.GetJsonAsync($"https://api.binance.com/api/v3/klines?symbol={symbol}&interval=1h&limit=24");
        var r = t.RootElement;
        decimal price = decimal.Parse(r.GetProperty("lastPrice").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        decimal pct = decimal.Parse(r.GetProperty("priceChangePercent").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var line = k.RootElement.EnumerateArray()
            .Select(c => double.Parse(c[4].GetString()!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        return new Ticker(symbol, price, pct, line);
    }

    public static async Task<decimal> RateAsync(string from, string to)
    {
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return 1m;
        using var doc = await Http.GetJsonAsync($"https://api.frankfurter.dev/v1/latest?base={from.ToUpperInvariant()}&symbols={to.ToUpperInvariant()}");
        return doc.RootElement.GetProperty("rates").GetProperty(to.ToUpperInvariant()).GetDecimal();
    }
}
