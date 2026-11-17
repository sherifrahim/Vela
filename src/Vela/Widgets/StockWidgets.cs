using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Vela.Core;
using Vela.Shell;
using Vela.UI;
using Vela.Views;
using static Vela.UI.UI;

namespace Vela.Widgets;

/// <summary>Price history for one symbol from Yahoo Finance's public chart endpoint (no key, no account).</summary>
static class StockFeed
{
    public sealed record Quote(string Symbol, string Name, string Currency, double Price, double ChangePct, List<double> Closes);

    public static async Task<Quote> GetAsync(string symbol, string range = "1mo", string interval = "1d")
    {
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range={range}&interval={interval}";
        using var doc = await Http.GetJsonAsync(url);
        var chart = doc.RootElement.GetProperty("chart");
        if (chart.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            throw new InvalidOperationException(err.TryGetProperty("description", out var d) ? d.GetString() : "Unknown symbol");
        var r = chart.GetProperty("result")[0];
        var meta = r.GetProperty("meta");
        double price = meta.GetProperty("regularMarketPrice").GetDouble();
        double prev = meta.TryGetProperty("chartPreviousClose", out var pc) && pc.ValueKind == JsonValueKind.Number ? pc.GetDouble() : price;
        double pct = meta.TryGetProperty("regularMarketChangePercent", out var cp) && cp.ValueKind == JsonValueKind.Number ? cp.GetDouble()
            : prev != 0 ? (price - prev) / prev * 100 : 0;
        var closes = new List<double>();
        if (r.TryGetProperty("indicators", out var ind) && ind.GetProperty("quote")[0].TryGetProperty("close", out var cl))
            foreach (var c in cl.EnumerateArray()) if (c.ValueKind == JsonValueKind.Number) closes.Add(c.GetDouble());
        string name = meta.TryGetProperty("shortName", out var sn) ? sn.GetString() ?? symbol : symbol;
        string cur = meta.TryGetProperty("currency", out var cu) ? cu.GetString() ?? "" : "";
        return new Quote(meta.GetProperty("symbol").GetString() ?? symbol, name, cur, price, pct, closes);
    }

    public static string Price(double p) => p >= 1000 ? p.ToString("#,0.##", CultureInfo.CurrentCulture) : p.ToString("0.00", CultureInfo.CurrentCulture);
}

/// <summary>Reference "Stock Chart": symbol, price, day change and a one-month line. Card: change symbol and range.</summary>
public sealed class StockChartWidget : WidgetBase
{
    TextBlock _symbol = null!, _price = null!, _change = null!;
    Sparkline _spark = null!;
    StockFeed.Quote? _quote;
    protected override int TickSeconds => 300;
    string Symbol => Cfg("symbol", "AAPL");

    protected override UIElement BuildContent()
    {
        _symbol = Text("", 7.5, FontWeights.Bold, "Text.Secondary");
        _price = Text("—", 13, FontWeights.SemiBold);
        _change = Text("", 7.5, FontWeights.Bold, "Sys.Green");
        _spark = new Sparkline("Sys.Green") { Width = 62, Height = 24, Margin = new Thickness(10, 0, 2, 0) };
        return H(V(H(_symbol, _change.Margin(5, 0, 0, 0)), _price.Margin(0, -1, 0, 0)), _spark);
    }

    protected override void OnTick(DateTime now) => _ = LoadAsync();

    async Task LoadAsync()
    {
        try
        {
            _quote = await StockFeed.GetAsync(Symbol);
            Render();
        }
        catch (Exception ex)
        {
            _symbol.Text = Symbol;
            _price.Text = "—";
            _change.Text = "";
            ToolTip = "Couldn't load " + Symbol + ": " + ex.Message;
        }
    }

