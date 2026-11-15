using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vela.Connectors;
using Vela.Core;
using Vela.Services;
using Vela.Shell;
using Vela.UI;
using Vela.Views;
using static Vela.UI.UI;

namespace Vela.Widgets;

static class BrandBadges
{
    // Generic marks (not the companies' logos): a coral square with a burst, a dark square with a prompt.
    public static FrameworkElement Claude(double size = 32)
    {
        var b = Badge(new BurstMark { Width = size * 0.6, Height = size * 0.6 }, Brush("#D97757"), size, size * 0.26);
        return b;
    }

    public static FrameworkElement Codex(double size = 32) =>
        Badge(Text(">_", size * 0.36, FontWeights.Bold, "OnAccent").Center(), Brush("#10A37F"), size, size * 0.26);

    public static FrameworkElement GitHub(double size = 28) =>
        Badge(Icon(Glyphs.Code, size * 0.48, "OnAccent"), Brush("#24292F"), size, size * 0.28);

    public static FrameworkElement Stripe(double size = 18) =>
        Badge(Text("S", size * 0.62, FontWeights.Bold, "OnAccent").Center(), Brush("#635BFF"), size, size / 2);
}

/// <summary>Eight-ray burst drawn in code.</summary>
sealed class BurstMark : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight), c = s / 2;
        var pen = new Pen(Brushes.White, s * 0.13) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int i = 0; i < 6; i++)
        {
            double a = i * Math.PI / 6;
            dc.DrawLine(pen, new Point(c + Math.Cos(a) * c * 0.9, c + Math.Sin(a) * c * 0.9), new Point(c - Math.Cos(a) * c * 0.9, c - Math.Sin(a) * c * 0.9));
        }
    }
}

/// <summary>Shared tile for AI plan limits: badge + Session / Weekly bars + a third line.</summary>
public abstract class UsageTileBase : WidgetBase
{
    readonly Action<double>[] _setters = new Action<double>[2];
    readonly TextBlock[] _labels = new TextBlock[3], _resets = new TextBlock[3], _values = new TextBlock[3];

    protected abstract FrameworkElement MakeBadge();
    protected abstract IConnector Connector { get; }

    protected override UIElement BuildContent()
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 1, 0) };
        // Reference columns: label | bar | reset time | value.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        for (int r = 0; r < 3; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(11.5) });
            _labels[r] = Text("", 8.5, FontWeights.Bold).Row(r);
            _resets[r] = Text("", 7, null, "Text.Secondary").Row(r).Col(2).Right();
            _resets[r].Margin = new Thickness(0, 0, 3, 0);
            _values[r] = Text("", 8.5, FontWeights.Bold).Row(r).Col(3).Right();
            grid.Children.Add(_labels[r]); grid.Children.Add(_resets[r]); grid.Children.Add(_values[r]);
            if (r < 2)
            {
                var bar = Progress(out _setters[r], "Sys.Green", 3).Row(r).Col(1);
                bar.Margin = new Thickness(0, 0, 3, 0);
                grid.Children.Add(bar);
            }
        }
        return H(MakeBadge(), grid);
    }

    protected override void OnAttach() { Connector.Changed += Render; Render(); }
    protected override void OnDetach() => Connector.Changed -= Render;

    protected void SetRow(int r, string label, double? pct, string reset, string? valueOverride = null)
    {
        _labels[r].Text = label;
        _resets[r].Text = reset;
        _values[r].Text = valueOverride ?? (pct is { } p ? $"{p:0}%" : "—");
        if (r < 2 && pct is { } v) _setters[r](v / 100);
    }

    protected static string ShortReset(DateTimeOffset? at)
    {
        if (at == null) return "";
        var d = at.Value - DateTimeOffset.Now;
        if (d <= TimeSpan.Zero) return "now";
        return d.TotalHours < 24 ? $"{(int)d.TotalHours}h {d.Minutes}m" : at.Value.LocalDateTime.ToString("d ddd");
    }

    protected abstract void Render();
}

