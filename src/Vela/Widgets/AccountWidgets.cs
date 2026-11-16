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

/// <summary>Brand-style badge for an account: its colour and a short mark (dark text on light colours).</summary>
static class AccountBadge
{
    public static FrameworkElement Make(MetricConnector c, double size)
    {
        var bg = (SolidColorBrush)Brush(c.BadgeColor);
        var col = bg.Color;
        bool light = 0.299 * col.R + 0.587 * col.G + 0.114 * col.B > 165;
        var t = Text(c.BadgeText, size * (c.BadgeText.Length > 1 ? 0.36 : 0.46), FontWeights.Bold).Center();
        t.Foreground = light ? Brushes.Black : Brushes.White;
        return Badge(t, bg, size, size * 0.27);
    }
}

/// <summary>
/// Any <see cref="MetricConnector"/> as a tile: badge, headline value, subtitle (+ a 14-day bar chart for the
/// "Chart" variants). Not connected: a greyed sample and "click to connect"; the card holds the connect form.
/// </summary>
public sealed class MetricWidget : WidgetBase
{
    readonly string _id;
    readonly bool _chart;
    TextBlock _value = null!, _sub = null!;
    Sparkline? _spark;
    MetricConnector C => Svc.Account(_id);

    public MetricWidget(string connectorId, bool chart = false) { _id = connectorId; _chart = chart; }

    protected override UIElement BuildContent()
    {
        _value = Text("—", 13, FontWeights.SemiBold);
        _sub = Text("", 7, FontWeights.SemiBold, "Text.Secondary");
        _sub.MaxWidth = 120;
        var row = H(AccountBadge.Make(C, 24), V(_value, _sub.Margin(0, -1, 0, 0)).Margin(9, 0, 3, 0));
        if (!_chart) return row;
        _spark = new Sparkline("Accent") { Width = 58, Height = 24, Bars = true, Margin = new Thickness(8, 0, 2, 0) };
        row.Children.Add(_spark);
        return row;
    }

    protected override void OnAttach() { C.Changed += Render; Render(); }
    protected override void OnDetach() => C.Changed -= Render;

    void Render()
    {
        var c = C;
        bool on = c.IsConnected;
        _value.Text = !on ? c.SampleValue : c.LastUpdated == null && c.Error == null ? "…" : c.Value;
        _value.Opacity = on ? 1 : 0.5;
        _sub.Text = !on ? (c.Auth == ConnectorAuth.LocalSignIn ? "sample · not found on this PC" : "sample · click to connect")
            : c.NeedsSignIn ? "click to sign in" : c.Error ?? (c.Sub.Length > 0 ? c.Sub : c.Unit);
        if (c.NeedsSignIn) { _value.Text = c.SampleValue; _value.Opacity = 0.5; }
        _sub.Fg(on && c.Error != null ? "Sys.Red" : "Text.Secondary");
        if (_spark != null) _spark.SetValues(on && c.Series.Count > 0 ? c.Series : Sample);
        if (_spark != null) _spark.Opacity = on ? 1 : 0.4;
        ToolTip = c.Name;
    }

    static readonly List<double> Sample = new() { 3, 5, 4, 7, 6, 9, 8, 6, 10, 9, 12, 11, 13, 15 };

