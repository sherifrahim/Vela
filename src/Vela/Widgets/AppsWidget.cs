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
    LiveReorder? _live;
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
        // Reference Start button: a light Windows mark on a dark rounded tile (same in every style).
        start.SetContent(WindowsLogo.StartTile(AppItem.IconSize));
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
            Settings.Save("pins");
        }
        Svc.Windows.Changed += Rebuild;
        Svc.Windows.ForegroundChanged += UpdateActive;
        Theming.ThemeManager.Changed += OnTheme;
        Settings.Changed += OnSettings;
        if (Settings.Current.AppBadges && !IsPreview) Svc.Notifications.Changed += UpdateBadges;
        Rebuild();
    }

    protected override void OnDetach()
    {
        Svc.Windows.Changed -= Rebuild;
        Svc.Windows.ForegroundChanged -= UpdateActive;
        Theming.ThemeManager.Changed -= OnTheme;
        Settings.Changed -= OnSettings;
        Svc.Notifications.Changed -= UpdateBadges;
        if (_magRunning) { CompositionTarget.Rendering -= MagFrame; _magRunning = false; }
    }

    void OnSettings(string what)
    {
        if (what is not ("pins" or "apps")) return;
        Svc.Notifications.Changed -= UpdateBadges;
        if (Settings.Current.AppBadges && !IsPreview) Svc.Notifications.Changed += UpdateBadges;
        _items.Clear(); // fresh controls: tile style and tooltip/preview policy are constructor/bind state
        if (!Settings.Current.Magnification) _magHover = false;
        Rebuild();
        StartMag();
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
                    // Store apps launch by AUMID (their versioned .exe can't be started directly: access denied).
                    LaunchPath = w.Aumid != null && (w.ExePath == null || IsFrameHost(w.ExePath) || PackagedApps.IsPackagedPath(w.ExePath))
                        ? PackagedApps.LaunchPathFor(w.Aumid)
                        : w.ExePath != null && !IsFrameHost(w.ExePath) ? w.ExePath : null,
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
        bool dividerAdded = false;
        foreach (var g in groups)
        {
            // macOS dock: a thin divider between pinned apps and the other running apps.
            if (!dividerAdded && g.Pin == null && groups.Any(x => x.Pin != null))
            {
                var div = new Border { Width = 1, Height = 22, Margin = new Thickness(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                div.Bg("Divider");
                _panel.Children.Add(div);
                dividerAdded = true;
            }
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
            item.BadgeAumid = g.Pin?.Aumid ?? g.Windows.FirstOrDefault(w => w.Aumid != null)?.Aumid;
            item.BadgeName = g.Name;
            item.BadgeExe = g.Windows.FirstOrDefault(w => w.ExePath != null)?.ExePath ?? g.Pin?.TargetPath;
            var group = g;
            item.ClearHandlers();
            item.Clicked += () => OnItemClick(group, item);
            item.MiddleClicked += () => { if (group.LaunchPath != null) WindowTracker.Launch(group.LaunchPath); };
            item.MenuRequested += m => BuildMenu(m, group);
            var key = g.Key;
            item.HoverStarted += () =>
            {
                if (_dragging || IsPreview || group.Windows.Count == 0) return;
                Views.WindowPreview.Hover(item, () => (IReadOnlyList<AppWindow>?)_groups.FirstOrDefault(x => x.Key == key)?.Windows ?? Array.Empty<AppWindow>(), item.Icon);
            };
            item.HoverEnded += Views.WindowPreview.Leave;
            _panel.Children.Add(item);
        }
        UpdateActive();
        UpdateBadges();
    }

    /// <summary>Unread counts from Action Center on the matching dock icons (Settings → Apps → App badges).</summary>
    void UpdateBadges()
    {
        bool on = Settings.Current.AppBadges && !IsPreview;
        foreach (var item in _items.Values) item.SetBadge(on ? Svc.Notifications.CountFor(item.BadgeAumid, item.BadgeName, item.BadgeExe) : 0);
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
        Views.WindowPreview.CloseAll();
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
        AddJumpList(m, g);
        foreach (var w in g.Windows)
        {
            var title = w.Title.Length > 48 ? w.Title[..48] + "…" : w.Title;
            m.Items.Add(Item(title, () => WindowTracker.Activate(w.Hwnd), w.Hwnd == Svc.Windows.Foreground));
        }
        if (g.Windows.Count > 0) m.Items.Add(new Separator());
        if (g.LaunchPath != null) m.Items.Add(Item(g.Windows.Count > 0 ? "New window" : "Open", () => WindowTracker.Launch(g.LaunchPath)));
        if (g.Pin != null)
            m.Items.Add(Item("Unpin from dock", () => { Settings.Current.PinnedApps.Remove(g.Pin); Settings.Save("pins"); }));
        else if (g.LaunchPath != null && g.Windows.Count > 0)
            m.Items.Add(Item("Pin to dock", () =>
            {
                var w = g.Windows[0];
                Settings.Current.PinnedApps.Add(new PinnedApp { Name = g.Name, LaunchPath = g.LaunchPath, TargetPath = w.ExePath, Aumid = w.Aumid });
                Settings.Save("pins");
            }));
        // Like the reference: a hung app (Windows says "not responding") can be ended from its menu.
        if (g.Windows.FirstOrDefault(w => WindowTracker.IsHung(w.Hwnd)) is { } hung)
            m.Items.Add(Item("End task (not responding)", () => WindowTracker.EndTask(hung.Hwnd)));
        if (g.Windows.Count > 0)
            m.Items.Add(Item(g.Windows.Count > 1 ? "Close all windows" : "Close window", () => { foreach (var w in g.Windows) WindowTracker.Close(w.Hwnd); }));
        m.Items.Add(new Separator());
        m.Items.Add(Item("Widgets…", () => Views.DockWindow.Instance?.OpenWidgetPicker()));
        m.Items.Add(Item("Settings", () => Views.DockWindow.Instance?.OpenSettings()));
    }

    // ---------- jump lists (recent / frequent / tasks), filled in while the menu is already open ----------

    static void AddJumpList(ContextMenu m, Group g)
    {
        var hwnd = g.Windows.FirstOrDefault()?.Hwnd ?? IntPtr.Zero;
        var shortcut = hwnd == IntPtr.Zero ? g.LaunchPath : null;
        if (hwnd == IntPtr.Zero && string.IsNullOrEmpty(shortcut)) return;
        var loading = new MenuItem { Header = "Loading…", IsEnabled = false };
        var sep = new Separator();
        m.Items.Insert(0, loading);
        m.Items.Insert(1, sep);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        m.Closed += (_, _) => cts.Cancel();
        _ = Fill();

        async Task Fill()
        {
            ApplicationJumpList? list = null;
            try { list = await Svc.JumpLists.ReadAsync(hwnd, shortcut, cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warn("Jump list: " + ex.Message); }
            int at = m.Items.IndexOf(loading);
            if (at < 0) return;
            m.Items.Remove(loading);
            if (list is not { Status: JumpListStatus.Ready }) { m.Items.Remove(sep); return; }
            foreach (var group in list.Groups)
            {
                m.Items.Insert(at++, new MenuItem { Header = group.Title, IsEnabled = false, FontWeight = FontWeights.SemiBold });
                foreach (var e in group.Entries.Where(e => !e.IsSeparator).Take(8))
                {
                    var entry = e;
                    var mi = new MenuItem { Header = e.Title.Length > 52 ? e.Title[..52] + "…" : e.Title };
                    if (e.Icon != null) mi.Icon = new Image { Source = e.Icon, Width = 16, Height = 16 };
                    mi.Click += (_, _) => _ = Svc.JumpLists.InvokeAsync(list.SessionId, entry.CommandId, Views.DockWindow.Instance?.WindowHandle ?? IntPtr.Zero);
                    m.Items.Insert(at++, mi);
                }
            }
        }
    }

    // ---------- macOS-style magnification ----------
    // Each frame, every icon eases toward a target scale from a cosine falloff around the pointer
    // (measured in un-magnified slot units, so the strip doesn't chase itself). Runs only while the
    // pointer is over the strip or icons are still settling, then unhooks: zero cost at rest.

    double _mouseSlot = -100;
    bool _magHover, _magRunning;
    readonly Stopwatch _magClock = Stopwatch.StartNew();
    double _lastMagFrame;
    const double MagRange = 2.6; // how many neighbours (each side) grow with the hovered icon
    const double MagResponse = 0.055; // seconds; independent of the display's refresh rate

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
        _lastMagFrame = _magClock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += MagFrame;
    }

    void MagFrame(object? sender, EventArgs e)
    {
        double now = _magClock.Elapsed.TotalSeconds;
        double elapsed = Math.Clamp(now - _lastMagFrame, 0, 0.05);
        _lastMagFrame = now;
        double blend = 1 - Math.Exp(-elapsed / MagResponse);
        bool enabled = Settings.Current.Magnification;
        double max = Math.Clamp(Settings.Current.MagnifyAmount, 1.05, 1.6);
        bool settled = true;
        int i = 0;
        foreach (var it in _panel.Children.OfType<AppItem>())
        {
            double d = Math.Abs(i + 0.5 - _mouseSlot) / MagRange;
            double target = enabled && _magHover && d < 1 ? 1 + (max - 1) * Math.Cos(d * Math.PI / 2) : 1;
            double cur = it.Magnification, next = cur + (target - cur) * blend;
            if (Math.Abs(target - next) < 0.003) next = target; else settled = false;
            if (Math.Abs(next - cur) > 0.0005) it.Magnify(next);
            i++;
        }
        if (settled) { CompositionTarget.Rendering -= MagFrame; _magRunning = false; }
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
        if (IsPreview || EditMode || Settings.Current.LockDock) return; // locked: apps keep their order
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
            Views.WindowPreview.CloseAll();
            // Settle any magnification first so slots have their resting widths while reordering.
            foreach (var it in _panel.Children.OfType<AppItem>()) it.Magnify(1);
            _magHover = false;
            _panel.UpdateLayout();
            _live = new LiveReorder(_panel, _dragItem, _dragStart);
            _panel.CaptureMouse();
        }
        _live?.Move(p, minIndex: 1); // 0 = Start, never moves
    }

    void OnPanelUp(object sender, MouseButtonEventArgs e)
    {
        var item = _dragItem;
        _dragItem = null;
        if (!_dragging || item == null) return;
        _dragging = false;
        _panel.ReleaseMouseCapture();
        _live?.End();
        _live = null;
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
        Settings.Save("pins");
    }
}

