using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vela.Connectors;
using Vela.Core;
using Vela.Services;
using Vela.Shell;
using Vela.Theming;
using Vela.UI;
using Vela.Widgets;
using static Vela.UI.UI;

namespace Vela.Views;

/// <summary>
/// The Vela control panel: a sidebar of pages (General, Appearance, Dock, Widgets, Apps, Weather,
/// Connectors, Privacy, About). Every switch applies live through <see cref="Settings.Save"/>.
/// </summary>
public sealed class SettingsWindow : GlassWindow
{
    readonly StackPanel _body = new();
    readonly StackPanel _nav = new();
    readonly TextBlock _pageTitle = Text("", 20, FontWeights.Bold);
    readonly ScrollViewer _scroll;
    string _page = "General";

    static readonly (string Name, string Glyph)[] Pages =
    {
        ("General", Glyphs.Settings), ("Appearance", ""), ("Dock", ""), ("Widgets", Glyphs.Grid),
        ("Apps", Glyphs.AllApps), ("Weather", ""), ("Connectors", Glyphs.Plug), ("Privacy", Glyphs.Lock), ("About", ""),
    };

    public SettingsWindow(string? page = null)
    {
        Title = "Vela control panel";
        Topmost = true;
        Width = 860; Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RegionRadius = 24;
        if (page != null) _page = page;

        var root = new Border { CornerRadius = new CornerRadius(24) };
        root.Bg("Flyout.Background");
        root.SetResourceReference(Border.BorderBrushProperty, "Flyout.Border");
        root.SetResourceReference(Border.BorderThicknessProperty, "Hairline");
        RegionElement = root;

        // Sidebar
        var brand = H(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Child = Icon(Glyphs.Grid, 12, "OnAccent") }.Bg("Accent"),
            Text("Vela", 15, FontWeights.Bold).Margin(10, 0, 0, 0));
        brand.Margin = new Thickness(6, 0, 0, 18);
        var side = new DockPanel { Width = 210, Margin = new Thickness(14, 20, 0, 16) };
        DockPanel.SetDock(brand, Dock.Top);
        side.Children.Add(brand);
        var quit = GhostButton(H(Icon("", 12, "Sys.Red"), Text("Quit Vela", 12.5, FontWeights.SemiBold, "Sys.Red").Margin(10, 0, 0, 0)), () => Application.Current.Shutdown());
        quit.HorizontalContentAlignment = HorizontalAlignment.Left;
        DockPanel.SetDock(quit, Dock.Bottom);
        side.Children.Add(quit);
        side.Children.Add(_nav);

        var sideBg = new Border { Child = side, CornerRadius = new CornerRadius(24, 0, 0, 24) }.Bg("Tile.Background");

