using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vela.Core;
using Vela.UI;
using Vela.Widgets;
using static Vela.UI.UI;

namespace Vela.Views;

/// <summary>
/// "All Widgets": category sidebar + search on the left, live previews on the right.
/// Click a preview to add/remove it. While open, the dock is in edit mode (minus badges, drag to reorder).
/// </summary>
public sealed class WidgetPickerWindow : GlassWindow
{
    readonly DockWindow _dock;
    readonly StackPanel _sections = new();
    readonly StackPanel _sidebar = new();
    readonly TextBlock _title = Text("All Widgets", 20, FontWeights.Bold);
    readonly TextBlock _countPill = Text("", 11.5, FontWeights.Bold, "Text.Secondary");
    readonly TextBlock _inBar = Text("", 12, FontWeights.SemiBold, "Text.Secondary");
    readonly TextBox _search = TextBox("", "Search widgets");
    readonly List<WidgetBase> _previews = new();
    string? _category;

    static readonly Dictionary<string, string> CategoryGlyph = new()
    {
        ["Clocks"] = Glyphs.Clock, ["Productivity"] = Glyphs.Task, ["System"] = Glyphs.Settings, ["Apps & Files"] = Glyphs.AllApps,
        ["Media & Audio"] = Glyphs.Volume, ["AI"] = Glyphs.Lightning, ["Finance"] = Glyphs.Money, ["Stocks"] = Glyphs.Chart,
        ["Weather"] = Glyphs.Brightness, ["Online & Mail"] = Glyphs.Mail,
    };

    public WidgetPickerWindow(DockWindow dock)
    {
        _dock = dock;
        Title = "Vela widgets";
        Topmost = true;
        Width = 980; Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RegionRadius = 26;

        var root = new Border { CornerRadius = new CornerRadius(26), BorderThickness = new Thickness(1), ClipToBounds = true };
        root.Bg("Flyout.Background");
        root.SetResourceReference(Border.BorderBrushProperty, "Flyout.Border");
        RegionElement = root;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        // sidebar
        var side = new Border { Padding = new Thickness(14, 16, 14, 16), BorderThickness = new Thickness(0, 0, 1, 0) };
        side.SetResourceReference(Border.BorderBrushProperty, "Divider");
        _search.Margin = new Thickness(0, 0, 0, 14);
        _search.TextChanged += (_, _) => Render();
        side.Child = V(_search, _sidebar);
        side.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == side) DragMove(); };
        grid.Children.Add(side);

        // content
        var header = Columns("auto,auto,auto,*,auto",
            _title,
            new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(10, 0, 0, 0), Child = _countPill, VerticalAlignment = VerticalAlignment.Center }.Bg("Fill.Control"),
            _inBar.Margin(10, 0, 0, 0),
            new Border { Background = Brushes.Transparent },
            CircleButton(Glyphs.Close, Close, 32, "Fill.Control", "Text.Primary", 11));
        header.Margin = new Thickness(0, 0, 0, 14);
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is Grid or Border) DragMove(); };
        var scroll = new ScrollViewer { Content = _sections, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) };
        var right = new DockPanel { Margin = new Thickness(22, 18, 14, 18) };
        DockPanel.SetDock(header, Dock.Top);
        right.Children.Add(header);
        right.Children.Add(scroll);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        root.Child = grid;
        Content = root;

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        _dock.WidgetsChanged += Render;
        Closed += (_, _) =>
        {
            _dock.WidgetsChanged -= Render;
            foreach (var p in _previews) p.Dispose();
            _previews.Clear();
        };
        BuildSidebar();
        Render();
        Loaded += (_, _) => _search.Focus();
    }

    void BuildSidebar()
    {
        _sidebar.Children.Clear();
        void Add(string? cat, string label, string glyph)
        {
            bool sel = _category == cat;
            var content = H(Icon(glyph, 14, sel ? "OnAccent" : "Text.Secondary").Margin(0, 0, 12, 0), Text(label, 13.5, FontWeights.SemiBold, sel ? "OnAccent" : "Text.Primary"));
            var b = GhostButton(content, () => { _category = cat; BuildSidebar(); Render(); });
            b.Padding = new Thickness(12, 9, 12, 9);
            b.Margin = new Thickness(0, 0, 0, 3);
            if (sel) b.Bg("Accent");
            _sidebar.Children.Add(b);
        }
        Add(null, "All Widgets", Glyphs.Grid);
        foreach (var c in WidgetRegistry.Categories)
            if (WidgetRegistry.All.Any(d => d.Category == c)) Add(c, c, CategoryGlyph.GetValueOrDefault(c, Glyphs.Grid));
    }

    void Render()
    {
        foreach (var p in _previews) p.Dispose();
        _previews.Clear();
        _sections.Children.Clear();

        var q = _search.Text.Trim();
        var list = WidgetRegistry.All.Where(d => !d.Hidden &&
            (_category == null || d.Category == _category) &&
            (q.Length == 0 || d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || d.Category.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();

        _title.Text = _category ?? "All Widgets";
        _countPill.Text = list.Count.ToString();
        _inBar.Text = $"{_dock.WidgetCount} in bar";

        foreach (var cat in WidgetRegistry.Categories)
        {
            var items = list.Where(d => d.Category == cat).ToList();
            if (items.Count == 0) continue;
            var head = H(Icon(CategoryGlyph.GetValueOrDefault(cat, Glyphs.Grid), 13, "Text.Secondary").Margin(0, 0, 8, 0), Text(cat, 13.5, FontWeights.SemiBold, "Text.Secondary"));
            head.Margin = new Thickness(0, 6, 0, 10);
            _sections.Children.Add(head);
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            foreach (var d in items) wrap.Children.Add(Card(d));
            _sections.Children.Add(wrap);
        }
        if (list.Count == 0) _sections.Children.Add(Text("No widgets match.", 13, null, "Text.Secondary"));
    }

    FrameworkElement Card(WidgetDescriptor d)
    {
        bool inDock = _dock.HasWidget(d.Type);
        var preview = WidgetRegistry.Create(new WidgetInstance { Type = d.Type }, preview: true);
        var holder = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(2),
            MinWidth = 120,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            ToolTip = inDock ? "On the dock — click to remove" : "Click to add",
        };
        holder.SetResourceReference(Border.BorderBrushProperty, inDock ? "Accent" : "Transparent");
        var inner = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
        if (preview != null)
        {
            preview.IsHitTestVisible = false;
            _previews.Add(preview);
            inner.Children.Add(preview);
        }
        holder.Child = inner;
        holder.MouseEnter += (_, _) => holder.Bg("Fill.Control");
        holder.MouseLeave += (_, _) => holder.Background = Brushes.Transparent;
        holder.MouseLeftButtonUp += (_, _) =>
        {
            if (_dock.HasWidget(d.Type)) _dock.RemoveWidgetType(d.Type);
            else _dock.AddWidget(d.Type);
        };

        var label = H(Text(d.Name, 12, FontWeights.SemiBold, "Text.Secondary"));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        if (inDock) label.Children.Insert(0, Icon(Glyphs.Check, 10, "Accent").Margin(0, 0, 5, 0));
        if (d.Connector != null) label.Children.Add(Icon(Glyphs.Plug, 10, "Text.Tertiary").Margin(5, 0, 0, 0).Tip("Needs a connection"));
        label.Margin = new Thickness(0, 6, 0, 0);
        return V(holder, label).Margin(0, 0, 14, 14);
    }
}
