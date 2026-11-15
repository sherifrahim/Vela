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
    public const double TileHeight = 48;

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
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };
        Children.Add(Tile);

        _badge = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -3, -3, 0), Visibility = Visibility.Collapsed, Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
            Child = new Border { Width = 8, Height = 2, CornerRadius = new CornerRadius(1), Background = Brushes.White },
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
    protected void SetCfg(string key, string value)
    {
        Instance.Config[key] = value;
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

    void OnEditModeChanged() => _badge.Visibility = EditMode && !IsPreview ? Visibility.Visible : Visibility.Collapsed;

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
        menu.Items.Add(Item("Remove from dock", () => RemoveRequested?.Invoke(this)));
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
