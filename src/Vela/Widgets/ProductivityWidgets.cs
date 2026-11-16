using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vela.Core;
using Vela.Services;
using Vela.UI;
using Vela.Views;
using static Vela.UI.UI;

namespace Vela.Widgets;

/// <summary>Time in front of the PC today, with an hourly bar chart. Card: hours + top apps.</summary>
public sealed class ScreenTimeWidget : WidgetBase
{
    TextBlock _value = null!;
    Sparkline _bars = null!;
    protected override int TickSeconds => 10;

    protected override UIElement BuildContent()
    {
        var watch = new WatchIcon { Width = 14, Height = 24, Margin = new Thickness(3, 0, 0, 0) };
        _value = Value("0m");
        _bars = new Sparkline("Accent") { Bars = true, Baseline = true, Width = 52, Height = 18, Margin = new Thickness(16, 7, 1, 0) };
        return H(watch, V(Caption("Screen Time"), _value.Margin(0, -2, 0, 0)).Margin(11, 0, 0, 0), _bars);
    }

    protected override void OnTick(DateTime now)
    {
        var d = Svc.ScreenTime.Today;
        _value.Text = Fmt.Duration(TimeSpan.FromSeconds(d.Total));
        _bars.SetValues(d.Hours.Select(h => (double)h));
    }

    protected override double CardWidth => 360;

    protected override UIElement BuildCard()
    {
        var st = Svc.ScreenTime;
        var d = st.Today;
        var chart = new Sparkline("Accent") { Bars = true, Height = 90, Margin = new Thickness(0, 4, 0, 4) };
        chart.SetValues(d.Hours.Select(h => (double)h));
        var axis = Columns("*,*,*,*", Text("12a", 10, null, "Text.Tertiary"), Text("6a", 10, null, "Text.Tertiary"),
            Text("12p", 10, null, "Text.Tertiary"), Text("6p", 10, null, "Text.Tertiary"));
        var list = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var top = d.Apps.OrderByDescending(kv => kv.Value).Take(7).ToList();
        int max = top.Count > 0 ? top[0].Value : 1;
        foreach (var (app, secs) in top)
        {
            var bar = Progress(out var set, "Accent", 5);
            bar.Loaded += (_, _) => set((double)secs / max);
            var row = Columns("*,auto", Text(st.DisplayName(app), 13, FontWeights.SemiBold), Text(Fmt.Duration(TimeSpan.FromSeconds(secs)), 12, null, "Text.Secondary"));
            list.Children.Add(V(row, bar.Margin(0, 5, 0, 0)).Margin(0, 0, 0, 10));
        }
        if (top.Count == 0) list.Children.Add(Text("Nothing tracked yet today.", 12.5, null, "Text.Secondary"));
        var total = Value(Fmt.Duration(TimeSpan.FromSeconds(d.Total)), 30);
        var note = Text("Kept on this PC only.", 11, null, "Text.Tertiary").Margin(0, 6, 0, 0);
        return V(Card.Header("Screen Time"), Label("Today"), total, chart, axis, list, note);
    }

    protected override void AddMenuItems(ContextMenu menu) =>
        menu.Items.Add(Item("Track screen time", () => { Settings.Current.TrackScreenTime = !Settings.Current.TrackScreenTime; Settings.Save(); }, Settings.Current.TrackScreenTime));
}

/// <summary>Shared look for the coloured-outline timer tiles (Pomodoro red, Timer blue, Stopwatch orange).</summary>
public abstract class TimerTileBase : WidgetBase
{
    protected TextBlock Label = null!, Sub = null!, Time = null!;
    protected Button PlayButton = null!;
    protected abstract string ColorKey { get; }
    protected abstract string Title { get; }
    protected virtual UIElement? ExtraButton => null;

