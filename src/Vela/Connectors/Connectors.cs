using System.Windows;
using Vela.Core;
using Vela.Services;

namespace Vela.Connectors;

public enum ConnectorAuth { LocalSignIn, ApiKey }

public sealed record UsageWindow(string Label, double Percent, DateTimeOffset? ResetsAt);

/// <summary>
/// A connector links a widget to an outside service. Rules (see docs/ARCHITECTURE.md#connectors):
///  - talk directly from this PC to the service; never through a Vela server (there isn't one)
///  - read-only; never modify another app's files (e.g. Claude Code / Codex credentials)
///  - pasted keys go through <see cref="Secrets"/> (DPAPI), never plain settings.json
/// </summary>
public interface IConnector
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    ConnectorAuth Auth { get; }
    /// <summary>Secret name for ApiKey connectors.</summary>
    string? SecretName { get; }
    string KeyHint { get; }
    string? HelpUrl { get; }
    bool IsConnected { get; }
    string Status { get; }
    string? Error { get; }
    DateTime? LastUpdated { get; }
    event Action? Changed;
    Task RefreshAsync();
}

/// <summary>Base: polls on an interval only while something listens; marshals Changed to the UI thread.</summary>
public abstract class PollingConnector : IConnector
{
    DateTime _last;
    bool _busy;
    Action? _changed;

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract ConnectorAuth Auth { get; }
    public virtual string? SecretName => null;
    public virtual string KeyHint => "";
    public virtual string? HelpUrl => null;
    public abstract bool IsConnected { get; }
    public virtual string Status => Error ?? (IsConnected ? (LastUpdated is { } t ? $"Updated {t:t}" : "Connected") : "Not connected");
    public string? Error { get; protected set; }
    public DateTime? LastUpdated { get; protected set; }
    protected abstract TimeSpan Interval { get; }

    public event Action? Changed
    {
        add { bool first = _changed == null; _changed += value; if (first) { Svc.Ticker.Tick += OnTick; _ = RefreshAsync(); } }
        remove { _changed -= value; if (_changed == null) Svc.Ticker.Tick -= OnTick; }
    }

    protected PollingConnector()
    {
        Secrets.Changed += name => { if (name == SecretName) { _last = default; _ = RefreshAsync(); } };
    }

    void OnTick(DateTime now)
    {
        if (now - _last >= Interval) _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        _last = DateTime.Now;
        try
        {
            await FetchAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Error = ShortError(ex);
            Log.Warn($"{Name} refresh failed: {ex.Message}");
        }
        finally
        {
            _busy = false;
            Application.Current?.Dispatcher.BeginInvoke(() => _changed?.Invoke());
        }
    }

    /// <summary>Fetch and set state. Set Error = null on success. Runs on the UI thread's context.</summary>
    protected abstract Task FetchAsync();

    static string ShortError(Exception ex) => ex switch
    {
        System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } => "Sign-in expired or key rejected",
        System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } => "Key lacks permission",
        System.Net.Http.HttpRequestException => "Can't reach service",
        TaskCanceledException => "Timed out",
        _ => ex.Message.Length > 60 ? ex.Message[..60] : ex.Message,
    };
}
