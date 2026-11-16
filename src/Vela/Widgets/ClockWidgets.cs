using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using Vela.Core;
using Vela.Services;
using Vela.UI;
using Vela.Views;
using static Vela.UI.UI;

namespace Vela.Widgets;

/// <summary>Weekday (red) + date on the left, time + mini weather on the right. Click: month calendar.</summary>
public sealed class ClockWidget : WidgetBase
{
    TextBlock _day = null!, _date = null!, _time = null!, _ampm = null!, _temp = null!;
    Border _divider = null!;
    WeatherIcon _icon = null!;
    StackPanel _weather = null!;

    protected override int TickSeconds => 1;

    protected override UIElement BuildContent()
    {
        _day = Text("", 6.75, FontWeights.Bold, "Sys.Red");
        _date = Text("", 11.75, FontWeights.SemiBold);
        _time = Text("", 11.75, FontWeights.SemiBold);
        _ampm = Text("", 6.75, FontWeights.SemiBold).Margin(1.5, 0, 0, 1.5);
        _ampm.VerticalAlignment = VerticalAlignment.Bottom;
        _icon = new WeatherIcon { Width = 9, Height = 9, Margin = new Thickness(0, 0, 3, 0) };
        _temp = Text("", 7, FontWeights.Medium, "Text.Secondary");
        _weather = H(_icon, _temp).Right();
        _weather.Margin = new Thickness(0, 1, 0, 0);
        _weather.Visibility = Visibility.Collapsed;

        var left = V(_day, _date.Margin(0, -1, 0, 0));
        left.MinWidth = 44;
        left.Margin = new Thickness(2, 0, 0, 0);
        _divider = new Border { Width = 1, Height = 24, Margin = new Thickness(14, 0, 14, 0) }.Bg("Divider");
        var divider = _divider;
        var right = V(H(_time, _ampm).Right(), _weather);
        right.MinWidth = 44;
        return H(left, divider, right);
    }

    protected override void OnAttach()
    {
        if (Svc.Weather.Configured) Svc.Weather.Changed += UpdateWeather;
        UpdateWeather();
    }

    protected override void OnDetach() => Svc.Weather.Changed -= UpdateWeather;

    void UpdateWeather()
    {
        var w = Svc.Weather;
        _weather.Visibility = w.HasData ? Visibility.Visible : Visibility.Collapsed;
        if (!w.HasData) return;
        _icon.Set(WeatherService.Kind(w.Code), !w.IsDay);
        _temp.Text = $"{w.Temp:0}°";
    }

    protected override void OnTick(DateTime now)
    {
        _day.Text = now.ToString("dddd", CultureInfo.CurrentCulture).ToUpperInvariant();
        _date.Text = now.ToString("MMM d", CultureInfo.CurrentCulture);
        if (Settings.Current.Use24Hour) { _time.Text = now.ToString("HH:mm"); _ampm.Text = ""; }
        else { _time.Text = now.ToString("h:mm"); _ampm.Text = now.ToString("tt", CultureInfo.InvariantCulture); }
    }

    /// <summary>Date half → calendar card; time half → the Windows Clock app (alarms, timers, world clock).</summary>
    protected override void OnClick()
    {
        if (System.Windows.Input.Mouse.GetPosition(_divider).X > 0) Shell.WindowTracker.OpenUri("ms-clock:");
        else OpenCard();
    }

    protected override UIElement BuildCard() => new MonthCalendar();
    protected override double CardWidth => 320;

    protected override void AddMenuItems(ContextMenu menu)
    {
        menu.Items.Add(Item("24-hour clock", () => { Settings.Current.Use24Hour = !Settings.Current.Use24Hour; Settings.Save("clock"); OnTick(DateTime.Now); }, Settings.Current.Use24Hour));
        menu.Items.Add(Item("Date & time settings", () => Shell.WinActions.OpenSettings("dateandtime")));
    }
}

/// <summary>Large time with seconds.</summary>
public sealed class BigClockWidget : WidgetBase
{
    TextBlock _time = null!, _sec = null!, _date = null!;
    protected override int TickSeconds => 1;

    protected override UIElement BuildContent()
    {
        _time = Text("", 19, FontWeights.Bold);
        _sec = Text("", 9.5, FontWeights.SemiBold, "Text.Secondary").Margin(2, 0, 0, 3);
        _sec.VerticalAlignment = VerticalAlignment.Bottom;
        _date = Text("", 8, FontWeights.SemiBold, "Text.Secondary");
        return V(H(_time, _sec), _date.Margin(0, -2, 0, 0)).Margin(2, 0);
    }