    void Render()
    {
        var q = _quote;
        if (q == null) return;
        bool up = q.ChangePct >= 0;
        _symbol.Text = q.Symbol;
        _price.Text = StockFeed.Price(q.Price);
        _change.Text = $"{(up ? "+" : "")}{q.ChangePct:0.00}%";
        _change.Fg(up ? "Sys.Green" : "Sys.Red");
        _spark.SetResourceReference(Sparkline.StrokeProperty, up ? "Sys.Green" : "Sys.Red");
        _spark.SetValues(q.Closes);
        ToolTip = $"{q.Name} · {q.Currency}";
    }

    protected override double CardWidth => 380;
    protected override UIElement BuildCard()
    {
        var q = _quote;
        var chart = new Sparkline(q != null && q.ChangePct < 0 ? "Sys.Red" : "Sys.Green") { Height = 110, Margin = new Thickness(0, 10, 0, 6) };
        if (q != null) chart.SetValues(q.Closes);
        var rangeLabel = Text("1 month", 11, null, "Text.Tertiary");
        var ranges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        foreach (var (label, range, interval) in new[] { ("1D", "1d", "5m"), ("5D", "5d", "30m"), ("1M", "1mo", "1d"), ("6M", "6mo", "1d"), ("1Y", "1y", "1wk") })
        {
            var (r, i, l) = (range, interval, label);
            ranges.Children.Add(PillButton(label, async () =>
            {
                try { var data = await StockFeed.GetAsync(Symbol, r, i); chart.SetValues(data.Closes); rangeLabel.Text = l switch { "1D" => "Today", "5D" => "5 days", "1M" => "1 month", "6M" => "6 months", _ => "1 year" }; }
                catch (Exception ex) { rangeLabel.Text = ex.Message; }
            }).Margin(0, 0, 6, 0));
        }
        var box = TextBox(Symbol, "Symbol: AAPL, NVDA, BTC-USD, ^GSPC…");
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || box.Text.Trim().Length == 0) return;
            SetCfg("symbol", box.Text.Trim().ToUpperInvariant());
            FlyoutWindow.CloseCurrent();
            _ = LoadAsync();
        };
        var head = q == null ? Text("Loading…", 13, null, "Text.Secondary")
            : (UIElement)V(Text(q.Name, 13, FontWeights.SemiBold, "Text.Secondary"),
                H(Value(StockFeed.Price(q.Price), 30), Text($"  {(q.ChangePct >= 0 ? "+" : "")}{q.ChangePct:0.00}% today", 13, FontWeights.SemiBold, q.ChangePct >= 0 ? "Sys.Green" : "Sys.Red").Margin(0, 0, 0, 6)));
        var open = GhostButton(Text($"Open {Symbol} on Yahoo Finance ↗", 12, FontWeights.SemiBold, "Accent"), () => WindowTracker.OpenUri($"https://finance.yahoo.com/quote/{Uri.EscapeDataString(Symbol)}"));
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Padding = new Thickness(0, 8, 4, 0);
        return V(Card.Header(Symbol), head, chart, rangeLabel, ranges, Label("Change symbol (Enter)").Margin(0, 0, 0, 6), box,
            Text("Prices from Yahoo Finance, may be delayed.", 11, null, "Text.Tertiary").Margin(0, 8, 0, 0), open);
    }
}

/// <summary>
/// Reference "Wallpaper Gallery": fresh Unsplash photos; click one to make it the desktop wallpaper. Needs a free
/// Unsplash Access Key (Settings → Connections or the card). Photographers are credited, as Unsplash asks.
/// </summary>
public sealed class WallpaperGalleryWidget : WidgetBase
{
    Connectors.UnsplashConnector C => (Connectors.UnsplashConnector)Svc.Account("unsplash");
    System.Windows.Controls.Image _thumb = null!;
    TextBlock _title = null!, _sub = null!;

