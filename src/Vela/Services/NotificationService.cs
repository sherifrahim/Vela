using System.Windows;
using Vela.Core;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Vela.Services;

/// <summary>One notification in Action Center.</summary>
public sealed record ToastItem(uint Id, string App, string Aumid, string Title, string Body, DateTime When);

/// <summary>
/// Notifications waiting in Action Center, through Windows' notification listener (Settings → Privacy &amp; security →
/// Notifications must allow Vela; Windows asks once). Read-only except for an explicit "Clear". Powers app badges
/// (unread counts on dock icons), the Notifications tile and the WhatsApp tile.
/// Listens for Windows change events (or polls every 10 s where they are unavailable) only while something is
/// subscribed; nothing leaves this PC.
/// </summary>
public sealed class NotificationService
{
    UserNotificationListener? _listener;
    Action? _changed;
    DateTime _last;
    bool _busy, _asked, _again;
    // Windows' change event when it works for this process; otherwise a slow poll. Either way, a safety re-read.
    bool _event;
    int PollSeconds => _event ? 120 : 10;
    // Parsed notifications by id: a refresh only reads the texts of notifications it hasn't seen yet.
    Dictionary<uint, ToastItem> _cache = new();

    public IReadOnlyList<ToastItem> Items { get; private set; } = Array.Empty<ToastItem>();
    public UserNotificationListenerAccessStatus Access { get; private set; } = UserNotificationListenerAccessStatus.Unspecified;
    public bool Available => Access == UserNotificationListenerAccessStatus.Allowed;

    public event Action? Changed
    {
        add { bool first = _changed == null; _changed += value; if (first) { Svc.Ticker.Tick += OnTick; _ = RefreshAsync(); } }
        remove
        {
            _changed -= value;
            if (_changed != null) return;
            Svc.Ticker.Tick -= OnTick;
            if (_event && _listener != null) { try { _listener.NotificationChanged -= OnNative; } catch { } _event = false; }
        }
    }

    void OnTick(DateTime now)
    {
        if ((now - _last).TotalSeconds >= PollSeconds) _ = RefreshAsync();
    }

    void OnNative(UserNotificationListener sender, Windows.UI.Notifications.UserNotificationChangedEventArgs args) =>
        Application.Current?.Dispatcher.BeginInvoke(() => { if (_changed != null) _ = RefreshAsync(); });

    void TryEvent()
    {
        if (_event || _listener == null) return;
        try { _listener.NotificationChanged += OnNative; _event = true; Log.Info("Notifications: change events on"); }
        catch (Exception ex) { _event = false; Log.Info("Notifications: no change events here (" + ex.Message + "); polling"); }
    }

    public async Task RefreshAsync()
    {
        if (_busy) { _again = true; return; } // a change arrived mid-read: read once more afterwards
        _busy = true;
        _last = DateTime.Now;
        try
        {
            _listener ??= UserNotificationListener.Current;
            Access = _listener.GetAccessStatus();
            if (Access == UserNotificationListenerAccessStatus.Unspecified && !_asked)
            {
                _asked = true; // Windows shows its consent prompt once
                Access = await _listener.RequestAccessAsync();
            }
            if (!Available) { SetItems(Array.Empty<ToastItem>()); return; }
            TryEvent();
            var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var items = new List<ToastItem>(list.Count);
            var cache = new Dictionary<uint, ToastItem>(list.Count);
            foreach (var n in list)
            {
                if (_cache.TryGetValue(n.Id, out var known)) { items.Add(known); cache[n.Id] = known; continue; }
                string app = "", aumid = "";
                try { app = n.AppInfo?.DisplayInfo?.DisplayName ?? ""; aumid = n.AppInfo?.AppUserModelId ?? ""; } catch { }
                string title = "", body = "";
                try
                {
                    var texts = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
                    if (texts != null && texts.Count > 0)
                    {
                        title = texts[0].Text ?? "";
                        body = string.Join(" ", texts.Skip(1).Select(t => t.Text)).Trim();
                    }
                }
                catch { }
                var item = new ToastItem(n.Id, app, aumid, title, body, n.CreationTime.LocalDateTime);
                items.Add(item);
                cache[n.Id] = item;
            }
            _cache = cache;
            SetItems(items.OrderByDescending(i => i.When).ToList());
        }
        catch (Exception ex)
        {
            Log.Warn("Notifications: " + ex.Message);
        }
        finally
        {
            _busy = false;
            if (_again) { _again = false; _ = RefreshAsync(); }
        }
    }

    void SetItems(IReadOnlyList<ToastItem> items)
    {
        // Only tell listeners when something actually changed (ids differ).
        if (items.Count == Items.Count && items.Select(i => i.Id).SequenceEqual(Items.Select(i => i.Id))) return;
        Items = items;
        Application.Current?.Dispatcher.BeginInvoke(() => _changed?.Invoke());
    }

    /// <summary>Notifications for an app, matched by its AppUserModelID or, failing that, its name.</summary>
    public int CountFor(string? aumid, string? name, string? exePath = null)
    {
        if (Items.Count == 0) return 0;
        return Items.Count(i =>
            (!string.IsNullOrEmpty(aumid) && i.Aumid.Equals(aumid, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(exePath) && FromPackage(i.Aumid, exePath)) ||
            (!string.IsNullOrEmpty(name) && Matches(i.App, name)));
    }

    /// <summary>
    /// Packaged apps notify as "Name_PublisherId!App" while their windows run from
    /// …\WindowsApps\Name_Version_Arch__PublisherId\…, so match the package name and publisher id in the exe path.
    /// </summary>
    public static bool FromPackage(string aumid, string exePath)
    {
        int bang = aumid.IndexOf('!');
        if (bang <= 0) return false;
        int us = aumid.LastIndexOf('_', bang);
        if (us <= 0) return false;
        string pkg = aumid[..us], publisher = aumid[(us + 1)..bang];
        return exePath.Contains(@"\WindowsApps\" + pkg + "_", StringComparison.OrdinalIgnoreCase)
            && exePath.Contains("__" + publisher, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"WhatsApp" vs "WhatsApp Beta", "Discord" vs "Discord PTB": compare on the first word.</summary>
    static bool Matches(string appName, string name)
    {
        if (appName.Length == 0) return false;
        if (appName.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        static string First(string s) => s.Split(' ', 2)[0];
        return First(appName).Length >= 4 && First(appName).Equals(First(name), StringComparison.OrdinalIgnoreCase);
    }

    public void Remove(uint id)
    {
        try { _listener?.RemoveNotification(id); } catch (Exception ex) { Log.Warn("Remove notification: " + ex.Message); }
        _ = RefreshAsync();
    }

    public void ClearAll()
    {
        try { _listener?.ClearNotifications(); } catch (Exception ex) { Log.Warn("Clear notifications: " + ex.Message); }
        _ = RefreshAsync();
    }
}
