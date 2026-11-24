using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vela.Core;
using Vela.Services;
using Vela.UI;
using static Vela.Interop.Native;
using static Vela.UI.UI;

namespace Vela.Views;

/// <summary>
/// Search launcher (Alt+Space, the Search tile, or Vela.exe --search): a frosted field near the top of the screen the
/// pointer is on. Typing lists the top hit, apps, Windows Settings pages, commands, files and folders (Windows Search
/// index) and a web search; ↑/↓ choose, Enter opens, Ctrl+Enter shows a file in Explorer, Esc clears, then closes.
/// Layout follows Spotlight (WacOS 2beced5 measurements: one column, best result first, sections with headers).
/// </summary>
public sealed class LauncherWindow : GlassWindow
{
    const double PanelW = 680, MaxListH = 440;
    static LauncherWindow? _open;

    readonly Border _root;
    readonly TextBox _box;
    readonly StackPanel _list = new();
    readonly ScrollViewer _scroll;
    readonly Border _divider;
    sealed record RowUi(SearchResult Result, Border Row, TextBlock Name, TextBlock Detail, TextBlock? Glyph);
    readonly List<RowUi> _rows = new();
    List<SearchResult> _results = new(), _files = new();
    int _sel;
    bool _closing;
    CancellationTokenSource? _filesCts;
    readonly System.Windows.Threading.DispatcherTimer _filesTimer;

    public static bool IsOpen => _open != null;

    /// <summary>Open (on the pointer's display) or close.</summary>
    public static void Toggle()
    {
        if (_open != null) { _open.CloseNow(); return; }
        _open = new LauncherWindow();
        _open.ShowOnPointerDisplay();
    }