public sealed class ClaudeWidget : UsageTileBase
{
    protected override IConnector Connector => Svc.Claude;
    protected override FrameworkElement MakeBadge() => BrandBadges.Claude();

    protected override void Render()
    {
        var c = Svc.Claude;
        string noLimits = c.IsConnected ? "" : "sign in";
        SetRow(0, "Session", c.Session?.Percent, c.Session == null ? noLimits : ShortReset(c.Session.ResetsAt));
        SetRow(1, "Weekly", c.Weekly?.Percent, c.Weekly == null ? noLimits : ShortReset(c.Weekly.ResetsAt));
        SetRow(2, c.TopModel ?? "Tokens", null, c.TopModel != null ? "most used" : "this week", Fmt.Tokens(c.TopModel != null ? c.TopModelTokens : c.TokensWeek));
        ToolTip = c.Error;
    }

    protected override double CardWidth => 400;
    protected override UIElement BuildCard()
    {
        var c = Svc.Claude;
        var rows = new StackPanel();
        foreach (var w in c.Windows) rows.Children.Add(Card.UsageRow(w.Label, w.Percent, Fmt.ResetsIn(w.ResetsAt)));
        if (c.Windows.Count == 0)
        {
            var msg = Text(c.Error ?? "Loading…", 13, FontWeights.SemiBold, "Text.Secondary").Margin(0, 0, 0, 6);
            var hint = Text("Install Claude Code, run `claude` and sign in with a Pro or Max plan. Token counts work without a sign-in.", 12, null, "Text.Tertiary");
            hint.TextWrapping = TextWrapping.Wrap;
            rows.Children.Add(V(msg, hint).Margin(0, 0, 0, 16));
        }
        var tokens = Columns("*,*,*",
            V(Label("Today"), Value(Fmt.Tokens(c.TokensToday), 18)),
            V(Label("7 days"), Value(Fmt.Tokens(c.TokensWeek), 18)),
            V(Label("Most used"), Value(c.TopModel ?? "—", 18)));
        tokens.Margin = new Thickness(0, 4, 0, 18);
        return V(Card.Header("Claude", BrandBadges.Claude(30), c.Plan), rows, tokens,
            Card.Footer("Read from your Claude Code sign-in.", "Refresh", async () => { await c.RefreshAsync(); RefreshCard(); }));
    }
}

public sealed class CodexWidget : UsageTileBase
{
    protected override IConnector Connector => Svc.Codex;
    protected override FrameworkElement MakeBadge() => BrandBadges.Codex();

    protected override void Render()
    {
        var c = Svc.Codex;
        var s = c.Windows.FirstOrDefault(w => w.Label == "Session");
        var w = c.Windows.FirstOrDefault(w => w.Label == "Weekly");
        SetRow(0, "Session", s?.Percent, ShortReset(s?.ResetsAt));
        SetRow(1, "Weekly", w?.Percent, ShortReset(w?.ResetsAt));
        SetRow(2, c.Plan != null ? char.ToUpperInvariant(c.Plan[0]) + c.Plan[1..] : "Codex", null, c.AsOf is { } t ? "as of" : "", c.AsOf?.ToString("t") ?? "");
        ToolTip = c.Error;
    }

    protected override double CardWidth => 400;
    protected override UIElement BuildCard()
    {
        var c = Svc.Codex;
        var rows = new StackPanel();
        foreach (var w in c.Windows) rows.Children.Add(Card.UsageRow(w.Label, w.Percent, Fmt.ResetsIn(w.ResetsAt)));
        if (c.Windows.Count == 0)
        {
            var hint = Text((c.Error ?? "Loading…") + "\nInstall the Codex CLI, run `codex login` (ChatGPT sign-in) and use it once.", 12.5, null, "Text.Secondary");
            hint.TextWrapping = TextWrapping.Wrap;
            rows.Children.Add(hint.Margin(0, 0, 0, 16));
        }
        var asOf = c.AsOf is { } t ? $"As of your last Codex turn ({t:g})." : "Read from your Codex sign-in.";
        return V(Card.Header("ChatGPT", BrandBadges.Codex(30), c.Plan), rows,
            Card.Footer(asOf, "Refresh", async () => { await c.RefreshAsync(); RefreshCard(); }));
    }
}

