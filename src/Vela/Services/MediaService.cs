using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vela.Core;
using Windows.Media.Control;
using MediaSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using MediaManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace Vela.Services;

/// <summary>
/// "Now playing" for any app that reports to Windows (Spotify, browsers, Media Player...)
/// via GlobalSystemMediaTransportControls. Event-driven; no polling.
/// </summary>
public sealed class MediaService
{
    MediaManager? _manager;
    MediaSession? _session;
    string _artKey = "";
    bool _initStarted;

    public string? Title { get; private set; }
    public string? Artist { get; private set; }
    public string? AppId { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool HasSession => _session != null && !string.IsNullOrEmpty(Title);
    public ImageSource? Art { get; private set; }
    public TimeSpan Position { get; private set; }
    public TimeSpan Duration { get; private set; }
    public DateTime PositionUpdated { get; private set; }

    public event Action? Changed;

    public void EnsureStarted()
    {
        if (_initStarted) return;
        _initStarted = true;
        _ = InitAsync();
    }

    async Task InitAsync()
    {
        try
        {
            _manager = await MediaManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => Ui(() => Attach(_manager.GetCurrentSession()));
            Attach(_manager.GetCurrentSession());
        }
        catch (Exception ex) { Log.Error("Media manager unavailable", ex); }
    }

    void Attach(MediaSession? s)
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnProps;
            _session.PlaybackInfoChanged -= OnPlayback;
            _session.TimelinePropertiesChanged -= OnTimeline;
        }
        _session = s;
        if (s == null)
        {
            Title = Artist = AppId = null; IsPlaying = false; Art = null; _artKey = "";
            Changed?.Invoke();
            return;
        }
        s.MediaPropertiesChanged += OnProps;
        s.PlaybackInfoChanged += OnPlayback;
        s.TimelinePropertiesChanged += OnTimeline;
        AppId = s.SourceAppUserModelId;
        UpdatePlayback();
        UpdateTimeline();
        _ = UpdatePropsAsync();
    }

    void OnProps(MediaSession s, MediaPropertiesChangedEventArgs e) => Ui(() => _ = UpdatePropsAsync());
    void OnPlayback(MediaSession s, PlaybackInfoChangedEventArgs e) => Ui(() => { UpdatePlayback(); Changed?.Invoke(); });
    void OnTimeline(MediaSession s, TimelinePropertiesChangedEventArgs e) => Ui(() => { UpdateTimeline(); Changed?.Invoke(); });

    void UpdatePlayback()
    {
        try { IsPlaying = _session?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
        catch { IsPlaying = false; }
    }

    void UpdateTimeline()
    {
        try
        {
            var t = _session?.GetTimelineProperties();
            if (t == null) return;
            Position = t.Position;
            Duration = t.EndTime - t.StartTime;
            PositionUpdated = DateTime.Now;
        }
        catch { }
    }

    async Task UpdatePropsAsync()
    {
        var s = _session;
        if (s == null) return;
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (s != _session || p == null) return;
            Title = p.Title;
            Artist = string.IsNullOrEmpty(p.Artist) ? p.AlbumArtist : p.Artist;
            var key = $"{Title}|{Artist}";
            if (key != _artKey)
            {
                _artKey = key;
                Art = await LoadArtAsync(p.Thumbnail);
            }
            Changed?.Invoke();
        }
        catch (Exception ex) { Log.Warn("Media props failed: " + ex.Message); }
    }

    static async Task<ImageSource?> LoadArtAsync(Windows.Storage.Streams.IRandomAccessStreamReference? thumb)
    {
        if (thumb == null) return null;
        try
        {
            using var ras = await thumb.OpenReadAsync();
            using var stream = ras.AsStream();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 160; // keep memory small
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public TimeSpan EstimatedPosition =>
        IsPlaying ? Position + (DateTime.Now - PositionUpdated) : Position;

    public async void TogglePlayPause() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
    public async void Next() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }

    static void Ui(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);
}
