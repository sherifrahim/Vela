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
    WeatherIcon _icon = null!;
    StackPanel _weather = null!;

    protected override int TickSeconds => 1;

    protected override UIElement BuildContent()
    {
        _day = Text("", 9.5, FontWeights.Bold, "Sys.Red");
        _date = Text("", 15, FontWeights.Bold);
        _time = Text("", 17, FontWeights.Bold);
        _ampm = Text("", 9, FontWeights.Bold).Margin(2, 0, 0, 2);
        _ampm.VerticalAlignment = VerticalAlignment.Bottom;
        _icon = new WeatherIcon { Width = 12, Height = 12, Margin = new Thickness(0, 0, 3, 0) };
        _temp = Text("", 10.5, FontWeights.SemiBold, "Text.Secondary");
        _weather = H(_icon, _temp).Right();
        _weather.Visibility = Visibility.Collapsed;

        var left = V(_day, _date).Margin(2, 0, 22, 0);
        var right = V(H(_time, _ampm).Right(), _weather);
        return H(left, right);
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
        _time = Text("", 24, FontWeights.Bold);
        _sec = Text("", 12, FontWeights.SemiBold, "Text.Secondary").Margin(3, 0, 0, 4);
        _sec.VerticalAlignment = VerticalAlignment.Bottom;
        _date = Text("", 10, FontWeights.SemiBold, "Text.Secondary");
        return V(H(_time, _sec), _date).Margin(4, 0);
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
        _wd = Text("", 9.5, FontWeights.Bold, "Sys.Red").Center();
        _n = Text("", 20, FontWeights.Bold).Center();
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
        _ring = new RingGauge(_span == Span.Day ? "Sys.Orange" : "Sys.Purple") { Width = 34, Height = 34, Thickness = 4 };
        _pct = Text("", 10, FontWeights.Bold).Center();
        var g = new Grid { Width = 34, Height = 34 };
        g.Children.Add(_ring); g.Children.Add(_pct);
        _label = Text("", 11, FontWeights.SemiBold, "Text.Secondary").Margin(8, 0, 2, 0);
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
        var grid = new UniformGrid { Rows = Rows, Columns = Cols, Width = Cols * 7.5, Height = Rows * 7 };
        for (int i = 0; i < _dots.Length; i++)
        {
            _dots[i] = new Ellipse { Width = 5.5, Height = 5.5 };
            grid.Children.Add(_dots[i]);
        }
        _left = Text("", 9, FontWeights.SemiBold, "Text.Secondary");
        _right = Text("", 9, FontWeights.Bold);
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
        var today = Text(DateTime.Today.ToString("dddd, MMMM d, yyyy"), 12, FontWeights.SemiBold, "Text.Secondary").Margin(0, 12, 0, 0);
        Children.Add(today);
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