/// <summary>Two providers on one tile: Claude session + Codex session.</summary>
public sealed class AiLimitsWidget : WidgetBase
{
    Action<double> _a = null!, _b = null!;
    TextBlock _av = null!, _bv = null!;

    protected override UIElement BuildContent()
    {
        FrameworkElement Row(FrameworkElement badge, out Action<double> set, out TextBlock v)
        {
            var bar = Progress(out set, "Sys.Green", 3);
            bar.Width = 64;
            v = Text("—", 8.5, FontWeights.Bold).Margin(6, 0, 0, 0);
            return H(badge, bar.Margin(6, 0, 0, 0), v);
        }
        return V(Row(BrandBadges.Claude(14), out _a, out _av).Margin(0, 0, 0, 4), Row(BrandBadges.Codex(14), out _b, out _bv));
    }

    protected override void OnAttach() { Svc.Claude.Changed += Render; Svc.Codex.Changed += Render; Render(); }
    protected override void OnDetach() { Svc.Claude.Changed -= Render; Svc.Codex.Changed -= Render; }

    void Render()
    {
        var c = Svc.Claude.Session?.Percent;
        var x = Svc.Codex.Windows.FirstOrDefault()?.Percent;
        _a(c / 100 ?? 0); _av.Text = c is { } cv ? $"{cv:0}%" : "—";
        _b(x / 100 ?? 0); _bv.Text = x is { } xv ? $"{xv:0}%" : "—";
    }

    protected override void OnClick() => FlyoutWindow.Toggle(this, V(Card.Header("AI Limits"),
        Label("Claude"), Card.UsageRow("Session", Svc.Claude.Session?.Percent ?? 0, Fmt.ResetsIn(Svc.Claude.Session?.ResetsAt)),
        Label("ChatGPT"), Card.UsageRow("Session", Svc.Codex.Windows.FirstOrDefault()?.Percent ?? 0, Fmt.ResetsIn(Svc.Codex.Windows.FirstOrDefault()?.ResetsAt))), 360);
}

/// <summary>"$15 · net revenue - 30 days" (Stripe).</summary>
public sealed class StripeWidget : WidgetBase
{
    TextBlock _value = null!, _sub = null!;

    protected override UIElement BuildContent()
    {
        _value = Value("—", 15);
        _sub = Text("net revenue · 30 days", 7, FontWeights.SemiBold, "Text.Secondary");
        var g = new Grid();
        g.Children.Add(V(_value, _sub).Margin(1, 0, 22, 0));
        var badge = BrandBadges.Stripe(13);
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        badge.VerticalAlignment = VerticalAlignment.Top;
        badge.Margin = new Thickness(0, 5, 0, 0);
        g.Children.Add(badge);
        return g;
    }

    protected override void OnAttach() { Svc.Stripe.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Stripe.Changed -= Render;

    void Render()
    {
        var s = Svc.Stripe;
        _value.Text = s.IsConnected ? (s.LastUpdated != null ? s.Display : "…") : "$—";
        _sub.Text = !s.IsConnected ? "connect Stripe" : s.Error ?? (s.TestMode ? "test mode · 30 days" : "net revenue · 30 days");
    }

    protected override double CardWidth => 360;
    protected override UIElement BuildCard()
    {
        var s = Svc.Stripe;
        if (!s.IsConnected) return V(Card.Header("Stripe", BrandBadges.Stripe(28)), Card.ConnectForm(s, FlyoutWindow.CloseCurrent));
        var chart = new Sparkline("Accent") { Height = 70, Bars = true, Margin = new Thickness(0, 8, 0, 6) };
        chart.SetValues(s.Daily);
        return V(Card.Header("Stripe", BrandBadges.Stripe(28), s.TestMode ? "Test mode" : null),
            Label("Net revenue · last 30 days"), Value(s.Display, 30), chart, Text("Last 14 days", 11, null, "Text.Tertiary"),
            Card.Footer(s.Status, "Disconnect", () => { Secrets.Set(s.SecretName!, null); FlyoutWindow.CloseCurrent(); }));
    }

    protected override void AddMenuItems(ContextMenu menu)
    {
        if (Svc.Stripe.IsConnected) menu.Items.Add(Item("Logout", () => Secrets.Set(Svc.Stripe.SecretName!, null)));
        menu.Items.Add(Item("Refresh", () => _ = Svc.Stripe.RefreshAsync()));
    }
}

public sealed class GitHubWidget : WidgetBase
{
    TextBlock _count = null!, _sub = null!;

