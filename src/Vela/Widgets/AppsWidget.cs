using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Vela.Core;
using Vela.Shell;
using Vela.UI;
using static Vela.UI.UI;

namespace Vela.Widgets;

/// <summary>
/// The taskbar part of the dock: Start, pinned apps and every open window, grouped per app.
/// Click = activate / minimize (taskbar semantics), middle-click = new instance, right-click = jump menu.
/// Updates are event-driven from <see cref="WindowTracker"/>; icon controls are reused across updates.
/// </summary>
public sealed class AppsWidget : WidgetBase
{
    sealed class Group
    {
        public required string Key;
        public PinnedApp? Pin;
        public readonly List<AppWindow> Windows = new();
        public string IconSource = "";
        public string Name = "";
        public string? LaunchPath;
    }

    readonly Dictionary<string, AppItem> _items = new();
    List<Group> _groups = new();
    AppItem? _dragItem;
    bool _dragging;
    Point _dragStart;
    readonly List<string> _runningOrder = new();
    static readonly Dictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);
    StackPanel _panel = null!;

    protected override bool HasTile => false;
    protected override bool Clickable => false;

    protected override UIElement BuildContent()
    {
        _panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (!IsPreview) Views.DockRow.SetFill(this, true); // full-width bar: the apps sit at the screen centre
        var start = new AppItem(null) { ToolTip = "Start" };
        // The Windows 11 Start mark, full icon size, in its own blue.
        start.SetContent(new WindowsLogo { Colored = true, Width = AppItem.IconSize - 3, Height = AppItem.IconSize - 3 });
        start.Clicked += WinActions.OpenStart;
        start.MenuRequested += m =>
        {
            m.Items.Add(Item("Power user menu (Win+X)", () => WinActions.SendKeys(0x5B, 0x58)));
            m.Items.Add(Item("Task Manager", WinActions.OpenTaskManager));
            m.Items.Add(Item("Windows Settings", () => WinActions.OpenSettings("")));
            m.Items.Add(Item("File Explorer", () => WindowTracker.Launch("explorer.exe")));
        };
        _panel.Children.Add(start);
        _panel.PreviewMouseLeftButtonDown += OnPanelDown;
        _panel.PreviewMouseMove += OnPanelMove;
        _panel.PreviewMouseLeftButtonUp += OnPanelUp;
        _panel.MouseMove += OnMagMove;
        _panel.MouseLeave += OnMagLeave;
        _panel.Background = Brushes.Transparent; // hit-testable gaps, so the falloff tracks smoothly
        return _panel;
    }

    protected override void OnAttach()
    {
        if (!Settings.Current.PinnedImported && !IsPreview)
        {
            Settings.Current.PinnedApps = TaskbarPins.Read();
            Settings.Current.PinnedImported = true;
            Settings.Save();
        }
        Svc.Windows.Changed += Rebuild;
        Svc.Windows.ForegroundChanged += UpdateActive;
        Theming.ThemeManager.Changed += OnTheme;
        Settings.Changed += OnSettings;
        Rebuild();
    }

    protected override void OnDetach()
    {
        Svc.Windows.Changed -= Rebuild;
        Svc.Windows.ForegroundChanged -= UpdateActive;
        Theming.ThemeManager.Changed -= OnTheme;
        Settings.Changed -= OnSettings;
        if (_magRunning) { CompositionTarget.Rendering -= MagFrame; _magRunning = false; }
    }

    void OnSettings(string what) { if (what == "pins") Rebuild(); }

    void OnTheme() { foreach (var c in _panel.Children.OfType<AppItem>()) c.InvalidateVisualTree(); }

    List<Group> BuildGroups()
    {
        var groups = new List<Group>();
        var byKey = new Dictionary<string, Group>();
        foreach (var pin in Settings.Current.PinnedApps)
        {
            var key = PinKey(pin);
            if (byKey.ContainsKey(key)) continue;
            var g = new Group { Key = key, Pin = pin, IconSource = pin.LaunchPath, Name = pin.Name, LaunchPath = pin.LaunchPath };
            groups.Add(g); byKey[key] = g;
        }

        foreach (var w in Svc.Windows.Windows.OrderBy(w => w.Hwnd.ToInt64()))
        {
            var g = groups.FirstOrDefault(x => x.Pin != null && Matches(x.Pin, w));
            if (g == null && !byKey.TryGetValue(w.GroupKey, out g))
            {
                g = new Group
                {
                    Key = w.GroupKey,
                    IconSource = IconFor(w),
                    Name = NameFor(w),
                    LaunchPath = w.ExePath != null && !IsFrameHost(w.ExePath) ? w.ExePath : w.Aumid != null ? $"shell:AppsFolder\\{w.Aumid}" : null,
                };
                byKey[w.GroupKey] = g;
                if (!_runningOrder.Contains(g.Key)) _runningOrder.Add(g.Key);
            }
            g.Windows.Add(w);
        }

        var running = byKey.Values.Where(g => g.Pin == null).OrderBy(g => _runningOrder.IndexOf(g.Key));
        _runningOrder.RemoveAll(k => !byKey.ContainsKey(k));
        return groups.Concat(running).ToList();
    }

    static string PinKey(PinnedApp p) => (p.Aumid ?? p.TargetPath ?? p.LaunchPath).ToLowerInvariant();

    static bool Matches(PinnedApp p, AppWindow w) =>
        (p.TargetPath != null && w.ExePath != null && p.TargetPath.Equals(w.ExePath, StringComparison.OrdinalIgnoreCase)) ||
        (p.Aumid != null && w.Aumid != null && p.Aumid.Equals(w.Aumid, StringComparison.OrdinalIgnoreCase));

    static bool IsFrameHost(string exe) => exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);

    static string IconFor(AppWindow w)
    {
        if (w.Aumid != null && (w.ExePath == null || IsFrameHost(w.ExePath) || w.Aumid.Contains('!')))
            return $"shell:AppsFolder\\{w.Aumid}";
        return w.ExePath ?? "";
    }

    static string NameFor(AppWindow w)
    {
        if (w.ExePath == null || IsFrameHost(w.ExePath)) return w.Title;
        if (NameCache.TryGetValue(w.ExePath, out var n)) return n;
        try
        {
            var d = FileVersionInfo.GetVersionInfo(w.ExePath).FileDescription;
            n = string.IsNullOrWhiteSpace(d) ? Path.GetFileNameWithoutExtension(w.ExePath) : d.Trim();
        }
        catch { n = Path.GetFileNameWithoutExtension(w.ExePath); }
        return NameCache[w.ExePath] = n;
    }

    void Rebuild()
    {
        if (_dragging) return;
        var groups = BuildGroups();
        _groups = groups;
        var keep = new HashSet<string>(groups.Select(g => g.Key));
        foreach (var dead in _items.Keys.Where(k => !keep.Contains(k)).ToList()) _items.Remove(dead);

        // Children[0] is Start.
        while (_panel.Children.Count > 1) _panel.Children.RemoveAt(1);
        foreach (var g in groups)
        {
            if (!_items.TryGetValue(g.Key, out var item))
            {
                item = new AppItem(g.Key);
                var icon = new Image { Width = AppItem.IconSize, Height = AppItem.IconSize };
                RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                item.SetContent(icon);
                Svc.Icons.Get(g.IconSource, src => icon.Source = src);
                _items[g.Key] = item;
            }
            item.Bind(g.Name, g.Windows.Count, g.Windows.Any(w => w.Flashing));
            var group = g;
            item.ClearHandlers();
            item.Clicked += () => OnItemClick(group, item);
            item.MiddleClicked += () => { if (group.LaunchPath != null) WindowTracker.Launch(group.LaunchPath); };
            item.MenuRequested += m => BuildMenu(m, group);
            _panel.Children.Add(item);
        }
        UpdateActive();
    }

    void UpdateActive()
    {
        var fg = Svc.Windows.Foreground;
        foreach (var g in BuildGroupsCached())
            if (_items.TryGetValue(g.key, out var item)) item.SetActive(g.hwnds.Contains(fg));
    }

    IEnumerable<(string key, HashSet<IntPtr> hwnds)> BuildGroupsCached() =>
        BuildGroups().Select(g => (g.Key, g.Windows.Select(w => w.Hwnd).ToHashSet()));

    void OnItemClick(Group g, AppItem item)
    {
        if (g.Windows.Count == 0)
        {
            if (g.LaunchPath == null) return;
            WindowTracker.Launch(g.LaunchPath);
            item.Bounce();
            return;
        }
        if (g.Windows.Count == 1) { Svc.Windows.ActivateOrMinimize(g.Windows[0]); return; }
        // Several windows: cycle through them, starting after the one in front.
        var fg = Svc.Windows.Foreground;
        int i = g.Windows.FindIndex(w => w.Hwnd == fg);
        WindowTracker.Activate(g.Windows[(i + 1) % g.Windows.Count].Hwnd);
    }

    void BuildMenu(ContextMenu m, Group g)
    {
        foreach (var w in g.Windows)
        {
            var title = w.Title.Length > 48 ? w.Title[..48] + "…" : w.Title;
            m.Items.Add(Item(title, () => WindowTracker.Activate(w.Hwnd), w.Hwnd == Svc.Windows.Foreground));
        }
        if (g.Windows.Count > 0) m.Items.Add(new Separator());
        if (g.LaunchPath != null) m.Items.Add(Item(g.Windows.Count > 0 ? "New window" : "Open", () => WindowTracker.Launch(g.LaunchPath)));
        if (g.Pin != null)
            m.Items.Add(Item("Unpin from dock", () => { Settings.Current.PinnedApps.Remove(g.Pin); Settings.Save(); Rebuild(); }));
        else if (g.LaunchPath != null && g.Windows.Count > 0)
            m.Items.Add(Item("Pin to dock", () =>
            {
                var w = g.Windows[0];
                Settings.Current.PinnedApps.Add(new PinnedApp { Name = g.Name, LaunchPath = g.LaunchPath, TargetPath = w.ExePath, Aumid = w.Aumid });
                Settings.Save();
                Rebuild();
            }));
        if (g.Windows.Count > 0)
            m.Items.Add(Item(g.Windows.Count > 1 ? "Close all windows" : "Close window", () => { foreach (var w in g.Windows) WindowTracker.Close(w.Hwnd); }));
        m.Items.Add(new Separator());
        m.Items.Add(Item("Widgets…", () => Views.DockWindow.Instance?.OpenWidgetPicker()));
        m.Items.Add(Item("Settings", () => Views.DockWindow.Instance?.OpenSettings()));
    }

    // ---------- macOS-style magnification ----------
    // Each frame, every icon eases toward a target scale from a cosine falloff around the pointer
    // (measured in un-magnified slot units, so the strip doesn't chase itself). Runs only while the
    // pointer is over the strip or icons are still settling, then unhooks: zero cost at rest.

    double _mouseSlot = -100;
    bool _magHover, _magRunning;
    const double MagRange = 2.6; // how many neighbours (each side) grow with the hovered icon

    void OnMagMove(object sender, MouseEventArgs e)
    {
        if (!Settings.Current.Magnification || IsPreview || _dragging) return;
        double mx = e.GetPosition(_panel).X;
        var items = _panel.Children.OfType<AppItem>().ToList();
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            double left = it.TranslatePoint(new Point(0, 0), _panel).X, w = Math.Max(1, it.ActualWidth);
            if (mx < left + w || i == items.Count - 1) { _mouseSlot = i + Math.Clamp((mx - left) / w, 0, 1); break; }
        }
        _magHover = true;
        StartMag();
    }

    void OnMagLeave(object sender, MouseEventArgs e) { _magHover = false; StartMag(); }

    void StartMag()
    {
        if (_magRunning) return;
        _magRunning = true;
        CompositionTarget.Rendering += MagFrame;
    }

    void MagFrame(object? sender, EventArgs e)
    {
        double max = Math.Clamp(Settings.Current.MagnifyAmount, 1.05, 1.6);
        bool settled = true;
        int i = 0;
        foreach (var it in _panel.Children.OfType<AppItem>())
        {
            double d = Math.Abs(i + 0.5 - _mouseSlot) / MagRange;
            double target = _magHover && d < 1 ? 1 + (max - 1) * Math.Cos(d * Math.PI / 2) : 1;
            double cur = it.Magnification, next = cur + (target - cur) * 0.28;
            if (Math.Abs(target - next) < 0.003) next = target; else settled = false;
            if (Math.Abs(next - cur) > 0.0005) it.Magnify(next);
            i++;
        }
        if (settled && !_magHover) { CompositionTarget.Rendering -= MagFrame; _magRunning = false; }
    }

    // ---------- drag icons to reorder pins (dropping a running app among them pins it) ----------

    static AppItem? ItemFrom(object src)
    {
        for (var d = src as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is AppItem a) return a;
        return null;
    }

    void OnPanelDown(object sender, MouseButtonEventArgs e)
    {
        if (IsPreview || EditMode) return;
        var item = ItemFrom(e.OriginalSource);
        if (item?.Key == null) return;
        _dragItem = item;
        _dragStart = e.GetPosition(_panel);
    }

    void OnPanelMove(object sender, MouseEventArgs e)
    {
        if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_panel);
        if (!_dragging)
        {
            if (Math.Abs(p.X - _dragStart.X) < 7) return;
            _dragging = true;
            _dragItem.Opacity = 0.7;
            _panel.CaptureMouse();
        }
        int from = _panel.Children.IndexOf(_dragItem), target = 1; // 0 = Start, never moves
        for (int i = 1; i < _panel.Children.Count; i++)
        {
            var c = (FrameworkElement)_panel.Children[i];
            if (c == _dragItem) continue;
            if (p.X > c.TranslatePoint(new Point(c.ActualWidth / 2, 0), _panel).X) target++;
        }
        if (target != from) { _panel.Children.RemoveAt(from); _panel.Children.Insert(target, _dragItem); }
    }

    void OnPanelUp(object sender, MouseButtonEventArgs e)
    {
        var item = _dragItem;
        _dragItem = null;
        if (!_dragging || item == null) return;
        _dragging = false;
        _panel.ReleaseMouseCapture();
        item.Opacity = 1;
        e.Handled = true;

        var order = _panel.Children.OfType<AppItem>().Skip(1).Select(i => i.Key).ToList();
        int lastPinned = order.FindLastIndex(k => _groups.Any(g => g.Key == k && g.Pin != null));
        var pins = new List<PinnedApp>();
        for (int i = 0; i < order.Count; i++)
        {
            var g = _groups.FirstOrDefault(x => x.Key == order[i]);
            if (g == null) continue;
            if (g.Pin != null) pins.Add(g.Pin);
            else if (g.Key == item.Key && i <= lastPinned + 1 && g.LaunchPath != null && g.Windows.Count > 0)
            {
                var w = g.Windows[0];
                pins.Add(new PinnedApp { Name = g.Name, LaunchPath = g.LaunchPath, TargetPath = w.ExePath, Aumid = w.Aumid });
            }
        }
        Settings.Current.PinnedApps = pins;
        Settings.Save();
        Rebuild();
    }
}

