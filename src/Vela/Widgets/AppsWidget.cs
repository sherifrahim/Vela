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
    readonly List<string> _runningOrder = new();
    static readonly Dictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);
    StackPanel _panel = null!;

    protected override bool HasTile => false;
    protected override bool Clickable => false;

    protected override UIElement BuildContent()
    {
        _panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var start = new AppItem(null) { ToolTip = "Start" };
        start.SetContent(new WindowsLogo { Width = 22, Height = 22 });
        start.Clicked += WinActions.OpenStart;
        start.MenuRequested += m =>
        {
            m.Items.Add(Item("Power user menu (Win+X)", () => WinActions.SendKeys(0x5B, 0x58)));
            m.Items.Add(Item("Task Manager", WinActions.OpenTaskManager));
            m.Items.Add(Item("Windows Settings", () => WinActions.OpenSettings("")));
            m.Items.Add(Item("File Explorer", () => WindowTracker.Launch("explorer.exe")));
        };
        _panel.Children.Add(start);
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
        Rebuild();
    }

    protected override void OnDetach()
    {
        Svc.Windows.Changed -= Rebuild;
        Svc.Windows.ForegroundChanged -= UpdateActive;
        Theming.ThemeManager.Changed -= OnTheme;
    }

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
        var groups = BuildGroups();
        var keep = new HashSet<string>(groups.Select(g => g.Key));
        foreach (var dead in _items.Keys.Where(k => !keep.Contains(k)).ToList()) _items.Remove(dead);

        // Children[0] is Start.
        while (_panel.Children.Count > 1) _panel.Children.RemoveAt(1);
        foreach (var g in groups)
        {
            if (!_items.TryGetValue(g.Key, out var item))
            {
                item = new AppItem(g.Key);
                var icon = new Image { Width = 30, Height = 30 };
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

    public event Action? Clicked;
    public event Action? MiddleClicked;
    public event Action<ContextMenu>? MenuRequested;

    public AppItem(string? key)
    {
        Key = key;
        Width = 44;
        Height = WidgetBase.TileHeight;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        _bg = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(2, 2, 2, 2), Opacity = 0 };
        _bg.Bg("Tile.Hover");
        _content.HorizontalAlignment = HorizontalAlignment.Center;
        _content.VerticalAlignment = VerticalAlignment.Center;
        _content.Margin = new Thickness(0, 0, 0, 2);
        _content.RenderTransformOrigin = new Point(0.5, 1);
        _content.RenderTransform = new TransformGroup { Children = { _scale, _lift } };
        _dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2), Visibility = Visibility.Collapsed };
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
        _dot.Width = windows > 1 ? 10 : 4;
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

    void Hover(bool on)
    {
        _bg.Opacity = _active ? 1 : on ? 0.7 : 0;
        var dur = TimeSpan.FromMilliseconds(on ? 140 : 180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(on ? 1.12 : 1, dur) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(on ? 1.12 : 1, dur) { EasingFunction = ease });
    }

    /// <summary>Mac-style launch bounce.</summary>
    public void Bounce()
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1300) };
        foreach (var (t, y) in new[] { (0, 0.0), (180, -10.0), (360, 0.0), (540, -6.0), (700, 0.0), (850, -3.0), (1000, 0.0) })
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
