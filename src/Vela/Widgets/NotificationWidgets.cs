using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vela.Core;
using Vela.Services;
using Vela.Shell;
using Vela.UI;
using Vela.Views;
using static Vela.UI.UI;

namespace Vela.Widgets;

/// <summary>Shared pieces for the notification tiles.</summary>
static class NoteUi
{
    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes}m" : d.TotalDays < 1 ? $"{(int)d.TotalHours}h" : $"{(int)d.TotalDays}d";
    }

    /// <summary>Shown when Windows hasn't allowed Vela to read notifications.</summary>
    public static FrameworkElement AccessHint()
    {
        var t = Text("Vela needs permission to read notifications. Turn on \"Allow apps to access your notifications\" and Vela in Windows Settings.", 12.5, null, "Text.Secondary");
        t.TextWrapping = TextWrapping.Wrap;
        var b = PillButton("Open notification privacy settings", () => { FlyoutWindow.CloseCurrent(); WinActions.OpenSettings("privacy-notifications"); }, accent: true);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        return V(t, b.Margin(0, 12, 0, 0));
    }

    public static FrameworkElement Row(ToastItem n, bool showApp, Action? open)
    {
        var head = Text(showApp ? $"{n.App} · {n.Title}" : n.Title, 12.5, FontWeights.SemiBold);
        var when = Text(Ago(n.When), 11, null, "Text.Tertiary").Margin(8, 0, 0, 0);
        var body = Text(n.Body, 12, null, "Text.Secondary");
        body.TextTrimming = TextTrimming.CharacterEllipsis;
        var dismiss = CircleButton(Glyphs.Close, () => Svc.Notifications.Remove(n.Id), 20, "Fill.Control", "Text.Secondary", 7);
        dismiss.ToolTip = "Dismiss";
        var text = V(Columns("*,auto", head, when), body.Margin(0, 2, 0, 0));
        var row = Columns("*,auto", text, dismiss.Margin(8, 0, 0, 0));
        if (open == null) return row.Margin(0, 0, 0, 10);
        var b = GhostButton(row, open);
        b.Padding = new Thickness(6, 5, 6, 5);
        b.Margin = new Thickness(-6, 0, -6, 4);
        return b;
    }
}

/// <summary>Bell with the number of notifications waiting; card lists them (dismiss one, or clear all).</summary>
public sealed class NotificationsWidget : WidgetBase
{
    Border _badge = null!;
    TextBlock _count = null!;

    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        _count = new TextBlock { FontSize = 7.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _badge = new Border { MinWidth = 13, Height = 13, CornerRadius = new CornerRadius(6.5), Padding = new Thickness(3.5, 0, 3.5, 0), Child = _count,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 7, 0), Visibility = Visibility.Collapsed }.Bg("Sys.Red");
        var g = new Grid();
        g.Children.Add(Icon(Glyphs.Bell, 14).Center());
        g.Children.Add(_badge);
        return g;
    }

    protected override void OnAttach() { Svc.Notifications.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Notifications.Changed -= Render;

    void Render()
    {
        int n = Svc.Notifications.Items.Count;
        _badge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        _count.Text = n > 99 ? "99+" : n.ToString();
        ToolTip = !Svc.Notifications.Available && Svc.Notifications.Access != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Unspecified
            ? "Notifications — allow access in Windows Settings" : n == 0 ? "No notifications" : $"{n} notification{(n == 1 ? "" : "s")}";
        RefreshCardIfOpen();
    }

    void RefreshCardIfOpen() { if (CardOpen) FlyoutWindow.Navigate(BuildCard()); }

    protected override double CardWidth => 360;
    protected override UIElement BuildCard()
    {
        var s = Svc.Notifications;
        var header = Card.Header("Notifications", null, s.Items.Count > 0 ? s.Items.Count.ToString() : null);
        if (!s.Available) return V(header, NoteUi.AccessHint());
        var list = new StackPanel();
        foreach (var n in s.Items.Take(12)) list.Children.Add(NoteUi.Row(n, true, null));
        if (s.Items.Count == 0) list.Children.Add(Text("You're all caught up.", 12.5, null, "Text.Secondary").Margin(0, 0, 0, 8));
        var scroll = new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var footer = Columns("*,auto,auto",
            new Border(),
            GhostButton(Text("Notification Center", 12, FontWeights.SemiBold, "Accent"), () => { FlyoutWindow.CloseCurrent(); WinActions.OpenNotificationCenter(); }).Margin(0, 0, 10, 0),
            PillButton("Clear all", () => s.ClearAll()));
        footer.Margin = new Thickness(0, 8, 0, 0);
        return V(header, scroll, footer);
    }

    protected override void AddMenuItems(ContextMenu menu)
    {
        menu.Items.Add(Item("Notification Center", WinActions.OpenNotificationCenter));
        menu.Items.Add(Item("Clear all", () => Svc.Notifications.ClearAll()));
        menu.Items.Add(Item("Notification settings", () => WinActions.OpenSettings("notifications")));
    }
}

/// <summary>WhatsApp from its notifications (reference): unread chats and the latest message. Click opens WhatsApp.</summary>
public sealed class WhatsAppWidget : WidgetBase
{
    const string Chat = "";
    static readonly Brush Green = Brush("#25D366");
    TextBlock _value = null!, _sub = null!;

    protected override UIElement BuildContent()
    {
        _value = Text("", 12.5, FontWeights.SemiBold);
        _sub = Text("", 7, FontWeights.SemiBold, "Text.Secondary");
        _sub.MaxWidth = 130;
        var icon = Icon(Chat, 12).Center();
        icon.Foreground = Brushes.White;
        return H(Badge(icon, Green, 24, 12), V(_value, _sub.Margin(0, -1, 0, 0)).Margin(9, 0, 3, 0));
    }