    protected override UIElement BuildContent()
    {
        Tile.BorderThickness = new Thickness(1.6);
        Tile.SetResourceReference(Border.BorderBrushProperty, ColorKey);
        Label = Text(Title, 8.5, FontWeights.Bold, ColorKey);
        Sub = Text("", 8.5, FontWeights.SemiBold, "Text.Secondary").Margin(4, 0, 0, 0);
        Time = Text("0:00", 17, FontWeights.Bold);
        Time.Margin = new Thickness(0, -2, 0, 0);
        PlayButton = CircleButton(Glyphs.Play, TogglePlay, 26, ColorKey, "OnAccent", 9.5);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (ExtraButton is { } extra) { buttons.Children.Add(extra); ((FrameworkElement)extra).Margin = new Thickness(0, 0, 5, 0); }
        buttons.Children.Add(PlayButton);
        return H(V(H(Label, Sub), Time).Margin(2, 0, 0, 0), buttons);
    }

    protected abstract void TogglePlay();

    /// <summary>Tell Vela Island about a running countdown (endsAt) or stopwatch (since); null + null removes it.</summary>
    protected void PublishIsland(DateTime? endsAt, DateTime? since)
    {
        if (IsPreview) return;
        Island.SetTimer(endsAt == null && since == null ? null : new TimerActivity(Instance.Id, Label.Text, ColorKey, endsAt, since, TogglePlay, OpenCardFromIsland), Instance.Id);
    }

    void OpenCardFromIsland() { if (BuildCard() != null) OpenCard(); }

    protected override void OnDetach() { base.OnDetach(); if (!IsPreview) Island.SetTimer(null, Instance.Id); }

    protected void SetPlaying(bool playing) =>
        ((TextBlock)PlayButton.Content).Text = playing ? Glyphs.Pause : Glyphs.Play;

    protected static void Chime()
    {
        try { SystemSounds.Asterisk.Play(); } catch { }
    }
}

public sealed class PomodoroWidget : TimerTileBase
{
    enum Phase { Focus, Break, LongBreak }
    Phase _phase = Phase.Focus;
    int _round = 1;
    TimeSpan _left;
    DateTime? _endsAt;

    protected override string ColorKey => "Sys.Red";
    protected override string Title => "Focus";
    protected override int TickSeconds => 1;
    protected override UIElement? ExtraButton => CircleButton(Glyphs.Next, Skip, 26, "Fill.Control", "Text.Primary", 9).Tip("Skip");

    int FocusMin => int.TryParse(Cfg("focus", "25"), out var v) ? v : 25;
    int BreakMin => int.TryParse(Cfg("break", "5"), out var v) ? v : 5;
    int LongMin => int.TryParse(Cfg("long", "15"), out var v) ? v : 15;

    protected override void OnAttach() { if (_left == default) _left = Length(_phase); Render(); }

    TimeSpan Length(Phase p) => TimeSpan.FromMinutes(p switch { Phase.Focus => FocusMin, Phase.Break => BreakMin, _ => LongMin });

    protected override void TogglePlay()
    {
        if (_endsAt != null) { _left = _endsAt.Value - DateTime.Now; _endsAt = null; }
        else _endsAt = DateTime.Now + _left;
        Render();
    }

    void Skip()
    {
        _endsAt = null;
        if (_phase == Phase.Focus) { _phase = _round % 4 == 0 ? Phase.LongBreak : Phase.Break; }
        else { _phase = Phase.Focus; _round = _round % 4 + 1; }
        _left = Length(_phase);
        Render();
    }

    protected override void OnTick(DateTime now)
    {
        if (_endsAt is { } end && now >= end) { Chime(); Skip(); }
        Render();
    }

    void Render()
    {
        var left = _endsAt is { } end ? end - DateTime.Now : _left;
        Time.Text = Fmt.Countdown(left);
        Label.Text = _phase switch { Phase.Focus => "Focus", Phase.Break => "Break", _ => "Long break" };
        Sub.Text = $"{_round}/4";
        SetPlaying(_endsAt != null);
        PublishIsland(_endsAt, null);
    }