    protected override UIElement BuildContent()
    {
        _thumb = new System.Windows.Controls.Image { Width = 44, Height = 28, Stretch = System.Windows.Media.Stretch.UniformToFill };
        var frame = new Border { Width = 44, Height = 28, CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = _thumb }.Bg("Fill.Control");
        frame.Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 44, 28), 6, 6);
        _title = Text("Wallpapers", 10, FontWeights.SemiBold);
        _sub = Text("", 7, FontWeights.SemiBold, "Text.Secondary");
        return H(frame, V(_title, _sub).Margin(9, 0, 3, 0));
    }

    protected override void OnAttach() { if (!IsPreview) C.Changed += Render; Render(); }
    protected override void OnDetach() { if (!IsPreview) C.Changed -= Render; }

    void Render()
    {
        var c = C;
        _sub.Text = !c.IsConnected ? "sample · click to connect" : c.Error ?? (c.Photos.Count > 0 ? $"{c.Photos.Count} new · Unsplash" : "loading…");
        var first = c.Photos.FirstOrDefault();
        _thumb.Source = first == null ? null : Thumb(first.Thumb, 120);
    }

    static System.Windows.Media.ImageSource? Thumb(string url, int width)
    {
        try
        {
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit(); bi.UriSource = new Uri(url); bi.DecodePixelWidth = width; bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.EndInit();
            return bi;
        }
        catch { return null; }
    }

    protected override double CardWidth => 420;
    protected override UIElement BuildCard()
    {
        var c = C;
        var header = Card.Header("Wallpapers");
        if (!c.IsConnected) return V(header, Card.ConnectForm(c, FlyoutWindow.CloseCurrent));
        var status = Text("Click a photo to set it as your wallpaper.", 11.5, null, "Text.Secondary");
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(-4, 4, -4, 6) };
        foreach (var p in c.Photos.Take(8))
        {
            var photo = p;
            var img = new System.Windows.Controls.Image { Source = Thumb(p.Thumb, 360), Stretch = System.Windows.Media.Stretch.UniformToFill, Height = 104 };
            var tile = new Border { Height = 104, CornerRadius = new CornerRadius(10), Margin = new Thickness(4), ClipToBounds = true, Child = img, Cursor = Cursors.Hand };
            tile.Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, 184, 104), 10, 10);
            tile.SizeChanged += (_, e) => tile.Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 10, 10);
            tile.ToolTip = $"Photo by {p.Author} on Unsplash";
            tile.MouseLeftButtonUp += async (_, e) =>
            {
                e.Handled = true;
                status.Text = $"Setting the photo by {photo.Author}…";
                try { await c.ApplyAsync(photo); status.Text = $"Wallpaper set · photo by {photo.Author} on Unsplash"; }
                catch (Exception ex) { status.Text = "Couldn't set it: " + ex.Message; }
            };
            var credit = GhostButton(Text($"{p.Author}", 10.5, FontWeights.SemiBold, "Text.Secondary"), () => WindowTracker.OpenUri(photo.AuthorUrl + "?utm_source=vela&utm_medium=referral"));
            credit.Padding = new Thickness(6, 0, 6, 0);
            credit.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(V(tile, credit));
        }
        if (c.Photos.Count == 0) grid.Children.Add(Text(c.Error ?? "Loading photos…", 12, null, "Text.Secondary"));
        var topics = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (label, q) in new[] { ("Nature", "nature,landscape"), ("Mountains", "mountains"), ("Ocean", "ocean"), ("City", "city,night"), ("Space", "space,galaxy"), ("Minimal", "minimal,abstract") })
        {
            var topic = q;
            topics.Children.Add(PillButton(label, async () => { c.Topic = topic; await c.RefreshAsync(); FlyoutWindow.Navigate(BuildCard()); }, accent: c.Topic == q).Margin(0, 0, 5, 0));
        }
        var footer = Card.Footer("Photos from Unsplash.", "More", async () => { await c.RefreshAsync(); FlyoutWindow.Navigate(BuildCard()); });
        return V(header, topics, grid, status, footer);
    }

    protected override void AddMenuItems(ContextMenu menu)
    {
        menu.Items.Add(Item("New photos", () => _ = C.RefreshAsync()));
        if (C.IsConnected) menu.Items.Add(Item("Logout", C.Disconnect));
    }
}
