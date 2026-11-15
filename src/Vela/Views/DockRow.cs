using System.Windows;
using System.Windows.Controls;

namespace Vela.Views;

/// <summary>
/// The dock's item row. Lays children left to right like a horizontal StackPanel, but when the bar is
/// wider than its content (full-width mode) it behaves like a taskbar: widgets before the <c>Fill</c>
/// child (the apps strip) hug the left end, widgets after it hug the right end, and the apps strip sits
/// at the screen centre (clamped so it never overlaps its neighbours). Without a Fill child the row is centred.
/// </summary>
public sealed class DockRow : Panel
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached(
        "Fill", typeof(bool), typeof(DockRow), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static void SetFill(UIElement e, bool value) => e.SetValue(FillProperty, value);
    public static bool GetFill(UIElement e) => (bool)e.GetValue(FillProperty);

    /// <summary>Total width the items want at 100 %, so the dock can shrink the row to fit the screen.</summary>
    public double NaturalWidth { get; private set; }
    public event Action? NaturalWidthChanged;

    protected override Size MeasureOverride(Size available)
    {
        double w = 0, h = 0;
        var unbounded = new Size(double.PositiveInfinity, available.Height);
        foreach (UIElement c in InternalChildren)
        {
            c.Measure(unbounded);
            w += c.DesiredSize.Width;
            h = Math.Max(h, c.DesiredSize.Height);
        }
        if (Math.Abs(w - NaturalWidth) > 0.5)
        {
            NaturalWidth = w;
            Dispatcher.BeginInvoke(() => NaturalWidthChanged?.Invoke(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var kids = InternalChildren;
        double total = 0;
        int fill = -1;
        for (int i = 0; i < kids.Count; i++)
        {
            total += kids[i].DesiredSize.Width;
            if (fill < 0 && kids[i].Visibility == Visibility.Visible && GetFill(kids[i])) fill = i;
        }
        double extra = final.Width - total;

        if (extra <= 0.5 || fill < 0)
        {
            double x = Math.Max(0, extra) / 2;
            foreach (UIElement c in kids) { c.Arrange(new Rect(x, 0, c.DesiredSize.Width, final.Height)); x += c.DesiredSize.Width; }
            return final;
        }

        double left = 0, right = 0;
        for (int i = 0; i < kids.Count; i++)
            if (i < fill) left += kids[i].DesiredSize.Width; else if (i > fill) right += kids[i].DesiredSize.Width;
        double fw = kids[fill].DesiredSize.Width;
        double fx = Math.Clamp((final.Width - fw) / 2, left, final.Width - right - fw);

        double lx = 0, rx = final.Width - right;
        for (int i = 0; i < kids.Count; i++)
        {
            var c = kids[i];
            double cw = c.DesiredSize.Width;
            if (i < fill) { c.Arrange(new Rect(lx, 0, cw, final.Height)); lx += cw; }
            else if (i == fill) c.Arrange(new Rect(fx, 0, cw, final.Height));
            else { c.Arrange(new Rect(rx, 0, cw, final.Height)); rx += cw; }
        }
        return final;
    }
}