    protected override void AddMenuItems(ContextMenu menu)
    {
        foreach (var (f, b) in new[] { (25, 5), (50, 10), (15, 3) })
            menu.Items.Add(Item($"{f} min focus / {b} min break", () => { SetCfg("focus", f.ToString()); SetCfg("break", b.ToString()); _endsAt = null; _phase = Phase.Focus; _left = Length(_phase); Render(); }, FocusMin == f));
        menu.Items.Add(Item("Reset", () => { _endsAt = null; _phase = Phase.Focus; _round = 1; _left = Length(_phase); Render(); }));
    }
}

public sealed class TimerWidget : TimerTileBase
{
    TimeSpan _left;
    DateTime? _endsAt;
    protected override string ColorKey => "Accent";
    protected override string Title => "Timer";
    protected override int TickSeconds => 1;

    TimeSpan Preset => TimeSpan.FromSeconds(int.TryParse(Cfg("seconds", "60"), out var v) ? v : 60);

    protected override void OnAttach()
    {
        if (_left == default) _left = Preset;
        MouseWheel += OnWheel;
        Render();
    }

    protected override void OnDetach() { base.OnDetach(); MouseWheel -= OnWheel; }

    void OnWheel(object s, MouseWheelEventArgs e)
    {
        if (_endsAt != null) return;
        var step = _left.TotalMinutes >= 10 ? 5 : 1;
        _left = TimeSpan.FromMinutes(Math.Clamp(Math.Round(_left.TotalMinutes) + (e.Delta > 0 ? step : -step), 1, 600));
        SetCfg("seconds", ((int)_left.TotalSeconds).ToString());
        Render();
        e.Handled = true;
    }

    protected override void TogglePlay()
    {
        if (_endsAt != null) { _left = _endsAt.Value - DateTime.Now; _endsAt = null; }
        else { if (_left <= TimeSpan.Zero) _left = Preset; _endsAt = DateTime.Now + _left; }
        Render();
    }

    protected override void OnTick(DateTime now)
    {
        if (_endsAt is { } end && now >= end) { _endsAt = null; _left = Preset; Chime(); Sub.Text = "done"; }
        Render();
    }

    void Render()
    {
        Time.Text = Fmt.Countdown(_endsAt is { } end ? end - DateTime.Now : _left);
        SetPlaying(_endsAt != null);
        PublishIsland(_endsAt, null);
    }

    protected override double CardWidth => 300;
    protected override UIElement BuildCard()
    {
        var wrap = new WrapPanel();
        foreach (var m in new[] { 1, 3, 5, 10, 15, 20, 25, 30, 45, 60, 90, 120 })
        {
            var b = PillButton(m < 60 ? $"{m} min" : $"{m / 60}h{(m % 60 > 0 ? $" {m % 60}m" : "")}", () =>
            {
                SetCfg("seconds", (m * 60).ToString());
                _endsAt = null; _left = Preset; Sub.Text = ""; Render();
                FlyoutWindow.CloseCurrent();
            });
            b.Margin = new Thickness(0, 0, 6, 6);
            wrap.Children.Add(b);
        }
        return V(Card.Header("Timer"), Text("Pick a length — or scroll on the tile.", 12, null, "Text.Secondary").Margin(0, 0, 0, 12), wrap);
    }
}

public sealed class StopwatchWidget : TimerTileBase
{
    TimeSpan _acc;
    DateTime? _since;
    readonly List<TimeSpan> _laps = new();
    protected override string ColorKey => "Sys.Orange";
    protected override string Title => "Stopwatch";
    protected override int TickSeconds => 1;

    TimeSpan Elapsed => _acc + (_since is { } s ? DateTime.Now - s : TimeSpan.Zero);

    protected override void TogglePlay()
    {
        if (_since is { } s) { _acc += DateTime.Now - s; _since = null; }
        else _since = DateTime.Now;
        Render();
    }

    protected override void OnAttach() => Render();
    protected override void OnTick(DateTime now) => Render();

    void Render()
    {
        Time.Text = Fmt.Countdown(Elapsed);
        Sub.Text = _laps.Count > 0 ? $"lap {_laps.Count}" : "";
        SetPlaying(_since != null);
        PublishIsland(null, _since is { } s ? s - _acc : null);
    }