    protected override void OnAttach() { Svc.Notifications.Changed += Render; Render(); }
    protected override void OnDetach() => Svc.Notifications.Changed -= Render;

    static List<ToastItem> Messages => Svc.Notifications.Items.Where(i => i.App.StartsWith("WhatsApp", StringComparison.OrdinalIgnoreCase)).ToList();

    void Render()
    {
        if (!Svc.Notifications.Available && Svc.Notifications.Access != Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Unspecified)
        { _value.Text = "WhatsApp"; _sub.Text = "allow notification access"; return; }
        var msgs = Messages;
        int chats = msgs.Select(m => m.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _value.Text = chats == 0 ? "No new messages" : $"{chats} unread chat{(chats == 1 ? "" : "s")}";
        var latest = msgs.FirstOrDefault();
        _sub.Text = latest == null ? "WhatsApp" : $"{latest.Title}: {latest.Body}";
        if (CardOpen) FlyoutWindow.Navigate(BuildCard());
    }

    static void OpenApp() { FlyoutWindow.CloseCurrent(); WindowTracker.OpenUri("whatsapp:"); }
    protected override void OnClick() { if (Messages.Count == 0) OpenApp(); else OpenCard(); }

    protected override double CardWidth => 360;
    protected override UIElement BuildCard()
    {
        var header = Card.Header("WhatsApp", Badge(new TextBlock { Text = Chat, FontFamily = Icon(Chat).FontFamily, Foreground = Brushes.White, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, Green, 28, 14));
        if (!Svc.Notifications.Available) return V(header, NoteUi.AccessHint());
        var list = new StackPanel();
        foreach (var m in Messages.Take(10)) list.Children.Add(NoteUi.Row(m, false, OpenApp));
        if (list.Children.Count == 0) list.Children.Add(Text("No new messages.", 12.5, null, "Text.Secondary").Margin(0, 0, 0, 8));
        return V(header, list, Card.Footer("From WhatsApp's notifications on this PC.", "Open WhatsApp", OpenApp));
    }
}

/// <summary>
/// Background apps' tray icons (the notification area / hidden-icons flyout), reference "Tray" tile. Click: a grid of
/// icons; left-click, double-click and right-click go to each app exactly as from the Windows tray.
/// </summary>
public sealed class TrayWidget : WidgetBase
{
    TextBlock _count = null!;

    protected override UIElement BuildContent()
    {
        Tile.Width = TileHeight;
        Tile.Padding = new Thickness(0);
        _count = Text("", 7, FontWeights.Bold, "Text.Secondary");
        _count.HorizontalAlignment = HorizontalAlignment.Right;
        _count.VerticalAlignment = VerticalAlignment.Bottom;
        _count.Margin = new Thickness(0, 0, 7, 5);
        var g = new Grid();
        g.Children.Add(Icon(Glyphs.AllApps, 14).Center());
        g.Children.Add(_count);
        ToolTip = "Background apps";
        return g;
    }

    // Previews (picker, --render) must not take over the tray.
    // The tray host runs while a Tray tile is on a dock and Settings.TrayHost (kill switch, on by default) allows it.
    bool Live => !IsPreview && Settings.Current.TrayHost;
    bool _hosting;
    protected override void OnAttach() { Settings.Changed += OnSettings; Sync(); }
    protected override void OnDetach() { Settings.Changed -= OnSettings; if (_hosting) { Svc.Tray.Changed -= Render; _hosting = false; } }
    void OnSettings(string what) { if (what == "pins") Sync(); }

    /// <summary>Start / stop listening (and with it the tray host) when the Settings kill switch changes.</summary>
    void Sync()
    {
        if (Live && !_hosting) { Svc.Tray.Changed += Render; _hosting = true; }
        else if (!Live && _hosting) { Svc.Tray.Changed -= Render; _hosting = false; }
        Render();
    }

    void Render()
    {
        int n = _hosting ? Svc.Tray.Icons.Count : 0;
        _count.Text = n > 0 ? n.ToString() : "";
        if (CardOpen) FlyoutWindow.Navigate(BuildCard());
    }

    protected override double CardWidth => 300;
    protected override UIElement BuildCard()
    {
        var icons = Live ? Svc.Tray.Icons.ToList() : new List<TrayIcon>();
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 5, Margin = new Thickness(-4, 0, -4, 0) };
        foreach (var icon in icons)
        {
            var i = icon;
            var img = new Image { Source = i.Image, Width = 20, Height = 20 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var cell = new Border { Height = 44, CornerRadius = new CornerRadius(10), Child = img, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = string.IsNullOrWhiteSpace(i.Tip) ? null : i.Tip };
            cell.MouseEnter += (_, _) => cell.Bg("Fill.Control");
            cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
            // Left: a click per press (a second press within the double-click time sends the double-click), like the Windows
            // tray. The card stays open; it closes by itself when the app brings up its window or menu.
            cell.MouseLeftButtonDown += (_, e) => { e.Handled = true; i.Click(right: false, twice: e.ClickCount >= 2); };
            cell.MouseRightButtonUp += (_, e) => { e.Handled = true; i.Click(right: true); };
            grid.Children.Add(cell);
        }
        var msg = Live ? "No background apps right now." : IsPreview ? "Background apps appear here." : "Tray icons are switched off (Settings → Dock → Background apps).";
        var none = Text(msg, 12.5, null, "Text.Secondary");
        none.TextWrapping = TextWrapping.Wrap;
        var body = icons.Count > 0 ? (UIElement)grid : none;
        return V(Card.Header("Background apps", null, icons.Count > 0 ? icons.Count.ToString() : null), body);
    }
}
