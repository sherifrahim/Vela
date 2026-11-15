using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Vela.Core;
using Vela.Services;
using Vela.UI;
using Vela.Views;

namespace Vela.Widgets;

/// <summary>
/// Base class for every dock widget. A widget is a tile (rounded rect) with content built in code.
///
/// Lifecycle:  ctor → Build() → [Loaded] OnAttach() … OnTick()/events … [Unloaded] OnDetach() → Dispose()
/// Rules:
///  - Subscribe to services in OnAttach, unsubscribe in OnDetach (services stop polling when nobody listens).
///  - Never block the UI thread; do I/O in services/connectors.
///  - Per-instance settings go in Cfg()/SetCfg() (saved inside settings.json).
/// See docs/ADDING_A_WIDGET.md.
/// </summary>
public abstract class WidgetBase : Grid, IDisposable
{
    /// <summary>Tile height in DIPs at size M (S/L scale the whole bar). Matches the reference dock: 42 tall, 12 radius.</summary>
    public const double TileHeight = 42, TileRadius = 12;

    // ---------- edit mode (widget picker open): minus badges + drag to reorder ----------
    public static bool EditMode { get; private set; }
    public static event Action? EditModeChanged;
    public static void SetEditMode(bool on)
    {
        if (EditMode == on) return;
        EditMode = on;
        EditModeChanged?.Invoke();
    }

    public WidgetInstance Instance { get; internal set; } = new();
    public WidgetDescriptor Descriptor { get; internal set; } = null!;
    public bool IsPreview { get; internal set; }

    protected readonly Border Tile;
    readonly Border _badge;
    readonly ScaleTransform _press = new(1, 1);
    bool _attached, _pressed, _built;
    int _tickCounter;

    public event Action<WidgetBase>? RemoveRequested;
    /// <summary>Extra context-menu items added by a container (e.g. a stack adds "Unstack").</summary>
    public Action<ContextMenu>? ExtraMenu { get; set; }
    /// <summary>True while this widget lives inside a stack (it then has no remove badge of its own).</summary>
    public bool InStack { get; internal set; }

    protected WidgetBase()
    {
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(0);
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _press;
        ClipToBounds = false;

        Tile = new Border
        {
            Height = TileHeight,
            CornerRadius = new CornerRadius(TileRadius),
            Padding = new Thickness(9, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };
        Children.Add(Tile);

        _badge = new Border
        {
            Width = 15, Height = 15, CornerRadius = new CornerRadius(7.5),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -2, -2, 0), Visibility = Visibility.Collapsed, Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
            Child = new Border { Width = 7, Height = 1.6, CornerRadius = new CornerRadius(0.8), Background = Brushes.White },
            ToolTip = "Remove",
        };
        _badge.MouseLeftButtonDown += (_, e) => { e.Handled = true; RemoveRequested?.Invoke(this); };
        Children.Add(_badge);

        ContextMenu = new ContextMenu(); // replaced on every open (OnContextMenuOpening)
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        MouseEnter += (_, _) => UpdateTileBrush(true);
        MouseLeave += (_, _) => { UpdateTileBrush(false); PressVisual(false); _pressed = false; };
    }

    public void Build()
    {
        if (_built) return;
        _built = true;
        if (HasTile)
        {
            UpdateTileBrush(false);
            Tile.SetResourceReference(Border.BorderBrushProperty, "Tile.Border");
            Tile.SetResourceReference(Border.BorderThicknessProperty, "Hairline");
        }
        else
        {
            Tile.Background = Brushes.Transparent;
            Tile.BorderThickness = new Thickness(0);
            Tile.Padding = new Thickness(0);
        }
        Tile.Child = BuildContent();
        if (Clickable) Cursor = Cursors.Hand;
    }

    // ---------- override points ----------
    protected abstract UIElement BuildContent();
    protected virtual bool HasTile => true;
    protected virtual bool Clickable => true;
    /// <summary>0 = no tick. Otherwise OnTick runs every N seconds while attached (and once on attach).</summary>
    protected virtual int TickSeconds => 0;
    protected virtual void OnTick(DateTime now) { }
    protected virtual void OnAttach() { }
    protected virtual void OnDetach() { }
    protected virtual UIElement? BuildCard() => null;
    protected virtual double CardWidth => 340;
    protected virtual void OnClick() => OpenCard();
    protected virtual void OnMiddleClick() { }
    protected virtual void AddMenuItems(ContextMenu menu) { }
    /// <summary>Custom tile fill (e.g. the blue battery tile). Null = theme tile.</summary>
    protected virtual Brush? TileFill => null;