    protected override double CardWidth => 300;
    protected override UIElement BuildCard()
    {
        var list = new StackPanel();
        for (int i = _laps.Count - 1; i >= 0; i--)
            list.Children.Add(Columns("*,auto", Text($"Lap {i + 1}", 13, FontWeights.SemiBold), Text(Fmt.Countdown(_laps[i]), 13, null, "Text.Secondary")).Margin(0, 0, 0, 8));
        if (_laps.Count == 0) list.Children.Add(Text("No laps yet.", 12.5, null, "Text.Secondary"));
        var buttons = H(
            PillButton("Lap", () => { _laps.Add(Elapsed); Render(); RefreshCard(); }).Margin(0, 0, 8, 0),
            PillButton("Reset", () => { _acc = TimeSpan.Zero; _since = null; _laps.Clear(); Render(); RefreshCard(); }));
        buttons.Margin = new Thickness(0, 12, 0, 0);
        return V(Card.Header("Stopwatch"), Value(Fmt.Countdown(Elapsed), 30).Margin(0, 0, 0, 12), list, buttons);
    }
}

/// <summary>Open todos with a progress ring; tick them off from the dock. Card: full list + add.</summary>
public sealed class TodosWidget : WidgetBase
{
    RingGauge _ring = null!;
    TextBlock _count = null!;
    StackPanel _list = null!;

    protected override UIElement BuildContent()
    {
        _ring = new RingGauge("Accent") { Width = 28, Height = 28, Thickness = 3 };
        _count = Text("0", 10, FontWeights.Bold).Center();
        var ring = new Grid { Width = 28, Height = 28 };
        ring.Children.Add(_ring); ring.Children.Add(_count);
        _list = new StackPanel { Margin = new Thickness(10, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 64 };
        return H(ring, _list);
    }

    protected override void OnAttach() { Svc.Data.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Data.Changed -= Render;

    void Render()
    {
        var todos = Svc.Data.Data.Todos;
        var open = todos.Where(t => !t.Done).ToList();
        _count.Text = open.Count.ToString();
        _ring.Value = todos.Count == 0 ? 0 : (double)(todos.Count - open.Count) / todos.Count;
        _list.Children.Clear();
        foreach (var t in open.Take(2)) _list.Children.Add(Row(t, 9, 100));
        if (open.Count == 0) _list.Children.Add(Text(todos.Count == 0 ? "Add a todo" : "All done", 9, FontWeights.SemiBold, "Text.Secondary"));
    }

    static FrameworkElement Row(TodoItem t, double size, double maxWidth)
    {
        var circle = new Border { Width = size + 2, Height = size + 2, CornerRadius = new CornerRadius(size), BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand, Margin = new Thickness(0, 1, 7, 1) };
        circle.SetResourceReference(Border.BorderBrushProperty, t.Done ? "Accent" : "Text.Secondary");
        if (t.Done) { circle.Bg("Accent"); circle.Child = Icon(Glyphs.Check, size * 0.6, "OnAccent"); }
        circle.MouseLeftButtonDown += (_, e) => { e.Handled = true; t.Done = !t.Done; Svc.Data.Touch(); };
        var text = Text(t.Text, size, FontWeights.SemiBold, t.Done ? "Text.Tertiary" : "Text.Primary");
        text.MaxWidth = maxWidth;
        if (t.Done) text.TextDecorations = TextDecorations.Strikethrough;
        return H(circle, text);
    }

    protected override double CardWidth => 340;
    protected override UIElement BuildCard()
    {
        var list = new StackPanel();
        void Fill()
        {
            list.Children.Clear();
            foreach (var t in Svc.Data.Data.Todos.OrderBy(t => t.Done).ThenByDescending(t => t.Created))
            {
                var del = CircleButton(Glyphs.Close, () => { Svc.Data.Data.Todos.Remove(t); Svc.Data.Touch(); Fill(); }, 22, "Fill.Control", "Text.Secondary", 8);
                var row = Columns("*,auto", Row(t, 14, 250), del);
                row.Margin = new Thickness(0, 0, 0, 10);
                list.Children.Add(row);
            }
            if (Svc.Data.Data.Todos.Count == 0) list.Children.Add(Text("Nothing to do. Add one below.", 12.5, null, "Text.Secondary").Margin(0, 0, 0, 10));
        }
        Fill();
        Svc.Data.Changed += Fill;
        list.Unloaded += (_, _) => Svc.Data.Changed -= Fill;

        var input = TextBox("", "New todo — press Enter");
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(input.Text)) return;
            Svc.Data.Data.Todos.Add(new TodoItem { Text = input.Text.Trim() });
            input.Clear();
            Svc.Data.Touch();
        };
        input.Loaded += (_, _) => input.Focus();
        var scroll = new ScrollViewer { Content = list, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var clear = PillButton("Clear done", () => { Svc.Data.Data.Todos.RemoveAll(t => t.Done); Svc.Data.Touch(); });
        clear.Margin = new Thickness(0, 10, 0, 0);
        clear.HorizontalAlignment = HorizontalAlignment.Left;
        return V(Card.Header("Todos"), scroll, input, clear);
    }
}

/// <summary>Latest note on a yellow notepad; click to edit.</summary>
public sealed class NotesWidget : WidgetBase
{
    TextBlock _title = null!, _preview = null!;

