using System.Globalization;
using System.Text.Json;
using Vela.Core;

namespace Vela.Services;

public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Storm }

public sealed record WeatherDay(DateTime Date, double Max, double Min, int Code, double PrecipChance);
public sealed record WeatherHour(DateTime Time, double Temp, int Code, bool IsDay);
public sealed record GeoResult(string Name, string? Region, string? Country, double Lat, double Lon)
{
    public string Display => string.Join(", ", new[] { Name, Region, Country }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>
/// Weather from Open-Meteo (free, no key, no account). Only the chosen city's coordinates are sent.
/// Refreshes every 15 minutes while a weather widget is on the dock.
/// </summary>
public sealed class WeatherService
{
    DateTime _lastFetch;
    bool _fetching;

    public bool HasData { get; private set; }
    public double Temp { get; private set; }
    public double FeelsLike { get; private set; }
    public double Wind { get; private set; }
    public int Humidity { get; private set; }
    public int Code { get; private set; }
    public bool IsDay { get; private set; } = true;
    public DateTime Sunrise { get; private set; }
    public DateTime Sunset { get; private set; }
    public List<WeatherDay> Days { get; private set; } = new();
    public List<WeatherHour> Hours { get; private set; } = new();
    public string? Error { get; private set; }

    public bool Configured => Settings.Current.Weather.Lat != null;
    public string Unit => Settings.Current.Weather.Unit == "F" ? "F" : "C";

    Action? _changed;
    public event Action? Changed
    {
        add { bool first = _changed == null; _changed += value; if (first) { Services.Ticker.Tick += OnTick; _ = RefreshAsync(); } }
        remove { _changed -= value; if (_changed == null) Services.Ticker.Tick -= OnTick; }
    }

    public WeatherService()
    {
        Settings.Changed += what => { if (what == "weather") { _lastFetch = default; _ = RefreshAsync(); } };
    }

    void OnTick(DateTime now)
    {
        if ((now - _lastFetch).TotalMinutes >= 15) _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var w = Settings.Current.Weather;
        if (_fetching || w.Lat == null || w.Lon == null) return;
        _fetching = true;
        _lastFetch = DateTime.Now;
        try
        {
            string unit = w.Unit == "F" ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "";
            var lat = w.Lat.Value.ToString("0.####", CultureInfo.InvariantCulture);
            var lon = w.Lon.Value.ToString("0.####", CultureInfo.InvariantCulture);
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m" +
                "&hourly=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_probability_max" +
                $"&timezone=auto&forecast_days=7{unit}";
            using var doc = await Http.GetJsonAsync(url);
            Parse(doc.RootElement);
            Error = null;
            HasData = true;
        }
        catch (Exception ex)
        {
            Error = "Weather unavailable";
            Log.Warn("Weather fetch failed: " + ex.Message);
        }
        finally
        {
            _fetching = false;
            _changed?.Invoke();
        }
    }

    void Parse(JsonElement root)
    {
        var cur = root.GetProperty("current");
        Temp = cur.GetProperty("temperature_2m").GetDouble();
        FeelsLike = cur.GetProperty("apparent_temperature").GetDouble();
        Humidity = (int)cur.GetProperty("relative_humidity_2m").GetDouble();
        Code = cur.GetProperty("weather_code").GetInt32();
        IsDay = cur.GetProperty("is_day").GetInt32() == 1;
        Wind = cur.GetProperty("wind_speed_10m").GetDouble();

        var d = root.GetProperty("daily");
        var dates = d.GetProperty("time").EnumerateArray().Select(e => DateTime.Parse(e.GetString()!, CultureInfo.InvariantCulture)).ToList();
        var max = d.GetProperty("temperature_2m_max").EnumerateArray().Select(N).ToList();
        var min = d.GetProperty("temperature_2m_min").EnumerateArray().Select(N).ToList();
        var codes = d.GetProperty("weather_code").EnumerateArray().Select(e => (int)N(e)).ToList();
        var precip = d.GetProperty("precipitation_probability_max").EnumerateArray().Select(N).ToList();
        Days = dates.Select((dt, i) => new WeatherDay(dt, max[i], min[i], codes[i], precip[i])).ToList();
        Sunrise = DateTime.Parse(d.GetProperty("sunrise")[0].GetString()!, CultureInfo.InvariantCulture);
        Sunset = DateTime.Parse(d.GetProperty("sunset")[0].GetString()!, CultureInfo.InvariantCulture);

        var h = root.GetProperty("hourly");
        var htimes = h.GetProperty("time").EnumerateArray().Select(e => DateTime.Parse(e.GetString()!, CultureInfo.InvariantCulture)).ToList();
        var htemps = h.GetProperty("temperature_2m").EnumerateArray().Select(N).ToList();
        var hcodes = h.GetProperty("weather_code").EnumerateArray().Select(e => (int)N(e)).ToList();
        var hday = h.GetProperty("is_day").EnumerateArray().Select(e => N(e) == 1).ToList();
        // Open-Meteo returns local times for the location; good enough for "next hours" in the user's own city.
        var nowHour = DateTime.Now.AddMinutes(-DateTime.Now.Minute);
        Hours = htimes.Select((t, i) => new WeatherHour(t, htemps[i], hcodes[i], hday[i]))
                      .Where(x => x.Time >= nowHour.AddHours(-0.5)).Take(12).ToList();
    }

    static double N(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0;

    public static async Task<List<GeoResult>> SearchCityAsync(string query)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?count=6&language=en&format=json&name={Uri.EscapeDataString(query)}";
        using var doc = await Http.GetJsonAsync(url);
        if (!doc.RootElement.TryGetProperty("results", out var arr)) return new();
        return arr.EnumerateArray().Select(r => new GeoResult(
            r.GetProperty("name").GetString() ?? "",
            r.TryGetProperty("admin1", out var a) ? a.GetString() : null,
            r.TryGetProperty("country", out var c) ? c.GetString() : null,
            r.GetProperty("latitude").GetDouble(),
            r.GetProperty("longitude").GetDouble())).ToList();
    }

    public static WeatherKind Kind(int wmo) => wmo switch
    {
        0 or 1 => WeatherKind.Clear,
        2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        51 or 53 or 55 or 56 or 57 => WeatherKind.Drizzle,
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => WeatherKind.Rain,
        71 or 73 or 75 or 77 or 85 or 86 => WeatherKind.Snow,
        95 or 96 or 99 => WeatherKind.Storm,
        _ => WeatherKind.Cloudy,
    };

    public static string Describe(int wmo) => wmo switch
    {
        0 => "Clear", 1 => "Mostly clear", 2 => "Partly cloudy", 3 => "Overcast",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle", 56 or 57 => "Freezing drizzle",
        61 => "Light rain", 63 => "Rain", 65 => "Heavy rain", 66 or 67 => "Freezing rain",
        71 => "Light snow", 73 => "Snow", 75 => "Heavy snow", 77 => "Snow grains",
        80 or 81 => "Showers", 82 => "Heavy showers", 85 or 86 => "Snow showers",
        95 => "Thunderstorm", 96 or 99 => "Thunderstorm, hail",
        _ => "—",
    };
}