/// <summary>One app button: icon, hover lift, running dot(s), launch bounce.</summary>
public sealed class AppItem : Grid
{
    readonly Border _bg, _dot;
    readonly ContentControl _content = new() { Focusable = false, IsTabStop = false };
    readonly TranslateTransform _lift = new();
    readonly ScaleTransform _scale = new(1, 1);
    bool _active;
    public string? Key { get; }
    /// <summary>Icon size and slot width (DIPs at size M), from the reference: 25 px icons on a 36 px pitch.</summary>
    public const double IconSize = 25, SlotWidth = 36;

    public event Action? Clicked;
    public event Action? MiddleClicked;
    public event Action<ContextMenu>? MenuRequested;

    public AppItem(string? key)
    {
        Key = key;
        Width = SlotWidth;
        Height = WidgetBase.TileHeight;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        _bg = new Border { CornerRadius = new CornerRadius(10), Margin = new Thickness(1, 3, 1, 3), Opacity = 0 };
        _bg.Bg("Tile.Hover");
        _content.HorizontalAlignment = HorizontalAlignment.Center;
        _content.VerticalAlignment = VerticalAlignment.Center;
        _content.Margin = new Thickness(0, 0, 0, 1);
        _content.RenderTransformOrigin = new Point(0.5, 1);
        _content.RenderTransform = new TransformGroup { Children = { _scale, _lift } };
        _dot = new Border { Width = 3, Height = 3, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1.5), Visibility = Visibility.Collapsed };
        _dot.Bg("Accent");
        Children.Add(_bg);
        Children.Add(_content);
        Children.Add(_dot);
        ContextMenu = new ContextMenu();
        ToolTipService.SetInitialShowDelay(this, 350);