    protected override UIElement BuildContent()
    {
        _title = Text("", 9.5, FontWeights.Bold);
        _title.MaxWidth = 110;
        _preview = Text("", 8, null, "Text.Secondary");
        _preview.MaxWidth = 110;
        var edit = CircleButton(Glyphs.Edit, () => OpenCard(), 22, "Fill.Control", "Text.Primary", 9).Margin(10, 0, 0, 0);
        return H(NotepadIcon(28), V(_title, _preview).Margin(8, 0, 0, 0), edit);
    }

    /// <summary>Yellow-header notepad drawn in vectors.</summary>
    public static FrameworkElement NotepadIcon(double size)
    {
        var body = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24), Background = Brush("#FFFDFDFB"), ClipToBounds = true };
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(size * 0.32) });
        g.RowDefinitions.Add(new RowDefinition());
        g.Children.Add(new Border { Background = Gradient("#FFE15C", "#FFC928") });
        var lines = new StackPanel { Margin = new Thickness(size * 0.15, size * 0.12, size * 0.15, 0) };
        for (int i = 0; i < 3; i++) lines.Children.Add(new Border { Height = 1, Background = Brush("#22000000"), Margin = new Thickness(0, 0, 0, size * 0.12) });
        Grid.SetRow(lines, 1);
        g.Children.Add(lines);
        body.Child = g;
        return body;
    }

    protected override void OnAttach() { Svc.Data.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Data.Changed -= Render;

    void Render()
    {
        var n = Svc.Data.Data.Notes.OrderByDescending(n => n.Updated).FirstOrDefault();
        _title.Text = n?.Title ?? "Notes";
        _preview.Text = n == null ? "Click to write" : (n.Preview.Length > 0 ? n.Preview : n.Updated.ToString("MMM d, t"));
    }

    protected override double CardWidth => 380;
    protected override UIElement BuildCard()
    {
        var notes = Svc.Data.Data.Notes;
        var current = notes.OrderByDescending(n => n.Updated).FirstOrDefault();
        if (current == null) { current = new NoteItem(); notes.Add(current); }

        var editor = TextBox(current.Text, "Start typing…");
        editor.AcceptsReturn = true;
        editor.TextWrapping = TextWrapping.Wrap;
        editor.Height = 200;
        editor.VerticalContentAlignment = VerticalAlignment.Top;
        editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

        NoteItem cur = current;
        void Tabs()
        {
            tabs.Children.Clear();
            foreach (var n in notes.OrderByDescending(n => n.Updated).Take(8))
            {
                var note = n;
                var seg = Segment(note.Title.Length > 18 ? note.Title[..18] + "…" : note.Title, "notes", note == cur, () =>
                {
                    cur = note;
                    editor.Text = note.Text;
                });
                tabs.Children.Add(seg);
            }
        }
        editor.TextChanged += (_, _) =>
        {
            if (cur.Text == editor.Text) return;
            cur.Text = editor.Text;
            cur.Updated = DateTime.Now;
            Svc.Data.Touch();
        };
        editor.Loaded += (_, _) => { editor.Focus(); editor.CaretIndex = editor.Text.Length; };
        Tabs();

        var actions = H(
            PillButton("New note", () => { cur = new NoteItem(); notes.Add(cur); editor.Text = ""; Tabs(); editor.Focus(); }).Margin(0, 0, 8, 0),
            PillButton("Delete", () =>
            {
                notes.Remove(cur);
                cur = notes.OrderByDescending(n => n.Updated).FirstOrDefault() ?? new NoteItem();
                if (!notes.Contains(cur)) notes.Add(cur);
                editor.Text = cur.Text; Svc.Data.Touch(); Tabs();
            }));
        actions.Margin = new Thickness(0, 12, 0, 0);
        return V(Card.Header("Notes", NotepadIcon(28)), tabs, editor, actions);
    }
}