    protected void OpenCard()
    {
        var card = BuildCard();
        if (card != null) FlyoutWindow.Toggle(this, card, CardWidth);
    }

    protected bool CardOpen => FlyoutWindow.Current?.Anchor == this;

    protected void RefreshCard()
    {
        if (!CardOpen) return;
        FlyoutWindow.CloseCurrent();
        Dispatcher.BeginInvoke(OpenCard, System.Windows.Threading.DispatcherPriority.Background);
    }

    // ---------- config ----------
    protected string Cfg(string key, string fallback = "") => Instance.Config.TryGetValue(key, out var v) ? v : fallback;
    /// <summary>Set by a container whose children are stored inside its own config (stacks).</summary>
    internal Action? ConfigChanged { get; set; }

    protected void SetCfg(string key, string value)
    {
        Instance.Config[key] = value;
        ConfigChanged?.Invoke();
        if (!IsPreview) Settings.Save();
    }

    // ---------- plumbing ----------
    void Attach()
    {
        if (_attached) return;
        _attached = true;
        EditModeChanged += OnEditModeChanged;
        OnEditModeChanged();
        try { OnAttach(); } catch (Exception ex) { Log.Error($"{GetType().Name}.OnAttach", ex); }
        if (TickSeconds > 0)
        {
            Svc.Ticker.Tick += Tick;
            SafeTick(DateTime.Now);
        }
    }

    void Detach()
    {
        if (!_attached) return;
        _attached = false;
        EditModeChanged -= OnEditModeChanged;
        if (TickSeconds > 0) Svc.Ticker.Tick -= Tick;
        try { OnDetach(); } catch (Exception ex) { Log.Error($"{GetType().Name}.OnDetach", ex); }
    }

    void Tick(DateTime now)
    {
        if (++_tickCounter < TickSeconds) return;
        _tickCounter = 0;
        SafeTick(now);
    }

    void SafeTick(DateTime now)
    {
        try { OnTick(now); } catch (Exception ex) { Log.Error($"{GetType().Name}.OnTick", ex); }
    }

    void OnEditModeChanged() => _badge.Visibility = EditMode && !IsPreview && !InStack ? Visibility.Visible : Visibility.Collapsed;

    void UpdateTileBrush(bool hover)
    {
        if (!_built || !HasTile) return;
        if (TileFill is { } fill) { Tile.Background = fill; return; }
        Tile.SetResourceReference(Border.BackgroundProperty, hover && Clickable && !EditMode ? "Tile.Hover" : "Tile.Background");
    }

    /// <summary>Re-apply the tile fill after TileFill changes.</summary>
    protected void InvalidateTile() => UpdateTileBrush(IsMouseOver);

    void PressVisual(bool down)
    {
        var to = down ? 0.95 : 1.0;
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(down ? 70 : 140)) { EasingFunction = new QuadraticEase() };
        _press.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        _press.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (EditMode || IsPreview || !Clickable) return;
        _pressed = true;
        PressVisual(true);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_pressed) return;
        _pressed = false;
        PressVisual(false);
        if (e.Handled) return;
        e.Handled = true;
        try { OnClick(); } catch (Exception ex) { Log.Error($"{GetType().Name}.OnClick", ex); }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle && !EditMode && !IsPreview) OnMiddleClick();
    }

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        base.OnContextMenuOpening(e);
        if (IsPreview) { e.Handled = true; return; }
        var menu = new ContextMenu();
        AddMenuItems(menu);
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        if (ExtraMenu != null) { ExtraMenu(menu); menu.Items.Add(new Separator()); }
        else if (!InStack && Parent is Panel p && p.Children.IndexOf(this) is var i && i >= 0 && i + 1 < p.Children.Count
                 && p.Children[i + 1] is WidgetBase next && next.Descriptor?.Type != "apps" && Descriptor?.Type != "apps")
            menu.Items.Add(Item("Stack with next widget", () => DockWindow.For(this)?.StackWithNext(this)));
        menu.Items.Add(Item(InStack ? "Remove from stack" : "Remove from dock", () => RemoveRequested?.Invoke(this)));
        menu.Items.Add(Item("Widgets…", () => DockWindow.Instance?.OpenWidgetPicker()));
        menu.Items.Add(Item("Settings", () => DockWindow.Instance?.OpenSettings()));
        ContextMenu = menu;
    }

    protected static MenuItem Item(string header, Action onClick, bool isChecked = false)
    {
        var m = new MenuItem { Header = header, IsChecked = isChecked };
        m.Click += (_, _) => onClick();
        return m;
    }

    public virtual void Dispose() => Detach();
}