    protected override void OnTick(DateTime now)
    {
        _time.Text = now.ToString(Settings.Current.Use24Hour ? "HH:mm" : "h:mm");
        _sec.Text = now.ToString("ss");
        _date.Text = now.ToString("ddd, MMM d");
    }

    protected override UIElement BuildCard() => new MonthCalendar();
    protected override double CardWidth => 320;
}

/// <summary>Today as a calendar page: weekday in red over the day number.</summary>
public sealed class DayNumberWidget : WidgetBase
{
    TextBlock _wd = null!, _n = null!;
    protected override int TickSeconds => 30;

    protected override UIElement BuildContent()
    {
        _wd = Text("", 7, FontWeights.Bold, "Sys.Red").Center();
        _n = Text("", 16, FontWeights.Bold).Center();
        _n.Margin = new Thickness(0, -3, 0, 0);
        return V(_wd, _n).Margin(4, 0);
    }

    protected override void OnTick(DateTime now)
    {
        _wd.Text = now.ToString("ddd").ToUpperInvariant();
        _n.Text = now.Day.ToString();
    }

    protected override UIElement BuildCard() => new MonthCalendar();
    protected override double CardWidth => 320;
}

/// <summary>How much of today / this month has passed, as a ring.</summary>
public sealed class ProgressRingWidget : WidgetBase
{
    public enum Span { Day, Month }
    readonly Span _span;
    RingGauge _ring = null!;
    TextBlock _pct = null!, _label = null!;

    public ProgressRingWidget(Span span) => _span = span;
    protected override int TickSeconds => 30;
    protected override bool Clickable => false;

    protected override UIElement BuildContent()
    {
        _ring = new RingGauge(_span == Span.Day ? "Sys.Orange" : "Sys.Purple") { Width = 30, Height = 30, Thickness = 3 };
        _pct = Text("", 8.5, FontWeights.Bold).Center();
        var g = new Grid { Width = 30, Height = 30 };
        g.Children.Add(_ring); g.Children.Add(_pct);
        _label = Text("", 9, FontWeights.SemiBold);
        return H(g, V(Caption(_span == Span.Day ? "Day" : "Month"), _label).Margin(8, 0, 2, 0));
    }

    protected override void OnTick(DateTime now)
    {
        double v = _span == Span.Day
            ? now.TimeOfDay.TotalSeconds / 86400
            : (now.Day - 1 + now.TimeOfDay.TotalHours / 24) / DateTime.DaysInMonth(now.Year, now.Month);
        _ring.Value = v;
        _pct.Text = $"{v * 100:0}";
        _label.Text = _span == Span.Day ? $"{(DateTime.Today.AddDays(1) - now).TotalHours:0}h left" : now.ToString("MMMM");
        _label.Margin = new Thickness(0);
    }
}

/// <summary>The year as a grid of dots that fills up week by week.</summary>
public sealed class YearDotsWidget : WidgetBase
{
    const int Cols = 13, Rows = 4;
    readonly Ellipse[] _dots = new Ellipse[Cols * Rows];
    TextBlock _left = null!, _right = null!;
    protected override int TickSeconds => 60;
    protected override bool Clickable => false;

    protected override UIElement BuildContent()
    {
        var grid = new UniformGrid { Rows = Rows, Columns = Cols, Width = Cols * 6.6, Height = Rows * 6 };
        for (int i = 0; i < _dots.Length; i++)
        {
            _dots[i] = new Ellipse { Width = 4.6, Height = 4.6 };
            grid.Children.Add(_dots[i]);
        }
        _left = Text("", 7, FontWeights.SemiBold, "Text.Secondary");
        _right = Text("", 7.5, FontWeights.Bold);
        var foot = Columns("*,auto", _left, _right);
        foot.Margin = new Thickness(0, 2, 0, 0);
        return V(grid, foot);
    }

    protected override void OnTick(DateTime now)
    {
        int days = DateTime.IsLeapYear(now.Year) ? 366 : 365;
        double v = (now.DayOfYear - 1 + now.TimeOfDay.TotalHours / 24) / days;
        int filled = (int)Math.Round(v * _dots.Length);
        for (int i = 0; i < _dots.Length; i++) _dots[i].Fg(i < filled ? "Text.Primary" : "Fill.Track");
        _left.Text = now.ToString("MMM d");
        _right.Text = $"{v * 100:0}%";
    }
}