    protected override double CardWidth => 360;
    protected override UIElement BuildCard()
    {
        var c = C;
        var header = Card.Header(c.Name, AccountBadge.Make(c, 28));
        if (!c.IsConnected)
        {
            if (c.Auth == ConnectorAuth.ApiKey) return V(header, Card.ConnectForm(c, FlyoutWindow.CloseCurrent));
            var local = Text(c.Description, 12.5, null, "Text.Secondary");
            local.TextWrapping = TextWrapping.Wrap;
            return V(header, local);
        }
        if (c.NeedsSignIn)
        {
            // Step 2 (Google): the client is saved; the browser sign-in comes back to a one-shot local listener.
            var note = Text("Your OAuth client is saved. Sign in in the browser; Vela only asks for read-only access.", 12.5, null, "Text.Secondary");
            note.TextWrapping = TextWrapping.Wrap;
            var status = Text("", 12, null, "Sys.Red");
            status.TextWrapping = TextWrapping.Wrap;
            Button? btn = null;
            btn = PillButton(c.SignInLabel, async () =>
            {
                btn!.IsEnabled = false;
                status.Fg("Text.Secondary");
                status.Text = "Waiting for the browser…";
                try { await c.SignInAsync(); FlyoutWindow.CloseCurrent(); }
                catch (Exception ex) { status.Fg("Sys.Red"); status.Text = ex.Message; btn.IsEnabled = true; }
            }, accent: true);
            btn.HorizontalAlignment = HorizontalAlignment.Left;
            return V(header, note, btn.Margin(0, 12, 0, 8), status,
                Card.Footer("Wrong client?", "Disconnect", () => { c.Disconnect(); FlyoutWindow.CloseCurrent(); }));
        }

        var body = new StackPanel();
        body.Children.Add(Label(c.Sub.Length > 0 ? c.Sub : c.Unit));
        body.Children.Add(Value(c.Value, 28).Margin(0, 2, 0, 4));
        if (c.Error != null) body.Children.Add(Text(c.Error, 12, null, "Sys.Red").Margin(0, 0, 0, 6));
        if (c is GitHubActivityConnector gh && gh.Days.Count > 0) body.Children.Add(ContributionGrid(gh.Days));
        else if (c.Series.Count > 0)
        {
            var chart = new Sparkline("Accent") { Height = 64, Bars = true, Margin = new Thickness(0, 8, 0, 4) };
            chart.SetValues(c.Series);
            body.Children.Add(chart);
            body.Children.Add(Text(c.SeriesLabel, 11, null, "Text.Tertiary").Margin(0, 0, 0, 8));
        }
        foreach (var r in c.Rows)
        {
            var label = Text(r.Label, 12.5, FontWeights.SemiBold);
            var value = Text(r.Value, 12.5, FontWeights.SemiBold, "Text.Secondary");
            var row = Columns("*,auto", label, value.Margin(10, 0, 0, 0));
            if (r.Url is { } url)
            {
                var b = GhostButton(row, () => WindowTracker.OpenUri(url));
                b.Padding = new Thickness(6, 5, 6, 5);
                b.Margin = new Thickness(-6, 0, -6, 0);
                body.Children.Add(b);
            }
            else body.Children.Add(row.Margin(0, 5, 0, 5));
        }
        if (c.OpenUrl is { } open)
        {
            var link = GhostButton(Text($"Open {c.Name} ↗", 12, FontWeights.SemiBold, "Accent"), () => WindowTracker.OpenUri(open));
            link.HorizontalAlignment = HorizontalAlignment.Left;
            link.Padding = new Thickness(0, 8, 4, 2);
            body.Children.Add(link);
        }
        if (c.Auth == ConnectorAuth.LocalSignIn) return V(header, body, Card.Footer(c.Status, "Refresh", () => _ = c.RefreshAsync()));
        return V(header, body, Card.Footer(c.Status, "Disconnect", () => { c.Disconnect(); FlyoutWindow.CloseCurrent(); }));
    }

    /// <summary>GitHub's green grid for the last 20 weeks.</summary>
    static FrameworkElement ContributionGrid(List<(DateTime Day, int Count)> days)
    {
        var recent = days.Skip(Math.Max(0, days.Count - 20 * 7)).ToList();
        int max = Math.Max(1, recent.Max(d => d.Count));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Rows = 7, Columns = (recent.Count + 6) / 7, Margin = new Thickness(0, 8, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
        // Column-major (a week per column), like GitHub.
        var cells = new Border[grid.Rows * grid.Columns];
        for (int i = 0; i < cells.Length; i++) cells[i] = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(1.5) };
        for (int i = 0; i < recent.Count; i++)
        {
            int col = i / 7, row = (int)recent[i].Day.DayOfWeek;
            var cell = cells[row * grid.Columns + Math.Min(col, grid.Columns - 1)];
            double t = recent[i].Count / (double)max;
            cell.Background = recent[i].Count == 0 ? null : new SolidColorBrush(Color.FromArgb((byte)(70 + 185 * t), 0x39, 0xD3, 0x53));
            if (recent[i].Count == 0) cell.Bg("Fill.Track");
            cell.ToolTip = $"{recent[i].Day:ddd, MMM d}: {recent[i].Count}";
        }
        foreach (var c in cells) grid.Children.Add(c);
        return grid;
    }

    protected override void AddMenuItems(ContextMenu menu)
    {
        var c = C;
        menu.Items.Add(Item("Refresh", () => _ = c.RefreshAsync()));
        if (c.OpenUrl is { } url) menu.Items.Add(Item($"Open {c.Name}", () => WindowTracker.OpenUri(url)));
        if (c.IsConnected && c.Auth == ConnectorAuth.ApiKey) menu.Items.Add(Item("Logout", c.Disconnect));
    }
}

