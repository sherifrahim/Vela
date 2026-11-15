using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vela.Core;
using Vela.Services;
using Vela.Shell;
using Vela.UI;
using Windows.Devices.Radios;
using static Vela.UI.UI;

namespace Vela.Views;

/// <summary>Shared pieces for the Wi-Fi / Bluetooth / Control Centre cards.</summary>
static class QuickUi
{
    /// <summary>Title row with an on/off switch on the right (Windows-style panel header).</summary>
    public static FrameworkElement SwitchHeader(string title, bool on, Action<bool> set, Action? back = null)
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        if (back != null) left.Children.Add(CircleButton(Glyphs.ChevronLeft, back, 26, "Fill.Control", "Text.Primary", 9).Margin(0, 0, 10, 0));
        left.Children.Add(Text(title, 15, FontWeights.Bold));
        var g = Columns("*,auto", left, Switch(on, set));
        g.Margin = new Thickness(0, 0, 0, 12);
        return g;
    }

    /// <summary>A hoverable list row: round icon, title + subtitle, optional trailing element.</summary>
    public static Button Row(string glyph, string title, string sub, UIElement? trailing, Action? onClick, bool active = false)
    {
        var icon = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Child = Icon(glyph, 12, active ? "OnAccent" : "Text.Primary") }
            .Bg(active ? "Accent" : "Fill.Control");
        var t = Text(title, 12.5, FontWeights.SemiBold);
        var s = Text(sub, 10.5, null, active ? "Accent" : "Text.Secondary");
        var body = Columns("auto,*,auto", icon, V(t, s).Margin(10, 0, 8, 0), trailing ?? new Border());
        var b = GhostButton(body, onClick ?? (() => { }));
        b.Margin = new Thickness(-8, 0, -8, 2);
        if (onClick == null) b.Cursor = Cursors.Arrow;
        return b;
    }

    public static Border Section(string text) => new() { Child = Label(text), Margin = new Thickness(0, 10, 0, 6) };

    public static FrameworkElement Footer(string linkText, Action link, UIElement? right = null)
    {
        var a = GhostButton(Text(linkText, 11.5, FontWeights.SemiBold, "Accent"), link);
        a.HorizontalAlignment = HorizontalAlignment.Left;
        a.Padding = new Thickness(0, 4, 6, 4);
        var g = Columns("*,auto", a, right ?? new Border());
        g.Margin = new Thickness(0, 10, 0, 0);
        return g;
    }

    public static ScrollViewer Scroll(UIElement content, double max = 300) =>
        new() { Content = content, MaxHeight = max, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, -6, 0), Padding = new Thickness(0, 0, 6, 0) };
}

/// <summary>Wi-Fi panel: radio switch, networks (connected first), connect with password, disconnect.</summary>
public static class WifiCard
{
    static string Bars(int b) => b switch { <= 1 => "", 2 => "", 3 => "", _ => "" };

    public static UIElement Build(Action? back = null)
    {
        var list = new StackPanel();
        var status = Text("Scanning…", 11, null, "Text.Secondary").Margin(0, 0, 0, 6);
        status.TextWrapping = TextWrapping.Wrap;
        bool radioOn = Svc.Radios.IsOn(RadioKind.WiFi) ?? true;
        string? expanded = null;
        IReadOnlyList<WifiNetwork> nets = Array.Empty<WifiNetwork>();

        async void Load()
        {
            if (!(Svc.Radios.IsOn(RadioKind.WiFi) ?? true)) { list.Children.Clear(); status.Text = "Wi-Fi is off."; return; }
            status.Text = "Scanning…";
            nets = await Svc.Wifi.ScanAsync();
            Render();
        }

        void Render()
        {
            list.Children.Clear();
            status.Text = Svc.Wifi.Problem ?? "";
            status.Visibility = status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var n in nets)
            {
                var net = n;
                string sub = n.Connected ? (n.Secured ? "Connected, secured" : "Connected") : n.Secured ? "Secured" : "Open";
                var lockIcon = n.Secured ? Icon("", 10, "Text.Tertiary") : null;
                list.Children.Add(QuickUi.Row(Bars(n.Bars), n.Ssid, sub, lockIcon, () => { expanded = expanded == net.Ssid ? null : net.Ssid; Render(); }, n.Connected));
                if (expanded == n.Ssid) list.Children.Add(Expander(net));
            }
            if (Svc.Wifi.Problem?.Contains("location", StringComparison.OrdinalIgnoreCase) == true)
                list.Children.Add(PillButton("Open location settings", () => WinActions.OpenSettings("privacy-location")).Margin(0, 6, 0, 0));
        }