/// <summary>Live currency conversion (ECB rates via Frankfurter, no key).</summary>
public sealed class CurrencyWidget : WidgetBase
{
    TextBlock _amount = null!, _result = null!, _from = null!, _to = null!;
    decimal _rate;
    DateTime _fetched;
    protected override int TickSeconds => 60;

    string From => Cfg("from", "USD");
    string To => Cfg("to", "EUR");
    decimal Amount => decimal.TryParse(Cfg("amount", "100"), NumberStyles.Number, CultureInfo.InvariantCulture, out var a) ? a : 100;

    protected override UIElement BuildContent()
    {
        _amount = Text("", 9.5, FontWeights.SemiBold, "Text.Secondary");
        _result = Text("…", 14, FontWeights.Bold);
        _from = Text("", 7.5, FontWeights.Bold);
        _to = Text("", 7.5, FontWeights.Bold);
        Border Pill(TextBlock t) => new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 0, 5, 0.5), Child = t, HorizontalAlignment = HorizontalAlignment.Center }.Bg("Fill.Control");
        var swap = Text("⇅", 7.5, FontWeights.Bold, "Text.Secondary").Center();
        return H(V(_amount, _result).Margin(1, 0, 14, 0), V(Pill(_from), swap, Pill(_to)));
    }

    protected override async void OnTick(DateTime now)
    {
        Render();
        if (_rate != 0 && (now - _fetched).TotalMinutes < 60) return;
        try { _rate = await Markets.RateAsync(From, To); _fetched = now; }
        catch (Exception ex) { Log.Warn("FX: " + ex.Message); }
        Render();
    }

    void Render()
    {
        _amount.Text = Amount.ToString("#,0.##");
        _result.Text = _rate == 0 ? "…" : (Amount * _rate).ToString("#,0.00");
        _from.Text = From; _to.Text = To;
    }

    protected override double CardWidth => 300;
    protected override UIElement BuildCard()
    {
        var amount = TextBox(Amount.ToString(CultureInfo.InvariantCulture), "Amount");
        var from = TextBox(From, "From (USD)");
        var to = TextBox(To, "To (EUR)");
        var save = PillButton("Convert", () =>
        {
            SetCfg("amount", amount.Text.Trim().Replace(',', '.'));
            SetCfg("from", from.Text.Trim().ToUpperInvariant());
            SetCfg("to", to.Text.Trim().ToUpperInvariant());
            _rate = 0; OnTick(DateTime.Now);
            FlyoutWindow.CloseCurrent();
        }, accent: true);
        var swap = PillButton("Swap", () => { (from.Text, to.Text) = (to.Text, from.Text); });
        return V(Card.Header("Currency"), amount.Margin(0, 0, 0, 8), Columns("*,8,*", from, new Border(), to.Col(2)).Margin(0, 0, 0, 12),
            H(swap.Margin(0, 0, 8, 0), save), Text("Rates: European Central Bank via frankfurter.dev", 11, null, "Text.Tertiary").Margin(0, 10, 0, 0));
    }
}
