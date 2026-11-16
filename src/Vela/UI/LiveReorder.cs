using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Vela.UI;

/// <summary>
/// Live drag-and-drop for horizontal rows (dock widgets, app icons).
///  - The dragged item follows the pointer through its own TranslateTransform.
///  - When the order changes, every other item glides from where it was drawn to its new slot
///    ("FLIP": snapshot positions → reorder → layout → animate the difference back to 0).
/// Animations are short (≤ 180 ms) and only run during a drag, so the idle budget is untouched.
/// </summary>
public sealed class LiveReorder
{
    readonly Panel _panel;
    readonly UIElement _item;
    readonly double _grab;           // pointer offset inside the item when the drag started
    static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    public LiveReorder(Panel panel, UIElement item, Point pointerInPanel)
    {
        _panel = panel;
        _item = item;
        _grab = pointerInPanel.X - VisualX(item);
        Panel.SetZIndex(item, 10);
        item.Opacity = 0.92;
    }

    /// <summary>The item's TranslateTransform (created, or merged with an existing render transform).</summary>
    public static TranslateTransform ShiftOf(UIElement e)
    {
        switch (e.RenderTransform)
        {
            case TranslateTransform t: return t;
            case TransformGroup g:
                foreach (var c in g.Children) if (c is TranslateTransform tt) return tt;
                var add = new TranslateTransform();
                g.Children.Add(add);
                return add;
            default:
                var shift = new TranslateTransform();
                e.RenderTransform = e.RenderTransform is { } existing && existing != Transform.Identity
                    ? new TransformGroup { Children = { existing, shift } }
                    : shift;
                return shift;
        }
    }

    double VisualX(UIElement e) => e.TranslatePoint(new Point(0, 0), _panel).X;

    /// <summary>Call on every pointer move. <paramref name="minIndex"/> keeps fixed leading items (e.g. Start) in place.</summary>
    public bool Move(Point p, int minIndex = 0)
    {
        var kids = _panel.Children;
        int from = kids.IndexOf(_item);
        int target = minIndex;
        for (int i = minIndex; i < kids.Count; i++)
        {
            var c = kids[i];
            if (c == _item || c is not FrameworkElement fe) continue;
            if (p.X > VisualX(c) + fe.ActualWidth / 2) target++;
        }
        bool moved = false;
        if (target != from && target >= minIndex)
        {
            var before = new Dictionary<UIElement, double>();
            foreach (UIElement c in kids) if (c != _item) before[c] = VisualX(c);
            kids.RemoveAt(from);
            kids.Insert(Math.Min(target, kids.Count), _item);
            _panel.UpdateLayout();
            foreach (UIElement c in kids)
            {
                if (c == _item || !before.TryGetValue(c, out var was)) continue;
                var t = ShiftOf(c);
                double cur = t.X;
                t.BeginAnimation(TranslateTransform.XProperty, null);
                double layoutX = VisualX(c) - t.X;
                t.X = 0;
                double delta = was - layoutX;
                if (Math.Abs(delta) < 0.5 && Math.Abs(cur) < 0.5) continue;
                t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(delta, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = Ease });
            }
            moved = true;
        }
        // Keep the dragged item glued under the pointer.
        var mine = ShiftOf(_item);
        mine.BeginAnimation(TranslateTransform.XProperty, null);
        double layout = VisualX(_item) - mine.X;
        mine.X = p.X - _grab - layout;
        return moved;
    }

    /// <summary>Drop: the item settles into its slot.</summary>
    public void End()
    {
        var t = ShiftOf(_item);
        double cur = t.X;
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(cur, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = Ease });
        _item.Opacity = 1;
        Panel.SetZIndex(_item, 0);
    }
}