    protected override UIElement BuildContent()
    {
        _count = Value("—", 15);
        _sub = Text("notifications", 7.5, FontWeights.SemiBold, "Text.Secondary");
        return H(BrandBadges.GitHub(), V(_count, _sub).Margin(8, 0, 3, 0));
    }

    protected override void OnAttach() { Svc.GitHub.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.GitHub.Changed -= Render;

    void Render()
    {
        var g = Svc.GitHub;
        _count.Text = !g.IsConnected ? "—" : g.Items.Count >= 50 ? "50+" : g.Items.Count.ToString();
        _sub.Text = !g.IsConnected ? "connect GitHub" : g.Error ?? "unread";
    }

    protected override double CardWidth => 380;
    protected override UIElement BuildCard()
    {
        var g = Svc.GitHub;
        if (!g.IsConnected) return V(Card.Header("GitHub", BrandBadges.GitHub(28)), Card.ConnectForm(g, FlyoutWindow.CloseCurrent));
        var list = new StackPanel();
        foreach (var n in g.Items.Take(12))
        {
            var t = Text(n.Title, 13, FontWeights.SemiBold);
            var row = V(t, Text($"{n.Repo} · {n.Type} · {n.Updated:MMM d}", 11, null, "Text.Secondary"));
            var b = GhostButton(row, () => WindowTracker.OpenUri(n.Url));
            b.Margin = new Thickness(-8, 0, -8, 2);
            list.Children.Add(b);
        }
        if (g.Items.Count == 0) list.Children.Add(Text("You're all caught up.", 13, null, "Text.Secondary").Margin(0, 0, 0, 10));
        return V(Card.Header("GitHub", BrandBadges.GitHub(28), g.Login),
            new ScrollViewer { Content = list, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Card.Footer(g.Status, "Open inbox", () => WindowTracker.OpenUri("https://github.com/notifications")));
    }

    protected override void AddMenuItems(ContextMenu menu)
    {
        if (Svc.GitHub.IsConnected) menu.Items.Add(Item("Logout", () => Secrets.Set(Svc.GitHub.SecretName!, null)));
        menu.Items.Add(Item("Refresh", () => _ = Svc.GitHub.RefreshAsync()));
    }
}

/// <summary>Coin price, 24h change and the day's line (Binance public API, no key).</summary>
public sealed class CryptoWidget : WidgetBase
{
    TextBlock _sym = null!, _price = null!, _chg = null!;
    Sparkline _line = null!;
    DateTime _last;
    protected override int TickSeconds => 5;

    string Symbol => Cfg("symbol", "BTCUSDT");

    protected override UIElement BuildContent()
    {
        _sym = Text("", 8.5, FontWeights.Bold, "Text.Secondary");
        _price = Text("…", 14, FontWeights.Bold);
        _chg = Text("", 8.5, FontWeights.Bold);
        _line = new Sparkline("Sys.Green") { Width = 50, Height = 20, Margin = new Thickness(10, 0, 0, 0) };
        return H(V(H(_sym, _chg.Margin(6, 0, 0, 0)), _price).Margin(2, 0, 0, 0), _line);
    }

    protected override async void OnTick(DateTime now)
    {
        if ((now - _last).TotalSeconds < 60) return;
        _last = now;
        try
        {
            var t = await Markets.CryptoAsync(Symbol);
            _sym.Text = t.Symbol.Replace("USDT", "");
            _price.Text = t.Price >= 1000 ? t.Price.ToString("#,0") : t.Price.ToString("#,0.####");
            _chg.Text = $"{(t.ChangePct >= 0 ? "+" : "")}{t.ChangePct:0.0}%";
            var key = t.ChangePct >= 0 ? "Sys.Green" : "Sys.Red";
            _chg.Fg(key);
            _line.SetResourceReference(Sparkline.StrokeProperty, key);
            _line.SetValues(t.Line);
        }
        catch (Exception ex) { _sym.Text = Symbol; _price.Text = "offline"; Log.Warn("Crypto: " + ex.Message); }
    }

    protected override void AddMenuItems(ContextMenu menu)
    {
        foreach (var s in new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT", "BNBUSDT", "XRPUSDT" })
            menu.Items.Add(Item(s.Replace("USDT", "/USDT"), () => { SetCfg("symbol", s); _last = default; OnTick(DateTime.Now); }, s == Symbol));
    }

    protected override bool Clickable => false;
}

/// <summary>Current temperature, high/low and city. Card: next hours + week.</summary>
public sealed class WeatherWidget : WidgetBase
{
    WeatherIcon _icon = null!;
    TextBlock _temp = null!, _hl = null!, _city = null!;

    protected override UIElement BuildContent()
    {
        _icon = new WeatherIcon { Width = 24, Height = 24 };
        _temp = Value("—", 16);
        _hl = Text("", 7.5, FontWeights.SemiBold, "Text.Secondary");
        _city = Text("", 8.5, FontWeights.Bold);
        _city.MaxWidth = 90;
        return H(_icon, _temp.Margin(6, 0, 8, 0), V(_city, _hl));
    }

    protected override void OnAttach() { Svc.Weather.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Weather.Changed -= Render;

    void Render()
    {
        var w = Svc.Weather;
        _city.Text = Settings.Current.Weather.City ?? "Set city";
        if (!w.HasData) { _temp.Text = "—"; _hl.Text = w.Configured ? "loading" : "in Settings"; return; }
        _icon.Set(WeatherService.Kind(w.Code), !w.IsDay);
        _temp.Text = $"{w.Temp:0}°";
        var today = w.Days.FirstOrDefault();
        _hl.Text = today != null ? $"H {today.Max:0}°  L {today.Min:0}°" : WeatherService.Describe(w.Code);
    }

    protected override double CardWidth => 380;
    protected override UIElement BuildCard()
    {
        var w = Svc.Weather;
        if (!w.Configured) return V(Card.Header("Weather"), Text("Pick your city in Settings → Weather.", 13, null, "Text.Secondary"),
            PillButton("Open Settings", () => { FlyoutWindow.CloseCurrent(); DockWindow.Instance?.OpenSettings(); }, true).Margin(0, 12, 0, 0));
        var bigIcon = new WeatherIcon { Width = 56, Height = 56 };
        bigIcon.Set(WeatherService.Kind(w.Code), !w.IsDay);
        var head = H(bigIcon, V(Value($"{w.Temp:0}°", 36), Text(WeatherService.Describe(w.Code), 13, FontWeights.SemiBold, "Text.Secondary")).Margin(14, 0, 0, 0));
        var details = Columns("*,*,*",
            V(Label("Feels like"), Text($"{w.FeelsLike:0}°", 14, FontWeights.Bold)),
            V(Label("Wind"), Text($"{w.Wind:0} {(w.Unit == "F" ? "mph" : "km/h")}", 14, FontWeights.Bold)),
            V(Label("Humidity"), Text($"{w.Humidity}%", 14, FontWeights.Bold)));
        details.Margin = new Thickness(0, 14, 0, 0);

        var hours = new UniformGrid6();
        foreach (var h in w.Hours.Take(6))
        {
            var ic = new WeatherIcon { Width = 22, Height = 22, Margin = new Thickness(0, 4, 0, 4) };
            ic.Set(WeatherService.Kind(h.Code), !h.IsDay);
            hours.Children.Add(V(Text(h.Time.ToString(Settings.Current.Use24Hour ? "HH" : "h tt"), 10.5, FontWeights.SemiBold, "Text.Secondary").Center(), ic, Text($"{h.Temp:0}°", 13, FontWeights.Bold).Center()));
        }
        var days = new StackPanel();
        foreach (var d in w.Days.Take(7))
        {
            var ic = new WeatherIcon { Width = 20, Height = 20 };
            ic.Set(WeatherService.Kind(d.Code), false);
            days.Children.Add(Columns("70,30,*,auto",
                Text(d.Date.Date == DateTime.Today ? "Today" : d.Date.ToString("ddd"), 13, FontWeights.SemiBold),
                ic,
                Text(d.PrecipChance >= 20 ? $"{d.PrecipChance:0}%" : "", 11, null, "Accent"),
                Text($"{d.Max:0}° / {d.Min:0}°", 13, FontWeights.Bold)).Margin(0, 0, 0, 8));
        }
        var sun = Text($"Sunrise {w.Sunrise:t}  ·  Sunset {w.Sunset:t}", 11.5, null, "Text.Secondary").Margin(0, 4, 0, 0);
        return V(Card.Header(Settings.Current.Weather.City ?? "Weather"), head, details, Divider(12), hours, Divider(12), days, sun);
    }
}

/// <summary>Where the sun is between sunrise and sunset.</summary>
public sealed class SunWidget : WidgetBase
{
    SunArc _arc = null!;
    TextBlock _rise = null!, _set = null!;
    protected override int TickSeconds => 60;

    protected override UIElement BuildContent()
    {
        _arc = new SunArc { Width = 54, Height = 24 };
        _rise = Text("", 8, FontWeights.SemiBold, "Text.Secondary");
        _set = Text("", 8, FontWeights.SemiBold, "Text.Secondary");
        return H(_arc, V(_rise, _set).Margin(10, 0, 2, 0));
    }

    protected override void OnAttach() { Svc.Weather.Changed += Update; }
    protected override void OnDetach() => Svc.Weather.Changed -= Update;
    protected override void OnTick(DateTime now) => Update();

    void Update()
    {
        var w = Svc.Weather;
        if (!w.HasData) { _rise.Text = "Set city"; _set.Text = "in Settings"; return; }
        _rise.Text = $"↑ {w.Sunrise:t}";
        _set.Text = $"↓ {w.Sunset:t}";
        var now = DateTime.Now;
        _arc.Progress = Math.Clamp((now - w.Sunrise).TotalMinutes / (w.Sunset - w.Sunrise).TotalMinutes, -0.05, 1.05);
        _arc.InvalidateVisual();
    }

    sealed class SunArc : FrameworkElement
    {
        public double Progress;
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            var track = new Pen(UI.UI.Res("Fill.Track"), 1.5) { DashStyle = DashStyles.Dash };
            var arc = new StreamGeometry();
            using (var c = arc.Open()) { c.BeginFigure(new Point(2, h - 2), false, false); c.ArcTo(new Point(w - 2, h - 2), new Size(w / 2 - 2, h - 4), 0, false, SweepDirection.Clockwise, true, false); }
            dc.DrawGeometry(null, track, arc);
            dc.DrawLine(new Pen(UI.UI.Res("Divider"), 1), new Point(0, h - 1), new Point(w, h - 1));
            double p = Math.Clamp(Progress, 0, 1), a = Math.PI * (1 - p);
            var sun = new Point(w / 2 + Math.Cos(a) * (w / 2 - 2), h - 2 - Math.Sin(a) * (h - 4));
            dc.DrawEllipse(Progress is >= 0 and <= 1 ? Brush("#FFC93D") : UI.UI.Res("Text.Tertiary"), null, sun, 4.5, 4.5);
        }
    }
}

/// <summary>Six equal columns (hourly forecast).</summary>
sealed class UniformGrid6 : System.Windows.Controls.Primitives.UniformGrid
{
    public UniformGrid6() { Columns = 6; }
}