        // Content
        var header = Columns("*,auto", _pageTitle, CircleButton(Glyphs.Close, Close, 30, "Fill.Control", "Text.Primary", 10));
        header.Margin = new Thickness(0, 0, 0, 14);
        header.Background = Brushes.Transparent;
        _scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) };
        var content = new DockPanel { Margin = new Thickness(26, 20, 16, 20) };
        DockPanel.SetDock(header, Dock.Top);
        content.Children.Add(header);
        content.Children.Add(_scroll);

        var grid = Columns("auto,*", sideBg, content);
        root.Child = grid;
        Content = root;

        // Drag the window by its title area or the brand only. (Dragging from the whole sidebar used to start a
        // window move on every press, which swallowed clicks on the page list.)
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == header || e.OriginalSource == _pageTitle) TryDrag(); };
        brand.MouseLeftButtonDown += (_, _) => TryDrag();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Settings.Changed += OnSettingsChanged;
        Closed += (_, _) => Settings.Changed -= OnSettingsChanged;
        Show(_page);
    }

    void TryDrag()
    {
        try { if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); } catch (InvalidOperationException) { }
    }

    void OnSettingsChanged(string what) { if (what is "widgets" or "pins") Rebuild(); }

    public void Show(string page)
    {
        _page = page;
        _nav.Children.Clear();
        foreach (var (name, glyph) in Pages)
        {
            bool sel = name == _page;
            var row = H(Icon(glyph, 13, sel ? "OnAccent" : "Text.Secondary"), Text(name, 13, FontWeights.SemiBold, sel ? "OnAccent" : "Text.Primary").Margin(11, 0, 0, 0));
            var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 14, 2), Child = row, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            if (sel) b.Bg("Accent");
            else { b.MouseEnter += (_, _) => b.Bg("Fill.Control"); b.MouseLeave += (_, _) => b.Background = Brushes.Transparent; }
            var p = name;
            b.MouseLeftButtonDown += (_, e) => { e.Handled = true; Show(p); };
            _nav.Children.Add(b);
        }
        _pageTitle.Text = page;
        Build();
        _scroll.ScrollToTop();
    }

    void Rebuild() => Dispatcher.BeginInvoke(() => { var off = _scroll.VerticalOffset; Build(); _scroll.ScrollToVerticalOffset(off); }, System.Windows.Threading.DispatcherPriority.Background);

    void Build()
    {
        _body.Children.Clear();
        switch (_page)
        {
            case "General": General(); break;
            case "Appearance": Appearance(); break;
            case "Dock": DockPage(); break;
            case "Widgets": WidgetsPage(); break;
            case "Apps": AppsPage(); break;
            case "Weather": Weather(); break;
            case "Connectors": ConnectorsPage(); break;
            case "Privacy": Privacy(); break;
            default: About(); break;
        }
    }

    // ================================================================= pages

    void General()
    {
        var s = Settings.Current;
        Toggle("Launch at sign-in", "Start Vela when you sign in to Windows.", Settings.LaunchAtStartup, v => Settings.LaunchAtStartup = v);
        Field("Windows taskbar", Wrap(new[] { ("hidden", "Hidden"), ("autohide", "Auto-hide"), ("keep", "Leave as is") }
            .Select(x => Segment(x.Item2, "taskbar", s.TaskbarMode == x.Item1, () => { s.TaskbarMode = x.Item1; Settings.Save("taskbar"); }))),
            "Restored exactly as it was whenever Vela quits. Emergency: run  Vela.exe --restore-taskbar");
        Toggle("Reserve screen space", "Maximized windows stop above the dock instead of going underneath.", s.ReserveSpace, v => { s.ReserveSpace = v; Settings.Save("dock"); });
        Toggle("24-hour clock", null, s.Use24Hour, v => { s.Use24Hour = v; Settings.Save("clock"); });
        Toggle("Show app names on hover", null, s.ShowAppNames, v => { s.ShowAppNames = v; Settings.Save(); });
        Field("Quick actions", Wrap(new[]
        {
            Btn("Widget gallery", () => DockWindow.Instance?.OpenWidgetPicker()),
            Btn("Windows Quick Settings", () => WinActions.OpenQuickSettings()),
            Btn("Restart Vela", RestartApp),
        }));
    }

    void Appearance()
    {
        var s = Settings.Current;
        Field("Style", Wrap(Palettes.All.Select(p => Segment(p.Name, "theme", s.Theme == p.Id, () => { s.Theme = p.Id; Settings.Save("theme"); }))));
        var accents = new WrapPanel();
        foreach (var (name, hex) in Palettes.Accents)
        {
            bool sel = s.Accent.Equals(hex, StringComparison.OrdinalIgnoreCase);
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Brush(hex), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, ToolTip = name, BorderThickness = new Thickness(sel ? 3 : 0) };
            dot.SetResourceReference(Border.BorderBrushProperty, "Text.Primary");
            dot.MouseLeftButtonUp += (_, _) => { s.Accent = hex; Settings.Save("accent"); Rebuild(); };
            accents.Children.Add(dot);
        }
        Field("Accent colour", accents);
        Field("Size", Wrap(new[] { ("S", "Small"), ("M", "Medium"), ("L", "Large") }
            .Select(x => Segment(x.Item2, "size", s.Size == x.Item1, () => { s.Size = x.Item1; Settings.Save("size"); }))));
        Field("Widget tiles", Wrap(new[] { ("auto", "Match style"), ("black", "Black"), ("white", "White"), ("glass", "Glass") }
            .Select(x => Segment(x.Item2, "tiles", s.WidgetStyle == x.Item1, () => { s.WidgetStyle = x.Item1; Settings.Save("theme"); }))));

        Section("Glass");
        Field("Effect", Wrap(new[] { ("auto", "Style default"), ("off", "Off (solid)"), ("frosted", "Frosted"), ("liquid", "Liquid Glass") }
            .Select(x => Segment(x.Item2, "glassfx", s.GlassEffect == x.Item1, () => { s.GlassEffect = x.Item1; Settings.Save("glass"); Rebuild(); }))),
            "Frosted blurs and brightens the desktop behind the bar (macOS vibrancy). Liquid Glass adds a specular rim and glow to the bar and every tile.");
        Field("Blur strength", SliderRow(s.GlassBlur, 0, 100, "%", v => { s.GlassBlur = Math.Round(v); Settings.Save("glass"); }));
        Toggle("Glass tiles", "Widgets become frosted glass panes of their own instead of solid tiles.", s.GlassTiles, v => { s.GlassTiles = v; Settings.Save("glass"); });
    }

    void DockPage()
    {
        var s = Settings.Current;
        Field("Position", Wrap(new[] { ("bottom", "Bottom"), ("top", "Top") }
            .Select(x => Segment(x.Item2, "edge", s.Edge == x.Item1, () => { s.Edge = x.Item1; Settings.Save("dock"); }))),
            "Or drag the bar's empty space to the other edge.");
        Field("Width", Wrap(new[] { ("full", "Edge to edge"), ("fit", "Fit content") }
            .Select(x => Segment(x.Item2, "barwidth", s.BarWidth == x.Item1, () => { s.BarWidth = x.Item1; Settings.Save("dock"); }))),
            "Edge to edge keeps widgets at both ends with your apps centred, like a taskbar.");
        Field("Distance from screen edge", SliderRow(s.EdgeMargin, 0, 24, " px", v => { s.EdgeMargin = Math.Round(v); Settings.Save("dock"); }));
        Field("Spacing between widgets", SliderRow(s.Spacing, 0, 24, " px", v => { s.Spacing = Math.Round(v); Settings.Save("dock"); }));
        Toggle("Show desktop corner", "A sliver at the far end: hover to peek at the desktop, click to show it.", s.DesktopCorner, v => { s.DesktopCorner = v; Settings.Save("dock"); });
        Field("Displays", Wrap(new[] { ("all", "Every display"), ("primary", "Main display only") }
            .Select(x => Segment(x.Item2, "displays", s.Displays == x.Item1, () => { s.Displays = x.Item1; Settings.Save("displays"); }))),
            "With several monitors, each one gets its own dock with the same widgets.");
        Toggle("Magnification", "Icons grow under the pointer and their neighbours make room, like the macOS dock.", s.Magnification, v => { s.Magnification = v; Settings.Save(); });
        Field("Magnification size", SliderRow(s.MagnifyAmount * 100, 110, 160, "%", v => { s.MagnifyAmount = Math.Round(v) / 100; Settings.Save(); }));
    }

    void WidgetsPage()
    {
        Note("Drag widgets on the dock to rearrange them, or use the arrows here. Right-click a widget → \"Stack with next widget\" to put two in one slot (scroll to flip).");
        _body.Children.Add(H(Btn("Add widgets…", () => DockWindow.Instance?.OpenWidgetPicker(), true),
            Btn("Reset to default layout", () => { Settings.Current.Widgets = Settings.DefaultLayout(); Settings.SaveNow(); RestartApp(); }).Margin(8, 0, 0, 0)).Margin(0, 0, 0, 12));
        var list = Settings.Current.Widgets.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var inst = list[i];
            var d = WidgetRegistry.Get(inst.Type);
            string name = d?.Name ?? inst.Type, sub = d?.Category ?? "";
            if (inst.Type == "stack")
            {
                try
                {
                    var kids = System.Text.Json.JsonSerializer.Deserialize<List<WidgetInstance>>(inst.Config.GetValueOrDefault("items", "[]"), Json.Options) ?? new();
                    name = "Stack"; sub = string.Join(" · ", kids.Select(k => WidgetRegistry.Get(k.Type)?.Name ?? k.Type));
                }
                catch { }
            }
            var buttons = H(
                CircleButton(Glyphs.ChevronUp, () => DockWindow.Instance?.MoveWidget(inst, -1), 26, "Fill.Control", "Text.Primary", 9).Tip("Move left"),
                CircleButton(Glyphs.ChevronDown, () => DockWindow.Instance?.MoveWidget(inst, 1), 26, "Fill.Control", "Text.Primary", 9).Tip("Move right").Margin(6, 0, 0, 0),
                CircleButton(Glyphs.Close, () => DockWindow.Instance?.RemoveInstance(inst), 26, "Fill.Control", "Sys.Red", 9).Tip("Remove").Margin(6, 0, 0, 0));
            var row = Columns("28,*,auto", Text($"{i + 1}", 12, FontWeights.Bold, "Text.Tertiary"), V(Text(name, 13, FontWeights.SemiBold), Text(sub, 11, null, "Text.Secondary")), buttons);
            _body.Children.Add(Group(row, 6));
        }
    }

    void AppsPage()
    {
        var s = Settings.Current;
        Note("Pinned apps show on the dock even when closed. Drag icons on the dock to reorder; drop a running app among the pins to pin it.");
        _body.Children.Add(H(Btn("Import my Windows taskbar pins", () => { s.PinnedApps = TaskbarPins.Read(); Settings.Save("pins"); })).Margin(0, 0, 0, 12));
        for (int i = 0; i < s.PinnedApps.Count; i++)
        {
            var pin = s.PinnedApps[i];
            int idx = i;
            var icon = new Image { Width = 22, Height = 22 };
            Svc.Icons.Get(pin.LaunchPath, src => icon.Source = src);
            var buttons = H(
                CircleButton(Glyphs.ChevronUp, () => MovePin(idx, -1), 26, "Fill.Control", "Text.Primary", 9).Tip("Move left"),
                CircleButton(Glyphs.ChevronDown, () => MovePin(idx, 1), 26, "Fill.Control", "Text.Primary", 9).Tip("Move right").Margin(6, 0, 0, 0),
                CircleButton(Glyphs.Unpin, () => { s.PinnedApps.Remove(pin); Settings.Save("pins"); }, 26, "Fill.Control", "Text.Primary", 9).Tip("Unpin").Margin(6, 0, 0, 0));
            _body.Children.Add(Group(Columns("34,*,auto", icon, Text(pin.Name, 13, FontWeights.SemiBold), buttons), 6));
        }
        if (s.PinnedApps.Count == 0) Note("No pinned apps.");
    }

    static void MovePin(int i, int delta)
    {
        var pins = Settings.Current.PinnedApps;
        int j = i + delta;
        if (j < 0 || j >= pins.Count) return;
        (pins[i], pins[j]) = (pins[j], pins[i]);
        Settings.Save("pins");
    }

    void Weather()
    {
        var s = Settings.Current;
        var cityBox = TextBox(s.Weather.City ?? "", "Search a city…");
        var results = new StackPanel();
        async void Search()
        {
            results.Children.Clear();
            if (cityBox.Text.Trim().Length < 2) return;
            try
            {
                foreach (var r in await WeatherService.SearchCityAsync(cityBox.Text.Trim()))
                {
                    var b = GhostButton(Text(r.Display, 13, FontWeights.SemiBold), () =>
                    {
                        s.Weather.City = r.Name; s.Weather.Lat = r.Lat; s.Weather.Lon = r.Lon;
                        Settings.Save("weather"); Rebuild();
                    });
                    b.HorizontalContentAlignment = HorizontalAlignment.Left;
                    results.Children.Add(b);
                }
                if (results.Children.Count == 0) results.Children.Add(Text("No match.", 12, null, "Text.Secondary"));
            }
            catch { results.Children.Add(Text("Couldn't reach the weather service.", 12, null, "Sys.Red")); }
        }
        cityBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Search(); };
        Field("City", V(Columns("*,auto", cityBox, Btn("Find", Search).Margin(8, 0, 0, 0)), results.Margin(0, 6, 0, 0)),
            s.Weather.City != null ? $"Current: {s.Weather.City}. Only these coordinates are sent to open-meteo.com." : "Weather uses open-meteo.com (no account). The clock also shows the temperature once a city is set.");
        Field("Units", Wrap(new[] { ("C", "°C"), ("F", "°F") }
            .Select(x => Segment(x.Item2, "unit", s.Weather.Unit == x.Item1, () => { s.Weather.Unit = x.Item1; Settings.Save("weather"); }))));
    }

    void ConnectorsPage()
    {
        Note("Each connector talks straight from this PC to its service. Keys are encrypted with Windows DPAPI and never written to settings.json.");
        foreach (var c in Svc.AllConnectors) ConnectorRow(c);
    }

    void Privacy()
    {
        var s = Settings.Current;
        Toggle("Track screen time", "Counted locally per app; nothing leaves this PC.", s.TrackScreenTime, v => { s.TrackScreenTime = v; Settings.Save(); });
        Field("Data folder", H(Btn("Open folder", () => WindowTracker.Launch(Paths.Root)).Margin(0, 0, 8, 0),
            Btn("Open log", () => WindowTracker.Launch(Path.Combine(Paths.Dir("logs"), "vela.log")))), Paths.Root);
        Field("Taskbar", Btn("Restore Windows taskbar now", () => { s.TaskbarMode = "keep"; Settings.Save("taskbar"); Rebuild(); }),
            "If anything ever goes wrong: run  Vela.exe --restore-taskbar");
        Note("No telemetry, no account, no Vela server.");
    }

    void About()
    {
        using var p = Process.GetCurrentProcess();
        Note($"Vela {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)} · native WPF on .NET {Environment.Version.ToString(2)}");
        Field("Memory now", Text($"{Fmt.Bytes(p.WorkingSet64)} working set · {Fmt.Bytes(p.PrivateMemorySize64)} private", 13, FontWeights.SemiBold));
        Field("Command line", Text("Vela.exe --quit · --widgets · --settings · --restore-taskbar", 12, null, "Text.Secondary"));
    }

    static void RestartApp()
    {
        if (Environment.ProcessPath is { } exe) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Arguments = "--wait-for-exit" });
        Application.Current.Shutdown();
    }

    // ================================================================= building blocks

    static Button Btn(string text, Action a, bool accent = false) => PillButton(text, a, accent);

    void Section(string title) => _body.Children.Add(Text(title, 14, FontWeights.Bold).Margin(0, 16, 0, 10));

    void Note(string text)
    {
        var t = Text(text, 12, null, "Text.Secondary");
        t.TextWrapping = TextWrapping.Wrap;
        _body.Children.Add(t.Margin(0, 0, 0, 12));
    }

    void Field(string label, UIElement control, string? hint = null)
    {
        var panel = V(Text(label, 12.5, FontWeights.SemiBold, "Text.Secondary").Margin(0, 0, 0, 8), control);
        if (hint != null)
        {
            var h = Text(hint, 11.5, null, "Text.Tertiary");
            h.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(h.Margin(0, 6, 0, 0));
        }
        _body.Children.Add(Group(panel));
    }

    void Toggle(string label, string? hint, bool value, Action<bool> set)
    {
        var left = V(Text(label, 13.5, FontWeights.SemiBold));
        if (hint != null)
        {
            var h = Text(hint, 11.5, null, "Text.Secondary");
            h.TextWrapping = TextWrapping.Wrap;
            left.Children.Add(h.Margin(0, 2, 12, 0));
        }
        _body.Children.Add(Group(Columns("*,auto", left, Switch(value, set))));
    }

    void ConnectorRow(IConnector c)
    {
        var status = Text(c.Status, 11.5, null, c.Error != null ? "Sys.Orange" : c.IsConnected ? "Sys.Green" : "Text.Secondary");
        var head = Columns("*,auto", V(Text(c.Name, 13.5, FontWeights.SemiBold), status.Margin(0, 2, 0, 0)),
            c.Auth == ConnectorAuth.ApiKey && c.IsConnected
                ? Btn("Disconnect", () => { Secrets.Set(c.SecretName!, null); Rebuild(); })
                : c.HelpUrl != null ? Btn(c.Auth == ConnectorAuth.LocalSignIn ? "Setup guide" : "Get a key", () => WindowTracker.OpenUri(c.HelpUrl)) : new Border());
        var desc = Text(c.Description, 11.5, null, "Text.Tertiary");
        desc.TextWrapping = TextWrapping.Wrap;
        var panel = V(head, desc.Margin(0, 6, 0, 0));
        if (c.Auth == ConnectorAuth.ApiKey && !c.IsConnected)
        {
            var box = PasswordBox();
            var save = Btn("Connect", () => { Secrets.Set(c.SecretName!, box.Password); Rebuild(); }, true);
            panel.Children.Add(Columns("*,auto", box, save.Margin(8, 0, 0, 0)).Margin(0, 10, 0, 0));
        }
        _body.Children.Add(Group(panel));
    }

    static Border Group(UIElement child, double bottom = 8) =>
        new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, bottom), Child = child }.Bg("Tile.Background");

    static WrapPanel Wrap(IEnumerable<UIElement> items)
    {
        var w = new WrapPanel();
        foreach (var i in items) { if (i is FrameworkElement f && f.Margin == default) f.Margin = new Thickness(0, 0, 6, 6); w.Children.Add(i); }
        return w;
    }

    static FrameworkElement SliderRow(double value, double min, double max, string unit, Action<double> set)
    {
        var label = Text($"{value:0}{unit}", 12, FontWeights.SemiBold, "Text.Secondary");
        var slider = Slider(value, v => { label.Text = $"{v:0}{unit}"; });
        slider.Minimum = min; slider.Maximum = max;
        slider.Value = value;
        slider.PreviewMouseLeftButtonUp += (_, _) => set(slider.Value);
        slider.LostMouseCapture += (_, _) => set(slider.Value);
        return Columns("*,60", slider, label.Right());
    }
}