        MouseEnter += (_, _) => Hover(true);
        MouseLeave += (_, _) => Hover(false);
    }

    public void SetContent(UIElement e) => _content.Content = e;

    public void ClearHandlers() { Clicked = null; MiddleClicked = null; MenuRequested = null; }

    public void Bind(string name, int windows, bool flashing)
    {
        ToolTip = Settings.Current.ShowAppNames ? name : null;
        _dot.Visibility = windows > 0 ? Visibility.Visible : Visibility.Collapsed;
        _dot.Width = windows > 1 ? 8 : 3;
        _dot.Bg(flashing ? "Sys.Orange" : "Accent");
    }

    public void SetActive(bool active)
    {
        _active = active;
        _bg.Opacity = active ? 1 : IsMouseOver ? 0.7 : 0;
    }

    public void InvalidateVisualTree()
    {
        foreach (var fe in new[] { _content.Content as UIElement }) fe?.InvalidateVisual();
    }

    /// <summary>Magnification (macOS dock): scale grows from the bottom edge and the slot widens so neighbours make room.</summary>
    public double Magnification { get; private set; } = 1;

    public void Magnify(double s)
    {
        Magnification = s;
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _scale.ScaleX = _scale.ScaleY = s;
        Width = SlotWidth * (1 + (s - 1) * 0.75);
    }

    void Hover(bool on)
    {
        _bg.Opacity = _active ? 1 : on && !Settings.Current.Magnification ? 0.7 : 0;
        if (Settings.Current.Magnification) return; // the strip drives scale itself (AppsWidget.Magnifier)
        var dur = TimeSpan.FromMilliseconds(on ? 140 : 180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(on ? 1.12 : 1, dur) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(on ? 1.12 : 1, dur) { EasingFunction = ease });
    }

    /// <summary>Mac-style launch bounce.</summary>
    public void Bounce()
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1300) };
        foreach (var (t, y) in new[] { (0, 0.0), (180, -8.0), (360, 0.0), (540, -4.5), (700, 0.0), (850, -2.0), (1000, 0.0) })
            a.KeyFrames.Add(new EasingDoubleKeyFrame(y, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(t)), new QuadraticEase { EasingMode = y < 0 ? EasingMode.EaseOut : EasingMode.EaseIn }));
        _lift.BeginAnimation(TranslateTransform.YProperty, a);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (WidgetBase.EditMode) return;
        e.Handled = true;
        Clicked?.Invoke();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle) { e.Handled = true; MiddleClicked?.Invoke(); }
    }

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        base.OnContextMenuOpening(e);
        var m = new ContextMenu();
        MenuRequested?.Invoke(m);
        if (m.Items.Count == 0) { e.Handled = true; return; }
        ContextMenu = m;
    }

    static MenuItem Item(string header, Action a) { var m = new MenuItem { Header = header }; m.Click += (_, _) => a(); return m; }
}