/// <summary>Month grid used by the clock cards.</summary>
public sealed class MonthCalendar : StackPanel
{
    DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    readonly TextBlock _title = Text("", 16, FontWeights.Bold);
    readonly UniformGrid _grid = new() { Columns = 7 };

    public MonthCalendar()
    {
        var header = Columns("*,auto,auto", _title,
            CircleButton(Glyphs.ChevronLeft, () => { _month = _month.AddMonths(-1); Render(); }, 28, "Fill.Control", "Text.Primary", 10).Margin(0, 0, 6, 0),
            CircleButton(Glyphs.ChevronRight, () => { _month = _month.AddMonths(1); Render(); }, 28, "Fill.Control", "Text.Primary", 10));
        header.Margin = new Thickness(0, 0, 0, 12);
        Children.Add(header);
        var dow = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
            dow.Children.Add(Text(CultureInfo.CurrentCulture.DateTimeFormat.ShortestDayNames[((int)first + i) % 7], 11, FontWeights.SemiBold, "Text.Tertiary").Center());
        Children.Add(dow);
        Children.Add(_grid);
        var today = Text(DateTime.Today.ToString("dddd, MMMM d, yyyy"), 12, FontWeights.SemiBold, "Text.Secondary");
        var open = PillButton("Open Calendar", () => { FlyoutWindow.CloseCurrent(); Shell.WindowTracker.OpenUri("outlookcal:"); });
        Children.Add(Columns("*,auto", today, open).Margin(0, 12, 0, 0));
        Render();
    }

    void Render()
    {
        _title.Text = _month.ToString("MMMM yyyy");
        _grid.Children.Clear();
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        int offset = ((int)_month.DayOfWeek - (int)first + 7) % 7;
        int days = DateTime.DaysInMonth(_month.Year, _month.Month);
        for (int i = 0; i < offset; i++) _grid.Children.Add(new Border());
        for (int d = 1; d <= days; d++)
        {
            var date = new DateTime(_month.Year, _month.Month, d);
            bool isToday = date == DateTime.Today;
            var t = Text(d.ToString(), 13, isToday ? FontWeights.Bold : FontWeights.Normal, isToday ? "OnAccent" : "Text.Primary").Center();
            var cell = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Child = t, Margin = new Thickness(1) };
            if (isToday) cell.Bg("Sys.Red");
            _grid.Children.Add(cell);
        }
    }
}

/// <summary>
/// Calendar page + today's line, like the reference "SUN 11 · No upcoming events" tile.
/// Click: month card (with "Open Calendar"). Reading Windows calendar events needs package identity,
/// so the tile shows the day, week number and how much of the working day is left instead.
/// </summary>
public sealed class CalendarWidget : WidgetBase
{
    TextBlock _wd = null!, _n = null!, _title = null!, _sub = null!;
    protected override int TickSeconds => 30;

    protected override UIElement BuildContent()
    {
        _wd = Text("", 6.5, FontWeights.Bold, "Sys.Red").Center();
        _n = Text("", 13.5, FontWeights.Bold).Center();
        _n.Margin = new Thickness(0, -2, 0, 0);
        var page = new Border { Width = 30, Height = 32, CornerRadius = new CornerRadius(7), Child = V(_wd, _n) }.Bg("Fill.Control");
        _title = Text("", 10, FontWeights.Bold);
        _sub = Text("", 8, null, "Text.Secondary");
        return H(page, V(_title, _sub).Margin(9, 0, 4, 0));
    }

    protected override void OnTick(DateTime now)
    {
        _wd.Text = now.ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant();
        _n.Text = now.Day.ToString();
        int week = ISOWeek.GetWeekOfYear(now);
        _title.Text = now.ToString("dddd", CultureInfo.CurrentCulture);
        var end = now.Date.AddHours(18);
        _sub.Text = now < end && now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
            ? $"Week {week} · {Math.Ceiling((end - now).TotalHours):0}h of workday left"
            : $"Week {week} · click to plan your day";
    }

    protected override UIElement BuildCard() => new MonthCalendar();
    protected override double CardWidth => 320;
}