        FrameworkElement Expander(WifiNetwork n)
        {
            var panel = new StackPanel { Margin = new Thickness(40, 2, 0, 10) };
            var msg = Text("", 11, null, "Sys.Red");
            msg.TextWrapping = TextWrapping.Wrap;
            if (n.Connected)
            {
                panel.Children.Add(PillButton("Disconnect", async () => { await Svc.Wifi.DisconnectAsync(); Load(); }));
                return panel;
            }
            var pw = PasswordBox();
            pw.Visibility = Visibility.Collapsed;
            Button go = null!;
            go = PillButton("Connect", async () =>
            {
                go.IsEnabled = false; msg.Text = "Connecting…"; msg.Fg("Text.Secondary");
                var err = await Svc.Wifi.ConnectAsync(n, pw.Visibility == Visibility.Visible ? pw.Password : null);
                go.IsEnabled = true;
                if (err == null) { expanded = null; Load(); return; }
                // No saved profile for a secured network → ask for the key, then retry.
                if (n.Secured && pw.Visibility != Visibility.Visible) { pw.Visibility = Visibility.Visible; msg.Text = "Enter the network security key"; pw.Focus(); }
                else { msg.Text = err; msg.Fg("Sys.Red"); }
            }, accent: true);
            pw.KeyDown += (_, e) => { if (e.Key == Key.Enter) go.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            panel.Children.Add(pw.Margin(0, 0, 0, 6));
            panel.Children.Add(H(go));
            panel.Children.Add(msg.Margin(0, 6, 0, 0));
            return panel;
        }

        var refresh = CircleButton(Glyphs.Refresh, Load, 26, "Fill.Control", "Text.Secondary", 10).Tip("Scan again");
        var root = V(
            QuickUi.SwitchHeader("Wi-Fi", radioOn, async on => { await Svc.Radios.SetAsync(RadioKind.WiFi, on); await Task.Delay(on ? 2500 : 300); Load(); }, back),
            status, QuickUi.Scroll(list),
            QuickUi.Footer("More Wi-Fi settings", () => { FlyoutWindow.CloseCurrent(); WinActions.OpenSettings("network-wifi"); }, refresh));
        root.Loaded += (_, _) => Load();
        return root;
    }
}

/// <summary>Bluetooth panel: radio switch, your devices (connected first, remove), and live discovery + pairing.</summary>
public static class BluetoothCard
{
    public static UIElement Build(Action? back = null)
    {
        var mine = new StackPanel();
        var found = new StackPanel();
        var searching = Text("Looking for devices…", 11, null, "Text.Secondary").Margin(0, 0, 0, 4);
        var msg = Text("", 11, null, "Sys.Red");
        msg.TextWrapping = TextWrapping.Wrap;
        string? confirmRemove = null;

        async void LoadMine()
        {
            var devs = await Svc.Bluetooth.PairedAsync();
            mine.Children.Clear();
            foreach (var d in devs)
            {
                var dev = d;
                Button? remove = null;
                remove = PillButton(confirmRemove == d.Id ? "Remove?" : "Remove", async () =>
                {
                    if (confirmRemove != dev.Id) { confirmRemove = dev.Id; LoadMine(); return; }
                    confirmRemove = null;
                    msg.Text = await Svc.Bluetooth.UnpairAsync(dev) ?? "";
                    LoadMine();
                });
                remove.Padding = new Thickness(10, 3, 10, 4);
                remove.FontSize = 11;
                var sub = (d.Connected ? "Connected" : "Paired") + (d.Battery is { } pct ? $"  ·  battery {pct}%" : "");
                mine.Children.Add(QuickUi.Row(d.Glyph, d.Name, sub, remove, null, d.Connected));
            }
            if (devs.Count == 0) mine.Children.Add(Text("No paired devices yet.", 11.5, null, "Text.Secondary").Margin(0, 0, 0, 6));
        }

        void RenderFound()
        {
            found.Children.Clear();
            foreach (var d in Svc.Bluetooth.Discovered.OrderBy(d => d.Name).Take(12))
            {
                var dev = d;
                Button pair = null!;
                pair = PillButton("Pair", async () =>
                {
                    pair.IsEnabled = false; pair.Content = "Pairing…";
                    msg.Text = await Svc.Bluetooth.PairAsync(dev) ?? "";
                    LoadMine();
                }, accent: true);
                pair.Padding = new Thickness(12, 3, 12, 4);
                pair.FontSize = 11;
                found.Children.Add(QuickUi.Row(d.Glyph, d.Name, "Available", pair, null));
            }
            searching.Text = Svc.Bluetooth.Discovered.Count == 0 ? "Looking for devices… make sure yours is in pairing mode." : "New devices nearby";
        }

        bool on = Svc.Radios.IsOn(RadioKind.Bluetooth) ?? false;
        var root = V(
            QuickUi.SwitchHeader("Bluetooth", on, async v => { await Svc.Radios.SetAsync(RadioKind.Bluetooth, v); await Task.Delay(800); LoadMine(); if (v) Svc.Bluetooth.StartDiscovery(); else Svc.Bluetooth.StopDiscovery(); }, back),
            QuickUi.Section("Your devices"), QuickUi.Scroll(mine, 200),
            QuickUi.Section("Add a device"), searching, QuickUi.Scroll(found, 180), msg,
            QuickUi.Footer("More Bluetooth settings", () => { FlyoutWindow.CloseCurrent(); WinActions.OpenSettings("bluetooth"); }));
        void OnFound() => RenderFound();
        root.Loaded += (_, _) =>
        {
            LoadMine();
            Svc.Bluetooth.DiscoveryChanged += OnFound;
            if (Svc.Radios.IsOn(RadioKind.Bluetooth) ?? false) Svc.Bluetooth.StartDiscovery();
            RenderFound();
        };
        root.Unloaded += (_, _) => { Svc.Bluetooth.DiscoveryChanged -= OnFound; Svc.Bluetooth.StopDiscovery(); };
        return root;
    }
}