    LauncherWindow()
    {
        Title = "Vela Search";
        Topmost = true;
        Width = PanelW + 2; Height = 58 + MaxListH + 40;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -20000; Top = -20000;
        RegionRadius = 22;

        _box = new TextBox
        {
            FontSize = 21, BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0), FontFamily = Font,
        };
        _box.SetResourceReference(ForegroundProperty, "Text.Primary");
        _box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "Text.Primary");
        var hint = Text("Search apps, settings, files", 21, null, "Text.Tertiary");
        hint.IsHitTestVisible = false;
        _box.TextChanged += (_, _) => { hint.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; QueryChanged(); };
        var field = new Grid { Height = 58, Margin = new Thickness(18, 0, 18, 0) };
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        field.ColumnDefinitions.Add(new ColumnDefinition());
        field.Children.Add(Icon(Glyphs.Search, 19, "Text.Secondary").Margin(0, 0, 14, 0));
        var boxHost = new Grid();
        boxHost.Children.Add(hint);
        boxHost.Children.Add(_box);
        hint.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(boxHost, 1);
        field.Children.Add(boxHost);

        _divider = new Border { Height = 1, Visibility = Visibility.Collapsed }.Bg("Divider");
        _scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = MaxListH, Padding = new Thickness(8, 6, 8, 8) };
        _scroll.Visibility = Visibility.Collapsed;

        _root = new Border
        {
            CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top, Width = PanelW,
            Child = V(field, _divider, _scroll),
        };
        _root.Bg("Panel.Background");
        _root.SetResourceReference(Border.BorderBrushProperty, "Flyout.Border");
        RegionElement = _root;
        LiveBackdrop = true;
        Content = _root;

        _filesTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _filesTimer.Tick += (_, _) => { _filesTimer.Stop(); _ = LoadFilesAsync(); };

        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => CloseNow();
        Closed += (_, _) => { _filesCts?.Cancel(); _filesTimer.Stop(); if (_open == this) _open = null; Shell.WinActions.TrimWorkingSet(); };

        Launcher.RefreshApps(() => { if (!_closing) Render(); });
        Render();
    }

    /// <summary>The panel for <paramref name="query"/> (files included, searched synchronously), detached for an off-screen render.</summary>
    internal static FrameworkElement Preview(string query)
    {
        var w = new LauncherWindow();
        w._closing = true; // never shown; stops async updates
        w._box.Text = query;
        w._filesTimer.Stop();
        if (query.Trim().Length >= 2) { w._files = Launcher.SearchFilesAsync(query, CancellationToken.None).GetAwaiter().GetResult(); w.Render(); }
        w.Content = null;
        w._root.Margin = new Thickness(0);
        return w._root;
    }

    // ---------------------------------------------------------------- placement & focus

    void ShowOnPointerDisplay()
    {
        GetCursorPos(out var pt);
        var mon = MonitorFromPoint(pt, 2 /*nearest*/);
        var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(mon, ref mi);
        var work = mi.rcWork;
        Show();
        // First onto the target display (WPF rescales for its DPI), then centred, a fifth of the way down.
        SetWindowPos(Hwnd, HWND_TOPMOST, work.Left + 20, work.Top + 20, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        Dispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            GetWindowRect(Hwnd, out var r);
            int w = r.Right - r.Left;
            SetWindowPos(Hwnd, HWND_TOPMOST, work.Left + (work.Width - w) / 2, work.Top + work.Height / 5, 0, 0, SWP_NOSIZE);
            Activate();
            SetForegroundWindow(Hwnd);
            _box.Focus();
            Keyboard.Focus(_box);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void CloseNow()
    {
        if (_closing) return;
        _closing = true;
        HideLiveBackdrop();
        Close();
    }

    // ---------------------------------------------------------------- query

    void QueryChanged()
    {
        _filesCts?.Cancel();
        _files = new();
        _filesTimer.Stop();
        if (_box.Text.Trim().Length >= 2) _filesTimer.Start();
        Render();
    }

    async Task LoadFilesAsync()
    {
        var q = _box.Text;
        var cts = _filesCts = new CancellationTokenSource();
        var found = await Launcher.SearchFilesAsync(q, cts.Token);
        if (cts.IsCancellationRequested || _closing || q != _box.Text) return;
        // Folders and files Windows indexed; skip what the apps list already shows (e.g. an .exe).
        _files = found.Where(f => !_results.Any(r => r.Kind == ResultKind.App && r.Name.Equals(System.IO.Path.GetFileNameWithoutExtension(f.Name), StringComparison.OrdinalIgnoreCase))).ToList();
        Render(keepSelection: true);
    }

    // ---------------------------------------------------------------- list

    void Render(bool keepSelection = false)
    {
        var selected = keepSelection && _sel < _rows.Count ? _rows[_sel].Result : null;
        var q = _box.Text.Trim();
        _results = Launcher.Search(q);
        _list.Children.Clear();
        _rows.Clear();

        if (q.Length == 0)
        {
            if (_results.Count > 0) Section("Frequently used", _results);
        }
        else
        {
            var web = _results.Where(r => r.Kind == ResultKind.Web).ToList();
            var rest = _results.Where(r => r.Kind != ResultKind.Web).ToList();
            var top = rest.FirstOrDefault();
            if (top != null && top.Score >= 50) { Section("Top Hit", new[] { top }, big: true); rest.Remove(top); }
            else if (top != null && _files.Count > 0 && _files[0].Score >= 80) { top = _files[0]; Section("Top Hit", new[] { top }, big: true); }
            else top = null;
            Section("Apps", rest.Where(r => r.Kind == ResultKind.App).Take(6));
            Section("Settings", rest.Where(r => r.Kind == ResultKind.Setting).Take(4));
            Section("Commands", rest.Where(r => r.Kind == ResultKind.Command).Take(4));
            Section("Files & Folders", _files.Where(f => f != top).Take(8));
            Section("Web", web);
        }

        bool any = _rows.Count > 0;
        _divider.Visibility = _scroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        int at = selected == null ? -1 : _rows.FindIndex(r => r.Result.Name == selected.Name && r.Result.Kind == selected.Kind);
        Select(at >= 0 ? at : 0, scroll: false);
        _scroll.ScrollToTop();
    }

    void Section(string title, IEnumerable<SearchResult> items, bool big = false)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        _list.Children.Add(Text(title, 11, FontWeights.SemiBold, "Text.Secondary").Margin(12, _list.Children.Count == 0 ? 4 : 10, 0, 4));
        foreach (var r in list) AddRow(r, big);
    }

    void AddRow(SearchResult r, bool big)
    {
        double iconSize = big ? 36 : 26;
        FrameworkElement icon;
        TextBlock? glyph = null;
        if (r.IconSource != null)
        {
            var img = new Image { Width = iconSize, Height = iconSize };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            Svc.Icons.Get(r.IconSource, src => img.Source = src, big ? 96 : 64);
            icon = img;
        }
        else
        {
            var tile = new Border { Width = iconSize, Height = iconSize, CornerRadius = new CornerRadius(big ? 9 : 7) }.Bg("Fill.Control");
            glyph = Icon(r.Glyph ?? Glyphs.Search, big ? 17 : 13, "Text.Primary");
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            tile.Child = glyph;
            icon = tile;
        }

        var name = Text(r.Name, big ? 16 : 13.5, FontWeights.SemiBold);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var detail = Text(r.Detail, big ? 12 : 11.5, null, "Text.Secondary");
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        FrameworkElement text;
        if (big) text = V(name, detail);
        else
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 280 });
            detail.Margin = new Thickness(12, 0, 0, 0);
            detail.VerticalAlignment = name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(detail, 1);
            g.Children.Add(name);
            g.Children.Add(detail);
            text = g;
        }
        text.VerticalAlignment = VerticalAlignment.Center;

        var dock = new DockPanel();
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        dock.Children.Add(text);

        var row = new Border { CornerRadius = new CornerRadius(10), Padding = big ? new Thickness(10, 8, 12, 8) : new Thickness(10, 6, 12, 6), Background = Brushes.Transparent, Child = dock };
        int index = _rows.Count;
        row.MouseMove += (_, _) => { if (_sel != index) Select(index, scroll: false); };
        row.MouseLeftButtonUp += (_, _) => OpenAt(index, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        _rows.Add(new RowUi(r, row, name, detail, glyph));
        _list.Children.Add(row);
    }

    void Select(int i, bool scroll = true)
    {
        if (_rows.Count == 0) { _sel = 0; return; }
        i = Math.Clamp(i, 0, _rows.Count - 1);
        if (_sel < _rows.Count) Paint(_rows[_sel], false);
        _sel = i;
        Paint(_rows[i], true);
        if (scroll) _rows[i].Row.BringIntoView();
    }

    static void Paint(RowUi r, bool on)
    {
        if (on) r.Row.Bg("Accent"); else r.Row.Background = Brushes.Transparent;
        r.Name.SetResourceReference(TextBlock.ForegroundProperty, on ? "OnAccent" : "Text.Primary");
        r.Detail.SetResourceReference(TextBlock.ForegroundProperty, on ? "OnAccent" : "Text.Secondary");
        r.Glyph?.SetResourceReference(TextBlock.ForegroundProperty, "Text.Primary");
    }

    // ---------------------------------------------------------------- keys & opening

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: case Key.Tab when !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift): Select(_sel + 1); e.Handled = true; break;
            case Key.Up: case Key.Tab: Select(_sel - 1); e.Handled = true; break;
            case Key.PageDown: Select(_sel + 6); e.Handled = true; break;
            case Key.PageUp: Select(_sel - 6); e.Handled = true; break;
            case Key.Enter: OpenAt(_sel, Keyboard.Modifiers.HasFlag(ModifierKeys.Control)); e.Handled = true; break;
            case Key.Escape:
                if (_box.Text.Length > 0) _box.Clear(); else CloseNow();
                e.Handled = true;
                break;
        }
    }

    void OpenAt(int i, bool reveal)
    {
        if (i < 0 || i >= _rows.Count) return;
        var r = _rows[i].Result;
        var q = Launcher.Fold(_box.Text.Trim());
        CloseNow();
        try
        {
            if (reveal && r.Path != null) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{r.Path}\"");
            else r.Open();
            if (r.Key != null) Launcher.Learn(r.Key, q);
        }
        catch (Exception ex) { Log.Warn($"Launcher: couldn't open {r.Name}: {ex.Message}"); }
    }
}