/// <summary>One app button: icon, hover lift, running dot(s), launch bounce.</summary>
public sealed class AppItem : Grid
{
    readonly Border _bg, _dot;
    // Focused app (reference): a thin accent outline around the icon, drawn with the icon so it magnifies with it.
    readonly Border _ring = new() { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.25), Margin = new Thickness(-4), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    UIElement? _inner;
    // Unread badge (reference "app badges"): notification count for this app, top-right of the icon.
    readonly Border _badge = new()
    {
        MinWidth = 13, Height = 13, CornerRadius = new CornerRadius(6.5), Padding = new Thickness(3.5, 0, 3.5, 0),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, -5, -6, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
    };
    readonly TextBlock _badgeText = new() { FontSize = 7.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    /// <summary>What notifications are matched on (set by AppsWidget): the app's AppUserModelID and display name.</summary>
    public string? BadgeAumid { get; set; }
    public string? BadgeName { get; set; }
    public string? BadgeExe { get; set; }
    readonly ContentControl _content = new() { Focusable = false, IsTabStop = false };
    readonly TranslateTransform _lift = new();
    readonly ScaleTransform _scale = new(1, 1);
    bool _active;
    public string? Key { get; }
    /// <summary>Icon size and slot width (DIPs at size M), from the reference: 25 px icons on a 36 px pitch.</summary>
    public const double IconSize = 25, SlotWidth = 36;
    /// <summary>App tiles (Settings.AppTiles): each icon in a square tile with a small gap; else loose macOS-style icons.</summary>
    static bool Tiled => Settings.Current.AppTiles;
    const double TileGap = 5;
    static double BaseWidth => Tiled ? WidgetBase.TileHeight + TileGap : SlotWidth;
    readonly Border? _tile;

    public event Action? Clicked;
    public event Action? MiddleClicked;
    public event Action<ContextMenu>? MenuRequested;
    public event Action? HoverStarted;
    public event Action? HoverEnded;
    /// <summary>The app icon currently shown (for previews / menus).</summary>
    public ImageSource? Icon => (_inner as Image)?.Source;

    public AppItem(string? key)
    {
        Key = key;
        Width = BaseWidth;
        Height = WidgetBase.TileHeight;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        _bg = new Border { CornerRadius = new CornerRadius(10), Margin = new Thickness(1, 3, 1, 3), Opacity = 0 };
        _bg.Bg("Tile.Hover");
        if (Tiled)
        {
            // Reference look: every app sits in its own tile (18 % black, hairline edge), like the widgets.
            _tile = new Border { CornerRadius = new CornerRadius(WidgetBase.TileRadius), Margin = new Thickness(TileGap / 2, 0, TileGap / 2, 0) };
            _tile.Bg("Tile.Background");
            _tile.SetResourceReference(Border.BorderBrushProperty, "Tile.Border");
            _tile.SetResourceReference(Border.BorderThicknessProperty, "Hairline");
            _bg.Margin = _tile.Margin;
            _bg.CornerRadius = _tile.CornerRadius;
            Children.Add(_tile);
        }
        _content.HorizontalAlignment = HorizontalAlignment.Center;
        _content.VerticalAlignment = VerticalAlignment.Center;
        _content.Margin = new Thickness(0, 0, 0, 1);
        _content.RenderTransformOrigin = new Point(0.5, 1);
        _content.RenderTransform = new TransformGroup { Children = { _scale, _lift } };
        _dot = new Border { Width = 3, Height = 3, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1.5), Visibility = Visibility.Collapsed };
        _dot.Bg("Accent");
        _ring.SetResourceReference(Border.BorderBrushProperty, "Accent");
        Children.Add(_bg);
        Children.Add(_content);
        Children.Add(_dot);
        ContextMenu = new ContextMenu();
        ToolTipService.SetInitialShowDelay(this, 350);

        MouseEnter += (_, _) => { Hover(true); HoverStarted?.Invoke(); };
        MouseLeave += (_, _) => { Hover(false); HoverEnded?.Invoke(); };
    }

