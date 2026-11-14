using Windows.Media.Control;

namespace SpotifyLyricsOverlay;

public sealed class WindowsMediaClient
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task InitializeAsync()
    {
        _manager =
            await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    }

    public async Task<MediaPlayback?> GetCurrentPlaybackAsync()
    {
        if (_manager is null)
            await InitializeAsync();

        var session = GetPreferredSession();
        if (session is null)
            return null;

        var properties = await session.TryGetMediaPropertiesAsync();
        var timeline = session.GetTimelineProperties();
        var playbackInfo = session.GetPlaybackInfo();

        var title = properties.Title?.Trim() ?? "";
        var artist = properties.Artist?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(title))
            return null;

        var position = timeline.Position;

        if (playbackInfo.PlaybackStatus ==
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            position += DateTimeOffset.Now - timeline.LastUpdatedTime;
        }

        if (timeline.EndTime > TimeSpan.Zero && position > timeline.EndTime)
            position = timeline.EndTime;

        if (position < TimeSpan.Zero)
            position = TimeSpan.Zero;

        var duration = timeline.EndTime > TimeSpan.Zero
            ? timeline.EndTime
            : TimeSpan.Zero;

        return new MediaPlayback(
            CreateTrackId(session.SourceAppUserModelId, title, artist, duration),
            title,
            artist,
            properties.AlbumTitle?.Trim() ?? "",
            (long)position.TotalMilliseconds,
            (long)duration.TotalMilliseconds,
            playbackInfo.PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            session.SourceAppUserModelId);
    }

    private GlobalSystemMediaTransportControlsSession? GetPreferredSession()
    {
        if (_manager is null)
            return null;

        var sessions = _manager.GetSessions();

        return sessions.FirstOrDefault(IsSpotifySession) ??
            _manager.GetCurrentSession() ??
            sessions.FirstOrDefault();
    }

    private static bool IsSpotifySession(
        GlobalSystemMediaTransportControlsSession session)
    {
        return session.SourceAppUserModelId.Contains(
            "spotify",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTrackId(
        string source,
        string title,
        string artist,
        TimeSpan duration)
    {
        return $"{source}|{artist}|{title}|{Math.Round(duration.TotalSeconds)}";
    }
}