/// <summary>Every connected payment provider (Stripe, Polar, Lemon Squeezy, Paddle, Gumroad, Shopify, Dodo) added up.</summary>
public sealed class TotalRevenueWidget : WidgetBase
{
    TextBlock _value = null!, _sub = null!;
    Sparkline _spark = null!;
    IEnumerable<RevenueConnector> Providers => Svc.Accounts.OfType<RevenueConnector>();

    protected override UIElement BuildContent()
    {
        _value = Value("—", 15);
        _sub = Text("", 7, FontWeights.SemiBold, "Text.Secondary");
        _spark = new Sparkline("Sys.Green") { Width = 58, Height = 24, Bars = true, Margin = new Thickness(10, 0, 2, 0) };
        return H(V(_value, _sub).Margin(1, 0, 0, 0), _spark);
    }

    protected override void OnAttach()
    {
        Svc.Stripe.Changed += Render;
        foreach (var p in Providers) p.Changed += Render;
        Render();
    }

    protected override void OnDetach()
    {
        Svc.Stripe.Changed -= Render;
        foreach (var p in Providers) p.Changed -= Render;
    }

    (decimal total, string currency, int sources, int skipped, double[] daily) Sum()
    {
        var parts = new List<(decimal net, string cur, IReadOnlyList<double> daily)>();
        if (Svc.Stripe.IsConnected && Svc.Stripe.LastUpdated != null) parts.Add((Svc.Stripe.Net30, Svc.Stripe.Currency, Svc.Stripe.Daily));
        foreach (var p in Providers.Where(p => p.IsConnected && p.LastUpdated != null)) parts.Add((p.Net30, p.Currency, p.Series));
        var cur = parts.GroupBy(p => p.cur.ToLowerInvariant()).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "usd";
        var same = parts.Where(p => p.cur.Equals(cur, StringComparison.OrdinalIgnoreCase)).ToList();
        var daily = new double[14];
        foreach (var p in same) for (int i = 0; i < Math.Min(14, p.daily.Count); i++) daily[i] += p.daily[i];
        return (same.Sum(p => p.net), cur, same.Count, parts.Count - same.Count, daily);
    }

    void Render()
    {
        var (total, cur, sources, skipped, daily) = Sum();
        bool any = sources > 0;
        _value.Text = any ? Fmt.Money(total, cur) : "$6,620";
        _value.Opacity = any ? 1 : 0.5;
        _sub.Text = !any ? "sample · click to connect" : $"total revenue · 30 days · {sources} source{(sources == 1 ? "" : "s")}" + (skipped > 0 ? $" (+{skipped} other currency)" : "");
        _spark.SetValues(any ? daily : new double[] { 2, 4, 3, 6, 5, 7, 6, 8, 7, 9, 8, 10, 12, 11 });
        _spark.Opacity = any ? 1 : 0.4;
    }

    protected override double CardWidth => 360;
    protected override UIElement BuildCard()
    {
        var (total, cur, sources, _, daily) = Sum();
        var rows = new StackPanel();
        void Row(string name, bool connected, string value, Action open)
        {
            var b = GhostButton(Columns("*,auto", Text(name, 12.5, FontWeights.SemiBold), Text(connected ? value : "Connect", 12.5, FontWeights.SemiBold, connected ? "Text.Secondary" : "Accent")), open);
            b.Padding = new Thickness(6, 5, 6, 5);
            b.Margin = new Thickness(-6, 0, -6, 0);
            rows.Children.Add(b);
        }
        Row("Stripe", Svc.Stripe.IsConnected, Svc.Stripe.Display, () => DockWindow.Instance?.OpenSettings());
        foreach (var p in Providers) Row(p.Name, p.IsConnected, p.Value, () => DockWindow.Instance?.OpenSettings());
        var hint = Text("Connect providers in Settings → Connections.", 11.5, null, "Text.Tertiary").Margin(0, 10, 0, 0);
        if (sources == 0)
        {
            var intro = Text("Adds up your revenue from every payment provider you connect, in one tile.", 12.5, null, "Text.Secondary");
            intro.TextWrapping = TextWrapping.Wrap;
            return V(Card.Header("Total Revenue"), intro.Margin(0, 0, 0, 12), rows, hint);
        }
        var chart = new Sparkline("Sys.Green") { Height = 64, Bars = true, Margin = new Thickness(0, 8, 0, 4) };
        chart.SetValues(daily);
        return V(Card.Header("Total Revenue"), Label("All connected providers · 30 days"), Value(Fmt.Money(total, cur), 28).Margin(0, 2, 0, 4),
            chart, Text("Last 14 days", 11, null, "Text.Tertiary").Margin(0, 0, 0, 10), rows, hint);
    }
}
