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
using static Vela.UI.UI;

namespace Vela.Views;

/// <summary>Settings: Appearance, Dock, Weather, Connectors, Privacy & data, About.</summary>
public sealed class SettingsWindow : GlassWindow
{
    readonly StackPanel _body = new();

    public SettingsWindow()
    {
        Title = "Vela settings";
        Topmost = true;
        Width = 600; Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RegionRadius = 26;

        var root = new Border { CornerRadius = new CornerRadius(26), BorderThickness = new Thickness(1), ClipToBounds = true };
        root.Bg("Flyout.Background");
        root.SetResourceReference(Border.BorderBrushProperty, "Flyout.Border");
        RegionElement = root;

        var header = Columns("*,auto", Text("Settings", 22, FontWeights.Bold), CircleButton(Glyphs.Close, Close, 32, "Fill.Control", "Text.Primary", 11));
        header.Margin = new Thickness(0, 0, 0, 10);
        header.Background = Brushes.Transparent;
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is Grid or TextBlock) DragMove(); };
        var scroll = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 10, 0) };
        var dock = new DockPanel { Margin = new Thickness(26, 22, 16, 22) };
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(scroll);
        root.Child = dock;
        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Build();
    }

    void Rebuild() => Dispatcher.BeginInvoke(Build, System.Windows.Threading.DispatcherPriority.Background);

    void Build()
    {
        _body.Children.Clear();
        var s = Settings.Current;

        // ---------------- Appearance
        Section("Appearance");
        Field("Style", Wrap(Palettes.All.Select(p => Segment(p.Name, "theme", s.Theme == p.Id, () => { s.Theme = p.Id; Settings.Save("theme"); }))));
        Field("Widget tiles", Wrap(new[] { ("auto", "Match style"), ("black", "Black"), ("white", "White"), ("glass", "Glass") }
            .Select(x => Segment(x.Item2, "tiles", s.WidgetStyle == x.Item1, () => { s.WidgetStyle = x.Item1; Settings.Save("theme"); }))));
        Field("Size", Wrap(new[] { ("S", "Small"), ("M", "Medium"), ("L", "Large") }
            .Select(x => Segment(x.Item2, "size", s.Size == x.Item1, () => { s.Size = x.Item1; Settings.Save("size"); }))));
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

        // ---------------- Dock
        Section("Dock");
        Field("Position", Wrap(new[] { ("bottom", "Bottom"), ("top", "Top") }
            .Select(x => Segment(x.Item2, "edge", s.Edge == x.Item1, () => { s.Edge = x.Item1; Settings.Save("dock"); }))),
            "You can also drag the dock to the top or bottom of the screen.");
        Field("Windows taskbar", Wrap(new[] { ("hidden", "Hidden"), ("autohide", "Auto-hide"), ("keep", "Leave as is") }
            .Select(x => Segment(x.Item2, "taskbar", s.TaskbarMode == x.Item1, () => { s.TaskbarMode = x.Item1; Settings.Save("taskbar"); }))),
            "Restored exactly as it was whenever Vela quits.");
        Field("Distance from edge", SliderRow(s.EdgeMargin, 0, 24, v => { s.EdgeMargin = Math.Round(v); Settings.Save("dock"); }));
        Field("Spacing between widgets", SliderRow(s.Spacing, 0, 24, v => { s.Spacing = Math.Round(v); Settings.Save("dock"); }));
        Toggle("Reserve screen space", "Maximized windows stop above the dock.", s.ReserveSpace, v => { s.ReserveSpace = v; Settings.Save("dock"); });
        Toggle("Show app names on hover", null, s.ShowAppNames, v => { s.ShowAppNames = v; Settings.Save(); });
        Toggle("24-hour clock", null, s.Use24Hour, v => { s.Use24Hour = v; Settings.Save("clock"); });
        Toggle("Launch at sign-in", null, Settings.LaunchAtStartup, v => Settings.LaunchAtStartup = v);

        // ---------------- Weather
        Section("Weather");
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
        Field("City", V(Columns("*,auto", cityBox, PillButton("Find", Search).Margin(8, 0, 0, 0)), results.Margin(0, 6, 0, 0)),
            s.Weather.City != null ? $"Current: {s.Weather.City}. Only these coordinates are sent to open-meteo.com." : "Weather uses open-meteo.com (no account).");
        Field("Units", Wrap(new[] { ("C", "°C"), ("F", "°F") }
            .Select(x => Segment(x.Item2, "unit", s.Weather.Unit == x.Item1, () => { s.Weather.Unit = x.Item1; Settings.Save("weather"); }))));

        // ---------------- Connectors
        Section("Connectors");
        Note("Each connector talks straight from this PC to its service. Keys are encrypted with Windows DPAPI and never written to settings.json.");
        foreach (var c in Svc.AllConnectors) ConnectorRow(c);

        // ---------------- Privacy & data
        Section("Privacy & data");
        Toggle("Track screen time", "Counted locally per app; nothing leaves this PC.", s.TrackScreenTime, v => { s.TrackScreenTime = v; Settings.Save(); });
        Field("Data folder", H(PillButton("Open folder", () => WindowTracker.Launch(Paths.Root)).Margin(0, 0, 8, 0),
            PillButton("Open log", () => WindowTracker.Launch(Path.Combine(Paths.Dir("logs"), "vela.log")))), Paths.Root);
        Field("Taskbar", PillButton("Restore Windows taskbar now", () => { s.TaskbarMode = "keep"; Settings.Save("taskbar"); Rebuild(); }),
            "If anything ever goes wrong: run  Vela.exe --restore-taskbar");
        Field("Layout", PillButton("Reset dock to default widgets", () => { s.Widgets = Settings.DefaultLayout(); Settings.SaveNow(); RestartApp(); }));

        // ---------------- About
        Section("About");
        using var p = Process.GetCurrentProcess();
        Note($"Vela {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)} · native WPF on .NET {Environment.Version.ToString(2)}\n" +
             $"Memory now: {Fmt.Bytes(p.WorkingSet64)} working set · {Fmt.Bytes(p.PrivateMemorySize64)} private");
    }

    static void RestartApp()
    {
        if (Environment.ProcessPath is { } exe) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Arguments = "--wait-for-exit" });
        Application.Current.Shutdown();
    }

    // ---------------- building blocks

    void Section(string title) => _body.Children.Add(Text(title, 15, FontWeights.Bold).Margin(0, 18, 0, 10));

    void Note(string text)
    {
        var t = Text(text, 12, null, "Text.Secondary");
        t.TextWrapping = TextWrapping.Wrap;
        _body.Children.Add(t.Margin(0, 0, 0, 12));
    }

    void Field(string label, UIElement control, string? hint = null)
    {
        var panel = V(Text(label, 12.5, FontWeights.SemiBold, "Text.Secondary").Margin(0, 0, 0, 7), control);
        if (hint != null)
        {
            var h = Text(hint, 11.5, null, "Text.Tertiary");
            h.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(h.Margin(0, 4, 0, 0));
        }
        _body.Children.Add(Group(panel));
    }

    void Toggle(string label, string? hint, bool value, Action<bool> set)
    {
        var left = V(Text(label, 13.5, FontWeights.SemiBold));
        if (hint != null) left.Children.Add(Text(hint, 11.5, null, "Text.Secondary").Margin(0, 2, 0, 0));
        _body.Children.Add(Group(Columns("*,auto", left, Switch(value, set))));
    }

    void ConnectorRow(IConnector c)
    {
        var status = Text(c.Status, 11.5, null, c.Error != null ? "Sys.Orange" : c.IsConnected ? "Sys.Green" : "Text.Secondary");
        var head = Columns("*,auto", V(Text(c.Name, 13.5, FontWeights.SemiBold), status.Margin(0, 2, 0, 0)),
            c.Auth == ConnectorAuth.ApiKey && c.IsConnected
                ? PillButton("Disconnect", () => { Secrets.Set(c.SecretName!, null); Rebuild(); })
                : c.HelpUrl != null ? PillButton(c.Auth == ConnectorAuth.LocalSignIn ? "Setup guide" : "Get a key", () => WindowTracker.OpenUri(c.HelpUrl)) : new Border());
        var desc = Text(c.Description, 11.5, null, "Text.Tertiary");
        desc.TextWrapping = TextWrapping.Wrap;
        var panel = V(head, desc.Margin(0, 6, 0, 0));
        if (c.Auth == ConnectorAuth.ApiKey && !c.IsConnected)
        {
            var box = PasswordBox();
            var save = PillButton("Connect", () => { Secrets.Set(c.SecretName!, box.Password); Rebuild(); }, accent: true);
            panel.Children.Add(Columns("*,auto", box, save.Margin(8, 0, 0, 0)).Margin(0, 10, 0, 0));
        }
        _body.Children.Add(Group(panel));
    }

    static Border Group(UIElement child) =>
        new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 8), Child = child }.Bg("Fill.Control").WithOpacityBg();

    static WrapPanel Wrap(IEnumerable<UIElement> items)
    {
        var w = new WrapPanel();
        foreach (var i in items) w.Children.Add(i);
        return w;
    }

    static FrameworkElement SliderRow(double value, double min, double max, Action<double> set)
    {
        var label = Text($"{value:0} px", 12, FontWeights.SemiBold, "Text.Secondary");
        var slider = Slider(value, v => { label.Text = $"{v:0} px"; });
        slider.Minimum = min; slider.Maximum = max;
        slider.Value = value;
        slider.PreviewMouseLeftButtonUp += (_, _) => set(slider.Value);
        slider.LostMouseCapture += (_, _) => set(slider.Value);
        return Columns("*,60", slider, label.Right());
    }
}

static class SettingsExt
{
    /// <summary>Settings groups sit on a softer fill than controls inside them.</summary>
    public static Border WithOpacityBg(this Border b)
    {
        b.Opacity = 1;
        b.SetResourceReference(Border.BackgroundProperty, "Tile.Background");
        return b;
    }
}
