using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Vela.Core;
using Vela.Shell;
using Vela.Theming;
using Vela.Widgets;
using static Vela.Interop.Native;

namespace Vela.Views;

/// <summary>
/// The dock: one borderless, non-activating, topmost window spanning the primary monitor's edge,
/// whose visible shape (window region) is just the rounded bar.
/// Owns: appbar registration, taskbar hiding, fullscreen hiding, edge drag, widget layout/reorder.
/// </summary>
public partial class DockWindow : GlassWindow
{
    public static DockWindow? Instance { get; private set; }

    /// <summary>Registered message for `Vela.exe --widgets / --settings` (wParam 1 / 2).</summary>
    public static readonly uint CommandMessage = RegisterWindowMessage("Vela.Command");

    AppBar? _appBar;
    uint _taskbarCreatedMsg;
    bool _fullscreenHidden, _repositionQueued;
    readonly ScaleTransform _sizeScale = new(1, 1);
    WidgetPickerWindow? _picker;
    SettingsWindow? _settings;
    int _enforceCounter;

    // edge drag / reorder state
    Point _dragStart;
    bool _barDragging, _barDragMoved;
    WidgetBase? _reorderItem;

    public event Action? WidgetsChanged;

    public DockWindow()
    {
        InitializeComponent();
        Instance = this;
        NoActivate = true;
        RegionElement = Bar;
        RegionRadius = 20;
        Bar.LayoutTransform = _sizeScale;
        Left = -32000; Top = -32000; Width = 100; Height = 60;

        ThemeManager.Changed += ApplyLook;
        Settings.Changed += OnSettingChanged;
        WallpaperGlass.Changed += UpdateGlass;
        Bar.SizeChanged += (_, _) => UpdateGlass();
        WidgetBase.EditModeChanged += () => Items.Cursor = WidgetBase.EditMode ? Cursors.SizeWE : null;

        Bar.ContextMenu = new ContextMenu();
        Bar.ContextMenuOpening += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindWidget(d) != null) return; // widget menus handle themselves
            Bar.ContextMenu = BuildBarMenu();
        };
        Closed += (_, _) => Application.Current.Shutdown();
        Bar.MouseLeftButtonDown += OnBarMouseDown;
        Bar.MouseMove += OnBarMouseMove;
        Bar.MouseLeftButtonUp += OnBarMouseUp;
        Items.PreviewMouseLeftButtonDown += OnItemsPreviewDown;
        Items.PreviewMouseMove += OnItemsPreviewMove;
        Items.PreviewMouseLeftButtonUp += OnItemsPreviewUp;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(Hwnd).AddHook(WndProc);
        _appBar = new AppBar(Hwnd);
        _appBar.Register();
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        Svc.Windows.Start(Hwnd);
        Svc.Windows.ForegroundChanged += CheckFullscreen;
        Svc.Taskbar.Apply(Settings.Current.TaskbarMode);
        Svc.Ticker.Tick += OnTick;

        BuildWidgets();
        ApplyLook();
    }

    // ------------------------------------------------------------------ layout & position

    void ApplyLook()
    {
        double s = ThemeManager.SizeScale;
        _sizeScale.ScaleX = _sizeScale.ScaleY = s;
        RegionScale = s;
        Sheen.Visibility = ThemeManager.Current.Sheen.A > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplySpacing();
        QueueReposition();
        UpdateRegion();
        UpdateGlass();
    }

    /// <summary>Line the blurred wallpaper slice up with the bar's current screen position.</summary>
    void UpdateGlass()
    {
        if (Hwnd == IntPtr.Zero || !Bar.IsLoaded || ThemeManager.Current.Backdrop == BackdropKind.None)
        {
            Bar.Background = null;
            return;
        }
        try
        {
            var tl = Bar.PointToScreen(new Point(0, 0));
            var br = Bar.PointToScreen(new Point(Bar.ActualWidth, Bar.ActualHeight));
            var m = GetPrimaryMonitor(out _).rcMonitor;
            Bar.Background = WallpaperGlass.BrushFor(new Rect(tl, br), new Rect(m.Left, m.Top, m.Width, m.Height));
        }
        catch (InvalidOperationException) { /* not yet connected to a presentation source */ }
    }

    void ApplySpacing()
    {
        double half = Settings.Current.Spacing / 2;
        foreach (FrameworkElement c in Items.Children) c.Margin = new Thickness(half, 0, half, 0);
        Items.Margin = new Thickness(-half, 0, -half, 0);
    }

    void OnSettingChanged(string what)
    {
        switch (what)
        {
            case "theme": case "size": case "accent": ThemeManager.Apply(); break;
            case "dock": ApplyLook(); break;
            case "taskbar": Svc.Taskbar.Apply(Settings.Current.TaskbarMode); QueueReposition(); break;
        }
    }

    public void QueueReposition()
    {
        if (_repositionQueued) return;
        _repositionQueued = true;
        Dispatcher.BeginInvoke(() => { _repositionQueued = false; Reposition(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    void Reposition()
    {
        if (Hwnd == IntPtr.Zero || _appBar == null) return;
        var mi = GetPrimaryMonitor(out var hmon);
        double dpi = GetMonitorScale(hmon);
        bool top = Settings.Current.Edge == "top";
        double margin = Settings.Current.EdgeMargin;
        double barH = (WidgetBase.TileHeight + 12) * ThemeManager.SizeScale;   // tile + padding + border
        int hPx = (int)Math.Ceiling((barH + margin + 4) * dpi);

        var rect = mi.rcMonitor;
        if (top) rect.Bottom = rect.Top + hPx; else rect.Top = rect.Bottom - hPx;
        if (Settings.Current.ReserveSpace) rect = _appBar.Reserve(mi.rcMonitor, top, hPx);
        else _appBar.Release(mi.rcMonitor, top);

        Bar.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        Bar.Margin = top ? new Thickness(0, margin, 0, 0) : new Thickness(0, 0, 0, margin);
        SetWindowPos(Hwnd, HWND_TOPMOST, mi.rcMonitor.Left, top ? mi.rcMonitor.Top : mi.rcMonitor.Bottom - hPx,
            mi.rcMonitor.Width, hPx, SWP_NOACTIVATE);
        UpdateRegion();
        Dispatcher.BeginInvoke(UpdateGlass, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ------------------------------------------------------------------ widgets

    void BuildWidgets()
    {
        foreach (var w in Items.Children.OfType<WidgetBase>().ToList()) w.Dispose();
        Items.Children.Clear();
        foreach (var inst in Settings.Current.Widgets.ToList()) AddControl(inst);
        EnsurePlaceholder();
        ApplySpacing();
    }

    WidgetBase? AddControl(WidgetInstance inst)
    {
        var w = WidgetRegistry.Create(inst);
        if (w == null) return null;
        w.RemoveRequested += RemoveWidget;
        Items.Children.Add(w);
        return w;
    }

    void EnsurePlaceholder()
    {
        var existing = Items.Children.OfType<Button>().FirstOrDefault(b => b.Tag as string == "placeholder");
        bool empty = !Items.Children.OfType<WidgetBase>().Any();
        if (empty && existing == null)
        {
            var b = UI.UI.PillButton("+  Add widgets", OpenWidgetPicker);
            b.Tag = "placeholder";
            b.Height = WidgetBase.TileHeight;
            Items.Children.Add(b);
        }
        else if (!empty && existing != null) Items.Children.Remove(existing);
    }

    public bool HasWidget(string type) => Settings.Current.Widgets.Any(w => w.Type == type);
    public int WidgetCount => Settings.Current.Widgets.Count;

    public void AddWidget(string type)
    {
        var inst = new WidgetInstance { Type = type };
        Settings.Current.Widgets.Add(inst);
        AddControl(inst);
        EnsurePlaceholder();
        ApplySpacing();
        Settings.Save("widgets");
        WidgetsChanged?.Invoke();
    }

    public void RemoveWidgetType(string type)
    {
        var w = Items.Children.OfType<WidgetBase>().LastOrDefault(x => x.Instance.Type == type);
        if (w != null) RemoveWidget(w);
    }

    void RemoveWidget(WidgetBase w)
    {
        if (FlyoutWindow.Current?.Anchor == w) FlyoutWindow.Current.CloseNow();
        Items.Children.Remove(w);
        w.Dispose();
        Settings.Current.Widgets.Remove(w.Instance);
        EnsurePlaceholder();
        Settings.Save("widgets");
        WidgetsChanged?.Invoke();
    }

    void PersistOrder()
    {
        Settings.Current.Widgets = Items.Children.OfType<WidgetBase>().Select(w => w.Instance).ToList();
        Settings.Save("widgets");
    }

    static WidgetBase? FindWidget(DependencyObject? d)
    {
        while (d != null && d is not WidgetBase) d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        return d as WidgetBase;
    }

    // ------------------------------------------------------------------ reorder (edit mode only)

    void OnItemsPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (!WidgetBase.EditMode) return;
        var w = FindWidget(e.OriginalSource as DependencyObject);
        if (w == null || w.Parent != Items) return;
        _reorderItem = w;
        _dragStart = e.GetPosition(Items);
        Items.CaptureMouse();
        e.Handled = true;
    }

    void OnItemsPreviewMove(object sender, MouseEventArgs e)
    {
        if (_reorderItem == null) return;
        var p = e.GetPosition(Items);
        int from = Items.Children.IndexOf(_reorderItem);
        int target = from;
        double x = 0;
        for (int i = 0; i < Items.Children.Count; i++)
        {
            var c = (FrameworkElement)Items.Children[i];
            double mid = x + c.ActualWidth / 2;
            if (p.X > mid) target = i;
            x += c.ActualWidth + c.Margin.Left + c.Margin.Right;
        }
        if (p.X < ((FrameworkElement)Items.Children[0]).ActualWidth / 2) target = 0;
        if (target != from)
        {
            Items.Children.RemoveAt(from);
            Items.Children.Insert(target, _reorderItem);
        }
    }

    void OnItemsPreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (_reorderItem == null) return;
        Items.ReleaseMouseCapture();
        _reorderItem = null;
        PersistOrder();
        e.Handled = true;
    }

    // ------------------------------------------------------------------ drag the bar to the other edge

    void OnBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (WidgetBase.EditMode || FindWidget(e.OriginalSource as DependencyObject) != null) return;
        if (e.OriginalSource is not (Border or StackPanel or Grid)) return;
        _barDragging = true;
        _barDragMoved = false;
        _dragStart = PointToScreen(e.GetPosition(this));
        Bar.CaptureMouse();
    }

    void OnBarMouseMove(object sender, MouseEventArgs e)
    {
        if (!_barDragging) return;
        var p = PointToScreen(e.GetPosition(this));
        if (Math.Abs(p.Y - _dragStart.Y) > 24 && !_barDragMoved) { _barDragMoved = true; Bar.Opacity = 0.75; }
    }

    void OnBarMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_barDragging) return;
        _barDragging = false;
        Bar.ReleaseMouseCapture();
        Bar.Opacity = 1;
        if (!_barDragMoved) return;
        GetCursorPos(out var pt);
        var mi = GetPrimaryMonitor(out _);
        var edge = pt.Y < (mi.rcMonitor.Top + mi.rcMonitor.Bottom) / 2 ? "top" : "bottom";
        if (edge != Settings.Current.Edge) { Settings.Current.Edge = edge; Settings.Save("dock"); }
    }

    // ------------------------------------------------------------------ menus & windows

    ContextMenu BuildBarMenu()
    {
        var m = new ContextMenu();
        m.Items.Add(MI("Widgets…", OpenWidgetPicker));
        m.Items.Add(MI("Settings", OpenSettings));
        m.Items.Add(new Separator());
        var style = new MenuItem { Header = "Style" };
        foreach (var p in Palettes.All)
            style.Items.Add(MI(p.Name, () => { Settings.Current.Theme = p.Id; Settings.Save("theme"); }, Settings.Current.Theme == p.Id));
        m.Items.Add(style);
        var size = new MenuItem { Header = "Size" };
        foreach (var (id, name) in new[] { ("S", "Small"), ("M", "Medium"), ("L", "Large") })
            size.Items.Add(MI(name, () => { Settings.Current.Size = id; Settings.Save("size"); }, Settings.Current.Size == id));
        m.Items.Add(size);
        bool top = Settings.Current.Edge == "top";
        m.Items.Add(MI(top ? "Move to bottom" : "Move to top", () => { Settings.Current.Edge = top ? "bottom" : "top"; Settings.Save("dock"); }));
        m.Items.Add(new Separator());
        m.Items.Add(MI("Task Manager", WinActions.OpenTaskManager));
        m.Items.Add(MI("Restart Vela", Restart));
        m.Items.Add(MI("Quit Vela", () => Application.Current.Shutdown()));
        return m;
    }

    static MenuItem MI(string header, Action a, bool isChecked = false)
    {
        var mi = new MenuItem { Header = header, IsChecked = isChecked };
        mi.Click += (_, _) => a();
        return mi;
    }

    static void Restart()
    {
        if (Environment.ProcessPath is { } exe)
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Arguments = "--wait-for-exit" });
        }
        Application.Current.Shutdown();
    }

    public void OpenWidgetPicker()
    {
        FlyoutWindow.CloseCurrent();
        if (_picker == null)
        {
            _picker = new WidgetPickerWindow(this);
            _picker.Closed += (_, _) => { _picker = null; WidgetBase.SetEditMode(false); WinActions.TrimWorkingSet(); };
            WidgetBase.SetEditMode(true);
            _picker.Show();
        }
        _picker.Activate();
    }

    public void OpenSettings()
    {
        FlyoutWindow.CloseCurrent();
        if (_settings == null)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => { _settings = null; WinActions.TrimWorkingSet(); };
            _settings.Show();
        }
        _settings.Activate();
    }

    // ------------------------------------------------------------------ fullscreen & system messages

    void OnTick(DateTime _)
    {
        if (++_enforceCounter >= 3) { _enforceCounter = 0; Svc.Taskbar.Enforce(); }
    }

    void CheckFullscreen()
    {
        bool fs = IsFullscreenActive();
        if (fs == _fullscreenHidden) return;
        _fullscreenHidden = fs;
        if (fs)
        {
            FlyoutWindow.CloseCurrent();
            ShowWindow(Hwnd, SW_HIDE);
            Svc.Ticker.Paused = true;   // dock stops drawing and polling entirely
        }
        else
        {
            ShowWindow(Hwnd, SW_SHOWNOACTIVATE);
            SetWindowPos(Hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            Svc.Ticker.Paused = false;
        }
    }

    bool IsFullscreenActive()
    {
        if (SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4) return true; // busy / D3D fullscreen / presentation
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == Hwnd) return false;
        var cls = GetClassNameOf(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        long style = GetWindowLongPtr(fg, GWL_STYLE).ToInt64();
        if ((style & 0x00C00000L) == 0x00C00000L) return false; // has a caption => normal (maybe maximized) window
        if (!GetWindowRect(fg, out var r)) return false;
        var mi = GetPrimaryMonitor(out _);
        var m = mi.rcMonitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_appBar != null && msg == _appBar.CallbackMessage)
        {
            switch (wParam.ToInt32())
            {
                case ABN_POSCHANGED: QueueReposition(); break;
                case ABN_FULLSCREENAPP: Dispatcher.BeginInvoke(CheckFullscreen); break;
            }
        }
        else if (msg == Svc.Windows.ShellHookMessage && msg != 0)
        {
            Svc.Windows.HandleShellMessage(wParam, lParam);
        }
        else if (msg == _taskbarCreatedMsg && msg != 0)
        {
            Log.Info("Explorer restarted; re-applying taskbar + appbar");
            Svc.Taskbar.OnExplorerRestarted();
            _appBar?.Register();
            QueueReposition();
        }
        else if (msg == CommandMessage)
        {
            if (wParam.ToInt32() == 1) Dispatcher.BeginInvoke(OpenWidgetPicker);
            else if (wParam.ToInt32() == 2) Dispatcher.BeginInvoke(OpenSettings);
            handled = true;
        }
        else if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        else if (msg is WM_DISPLAYCHANGE or WM_DPICHANGED)
        {
            QueueReposition();
        }
        else if (msg == WM_SETTINGCHANGE && wParam.ToInt32() == 0x0014 /*SPI_SETDESKWALLPAPER*/)
        {
            Dispatcher.BeginInvoke(WallpaperGlass.Reload);
        }
        return IntPtr.Zero;
    }

    /// <summary>Called from App.OnExit. Order matters: give back the screen space, then restore the taskbar.</summary>
    public void Teardown()
    {
        try
        {
            Svc.Ticker.Tick -= OnTick;
            if (Hwnd != IntPtr.Zero) Svc.Windows.Stop(Hwnd);
            _appBar?.Dispose();
        }
        catch (Exception ex) { Log.Error("Dock teardown", ex); }
    }
}