/// <summary>
/// Control Centre: the Windows Quick Settings, in Vela's style. Toggle tiles (Wi-Fi, Bluetooth, airplane,
/// dark mode, microphone, battery saver, night light, project, cast…), brightness + volume sliders, power mode,
/// and shortcuts to notifications / Windows Quick Settings / Settings.
/// </summary>
public static class ControlCenter
{
    public static UIElement Build()
    {
        _ = Svc.Radios.InitAsync();
        void Noop() { }
        Svc.Microphone.Changed += Noop; // starts the microphone endpoint so its tile knows the real state
        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(-4, 0, -4, 0) };
        UIElement? self = null;
        void Back() { if (self != null) FlyoutWindow.Navigate(self); }

        Border Tile(string glyph, string label, Func<bool?> state, Action onClick, Action? more = null, string? sub = null)
        {
            var icon = Icon(glyph, 14);
            var name = Text(label, 11, FontWeights.SemiBold);
            var detail = Text(sub ?? "", 9.5, null, "Text.Secondary");
            var stack = V(icon.Margin(0, 0, 0, 6), name, detail);
            stack.HorizontalAlignment = HorizontalAlignment.Left;
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            var b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 10, 8, 10), Margin = new Thickness(4), Cursor = Cursors.Hand, MinHeight = 74 };
            var g = new Grid();
            g.Children.Add(stack);
            if (more != null)
            {
                var chev = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Child = Icon(Glyphs.ChevronRight, 8), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, ToolTip = "More" };
                chev.MouseLeftButtonUp += (_, e) => { e.Handled = true; more(); };
                g.Children.Add(chev);
            }
            b.Child = g;
            void Paint()
            {
                bool on = state() == true;
                b.Bg(on ? "Accent" : "Fill.Control");
                foreach (var t in new[] { icon, name }) t.Fg(on ? "OnAccent" : "Text.Primary");
                detail.Fg(on ? "OnAccent" : "Text.Secondary");
                detail.Opacity = on ? 0.85 : 1;
            }
            Paint();
            b.MouseLeftButtonUp += async (_, e) => { e.Handled = true; onClick(); await Task.Delay(350); Paint(); };
            Svc.Radios.Changed += Paint;
            b.Unloaded += (_, _) => Svc.Radios.Changed -= Paint;
            return b;
        }

        tiles.Children.Add(Tile(Glyphs.Wifi, "Wi-Fi", () => Svc.Radios.IsOn(RadioKind.WiFi), () => _ = Svc.Radios.ToggleAsync(RadioKind.WiFi),
            () => FlyoutWindow.Navigate(WifiCard.Build(Back))));
        tiles.Children.Add(Tile(Glyphs.Bluetooth, "Bluetooth", () => Svc.Radios.IsOn(RadioKind.Bluetooth), () => _ = Svc.Radios.ToggleAsync(RadioKind.Bluetooth),
            () => FlyoutWindow.Navigate(BluetoothCard.Build(Back))));
        tiles.Children.Add(Tile(Glyphs.Airplane, "Airplane", () => Svc.Radios.AirplaneOn, () => _ = Svc.Radios.SetAirplaneAsync(!Svc.Radios.AirplaneOn)));
        tiles.Children.Add(Tile(Glyphs.Moon, "Dark mode", () => !WinActions.IsSystemLight(), () => WinActions.SetSystemLight(!WinActions.IsSystemLight())));
        tiles.Children.Add(Tile(Glyphs.Mic, "Microphone", () => Svc.Microphone.Available ? !Svc.Microphone.Muted : null, () => Svc.Microphone.ToggleMute(),
            sub: Svc.Microphone.Available ? null : "Not found"));
        tiles.Children.Add(Tile("", "Battery saver", () => Svc.Quick.BatterySaverOn, () => WinActions.OpenSettings("batterysaver"), sub: "Settings"));
        tiles.Children.Add(Tile(Glyphs.Brightness, "Night light", () => null, () => WinActions.OpenSettings("nightlight"), sub: "Settings"));
        tiles.Children.Add(Tile("", "Project", () => null, () => { FlyoutWindow.CloseCurrent(); WinActions.OpenProject(); }));
        tiles.Children.Add(Tile("", "Cast", () => null, () => { FlyoutWindow.CloseCurrent(); WinActions.OpenCast(); }));

        // Sliders
        var sliders = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        // Brightness comes from WMI (slow): read off the UI thread, show the row only if the panel supports it.
        var brightRow = new ContentControl { Focusable = false };
        sliders.Children.Add(brightRow);
        sliders.Loaded += async (_, _) =>
        {
            if (brightRow.Content != null) return;
            int bright = await Task.Run(() => Svc.Brightness.Read());
            if (bright < 0) return;
            var bs = Slider(bright, v => Svc.Brightness.Set((int)v));
            brightRow.Content = Columns("28,*", Icon(Glyphs.Brightness, 13, "Text.Secondary"), bs).Margin(0, 0, 0, 12);
        };
        var spk = Svc.Speakers;
        var vol = Slider(spk.Volume, v => spk.SetVolume(v));
        var muteBtn = CircleButton(spk.Muted ? Glyphs.Mute : Glyphs.Volume, () => spk.ToggleMute(), 24, "Transparent", "Text.Secondary", 12);
        sliders.Children.Add(Columns("28,*,auto", muteBtn, vol,
            CircleButton(Glyphs.ChevronRight, () => { FlyoutWindow.CloseCurrent(); WinActions.OpenSettings("sound"); }, 24, "Transparent", "Text.Secondary", 9).Margin(6, 0, 0, 0)));
        void SyncVol() { if (!vol.IsMouseCaptureWithin) vol.Value = spk.Volume; ((TextBlock)muteBtn.Content).Text = spk.Muted ? Glyphs.Mute : Glyphs.Volume; }
        spk.Changed += SyncVol;
        vol.Unloaded += (_, _) => spk.Changed -= SyncVol;

        // Power mode (Settings → Power & battery → Power mode)
        var power = new StackPanel();
        int pm = Svc.Quick.PowerMode;
        if (pm >= 0)
        {
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < QuickSettingsService.PowerModes.Length; i++)
            {
                int idx = i;
                seg.Children.Add(Segment(QuickSettingsService.PowerModes[i].Name, "powermode", pm == i, () => Svc.Quick.PowerMode = idx));
            }
            power.Children.Add(Label("Power mode").Margin(0, 14, 0, 6));
            power.Children.Add(seg);
        }

        // Footer: battery, notifications, Windows quick settings, settings
        var sys = Svc.Sys;
        sys.SampleBattery();
        var battery = Text(sys.HasBattery ? $"{sys.BatteryPercent}%{(sys.OnAc ? " · charging" : "")}" : "Plugged in", 11.5, FontWeights.SemiBold, "Text.Secondary");
        var foot = Columns("*,auto,auto,auto,auto", battery,
            CircleButton(Glyphs.Bell, () => { FlyoutWindow.CloseCurrent(); WinActions.OpenNotificationCenter(); }, 30, "Fill.Control", "Text.Primary", 12).Tip("Notifications").Margin(6, 0, 0, 0),
            CircleButton(Glyphs.Lock, () => { FlyoutWindow.CloseCurrent(); WinActions.LockPc(); }, 30, "Fill.Control", "Text.Primary", 12).Tip("Lock").Margin(6, 0, 0, 0),
            CircleButton(Glyphs.AllApps, () => { FlyoutWindow.CloseCurrent(); WinActions.OpenQuickSettings(); }, 30, "Fill.Control", "Text.Primary", 12).Tip("Windows Quick Settings").Margin(6, 0, 0, 0),
            CircleButton(Glyphs.Settings, () => { FlyoutWindow.CloseCurrent(); WinActions.OpenSettings(""); }, 30, "Fill.Control", "Text.Primary", 12).Tip("Windows Settings").Margin(6, 0, 0, 0));
        foot.Margin = new Thickness(0, 16, 0, 0);

        self = V(tiles, sliders, power, foot);
        tiles.Unloaded += (_, _) => Svc.Microphone.Changed -= Noop;
        return self;
    }
}