    public void SetContent(UIElement e)
    {
        _inner = e;
        if (_ring.Parent is Grid old) { old.Children.Remove(_ring); old.Children.Remove(_badge); }
        var g = new Grid();
        g.Children.Add(_ring);
        g.Children.Add(e);
        if (_badge.Child == null) { _badge.Child = _badgeText; _badge.Bg("Sys.Red"); }
        g.Children.Add(_badge); // inside the icon's grid, so it magnifies and bounces with it
        _content.Content = g;
    }

    public void SetBadge(int count)
    {
        _badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _badgeText.Text = count > 9 ? "9+" : count.ToString();
    }

    public void ClearHandlers() { Clicked = null; MiddleClicked = null; MenuRequested = null; HoverStarted = null; HoverEnded = null; }

    public void Bind(string name, int windows, bool flashing)
    {
        // Running apps show live previews (with titles) instead of a tooltip.
        ToolTip = Settings.Current.ShowAppNames && (windows == 0 || !Settings.Current.WindowPreviews) ? name : null;
        _dot.Visibility = windows > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Reference "open indicator": a dot, a larger dot, or a short line (wider when the app has several windows).
        var (w1, wn, h) = Settings.Current.OpenIndicator switch { "large" => (5.0, 11.0, 5.0), "line" => (12.0, 18.0, 2.5), _ => (3.0, 8.0, 3.0) };
        _dot.Width = windows > 1 ? wn : w1;
        _dot.Height = h;
        _dot.CornerRadius = new CornerRadius(h / 2);
        _dot.Bg(flashing ? "Sys.Orange" : "Accent");
        // Like the macOS dock: an app asking for attention bounces once.
        if (flashing && !_flashing) Bounce();
        _flashing = flashing;
    }
    bool _flashing;

    public void SetActive(bool active)
    {
        _active = active;
        // Loose icons (reference): no box behind the icon; the focused app gets an accent outline instead.
        _bg.Opacity = !Tiled ? 0 : active ? 1 : IsMouseOver ? 0.7 : 0;
        _ring.Visibility = active && !Tiled && Key != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public void InvalidateVisualTree()
    {
        _inner?.InvalidateVisual();
    }

    /// <summary>Magnification (macOS dock): scale grows from the bottom edge and the slot widens so neighbours make room.</summary>
    public double Magnification { get; private set; } = 1;

    public void Magnify(double s)
    {
        Magnification = s;
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _scale.ScaleX = _scale.ScaleY = s;
        Width = BaseWidth * (1 + (s - 1) * (Tiled ? 0.35 : 0.75));
    }

    void Hover(bool on)
    {
        _bg.Opacity = !Tiled ? 0 : _active ? 1 : on ? 0.7 : 0;
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
