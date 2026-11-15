using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Vela.Core;
using Vela.Views;
using static Vela.UI.UI;

namespace Vela.Widgets;

/// <summary>
/// A smart stack: several widgets share one slot. Scroll over it (or click the dots) to flip between them;
/// optionally rotates on its own. Each child is a normal widget instance with its own config, stored as JSON
/// in the stack's config ("items"), so stacking/unstacking never loses settings.
/// Create from any widget's menu → "Stack with next widget"; undo with "Unstack".
/// </summary>
public sealed class StackWidget : WidgetBase
{
    readonly List<WidgetBase> _kids = new();
    readonly Grid _host = new() { ClipToBounds = true };
    readonly StackPanel _dots = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
    readonly TranslateTransform _slide = new();
    int _index, _rotateCounter;

    protected override bool HasTile => false;
    protected override bool Clickable => false;
    protected override int TickSeconds => 1;

    public List<WidgetInstance> Items
    {
        get
        {
            try { return JsonSerializer.Deserialize<List<WidgetInstance>>(Cfg("items", "[]"), Json.Options) ?? new(); }
            catch { return new(); }
        }
    }

    void SaveItems() => SetCfg("items", JsonSerializer.Serialize(_kids.Select(k => k.Instance).ToList(), Json.Options));

    int RotateSeconds => int.TryParse(Cfg("rotate", "0"), out var s) ? s : 0;

    protected override UIElement BuildContent()
    {
        _host.RenderTransform = _slide;
        foreach (var inst in Items) AddKid(inst);
        _index = Math.Clamp(int.TryParse(Cfg("index", "0"), out var i) ? i : 0, 0, Math.Max(0, _kids.Count - 1));
        Show(_index, 0);
        MouseWheel += (_, e) =>
        {
            if (e.Handled || _kids.Count < 2) return;
            e.Handled = true;
            Show((_index + (e.Delta < 0 ? 1 : -1) + _kids.Count) % _kids.Count, e.Delta < 0 ? 1 : -1);
        };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(_host);
        Grid.SetColumn(_dots, 1);
        g.Children.Add(_dots);
        return g;
    }

    void AddKid(WidgetInstance inst)
    {
        var w = WidgetRegistry.Create(inst, IsPreview);
        if (w == null) return;
        w.InStack = true;
        w.Visibility = Visibility.Collapsed;
        w.RemoveRequested += RemoveKid;
        w.ConfigChanged = SaveItems;
        w.ExtraMenu = m =>
        {
            if (_kids.Count > 1) m.Items.Add(Item("Next in stack", () => Show((_index + 1) % _kids.Count, 1)));
            var rot = new MenuItem { Header = "Rotate automatically" };
            foreach (var (label, secs) in new[] { ("Off", 0), ("Every 10 seconds", 10), ("Every 30 seconds", 30), ("Every minute", 60) })
                rot.Items.Add(Item(label, () => SetCfg("rotate", secs.ToString()), RotateSeconds == secs));
            m.Items.Add(rot);
            m.Items.Add(Item("Unstack", () => DockWindow.For(this)?.Unstack(this)));
        };
        _kids.Add(w);
        _host.Children.Add(w);
    }

    void RemoveKid(WidgetBase w)
    {
        _kids.Remove(w);
        _host.Children.Remove(w);
        w.Dispose();
        SaveItems();
        if (_kids.Count <= 1) { DockWindow.For(this)?.Unstack(this); return; }
        Show(Math.Min(_index, _kids.Count - 1), 0);
    }

    void Show(int index, int direction)
    {
        if (_kids.Count == 0) return;
        _index = index;
        for (int i = 0; i < _kids.Count; i++) _kids[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        if (!IsPreview && Cfg("index", "0") != index.ToString()) SetCfg("index", index.ToString());
        RenderDots();
        if (direction != 0)
        {
            var a = new DoubleAnimation(direction * 14, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            _slide.BeginAnimation(TranslateTransform.YProperty, a);
            _host.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(180)));
        }
    }

    void RenderDots()
    {
        _dots.Children.Clear();
        _dots.Visibility = _kids.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _kids.Count; i++)
        {
            int idx = i;
            var d = new Border { Width = 4, Height = i == _index ? 9 : 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 1.5, 0, 1.5), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            d.Bg(i == _index ? "Text.Primary" : "Text.Tertiary");
            d.MouseLeftButtonUp += (_, e) => { e.Handled = true; Show(idx, idx > _index ? 1 : -1); };
            _dots.Children.Add(d);
        }
    }

    protected override void OnTick(DateTime now)
    {
        int every = RotateSeconds;
        if (every <= 0 || _kids.Count < 2 || IsMouseOver || FlyoutWindow.Current != null) { _rotateCounter = 0; return; }
        if (++_rotateCounter < every) return;
        _rotateCounter = 0;
        Show((_index + 1) % _kids.Count, 1);
    }

    /// <summary>The child instances, for unstacking.</summary>
    public List<WidgetInstance> Children_ => _kids.Select(k => k.Instance).ToList();

    /// <summary>Add a widget instance to this stack (used when stacking onto an existing stack).</summary>
    public void Append(WidgetInstance inst)
    {
        AddKid(inst);
        SaveItems();
        Show(_kids.Count - 1, 1);
    }

    public override void Dispose()
    {
        foreach (var k in _kids) k.Dispose();
        base.Dispose();
    }
}
