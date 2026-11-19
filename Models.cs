namespace SpotifyLyricsOverlay;

public sealed record MediaPlayback(
    string TrackId,
    string Title,
    string Artist,
    string Album,
    long ProgressMs,
    long DurationMs,
    bool IsPlaying,
    string SourceApp);

public sealed record LyricsLine(long TimeMs, string Text);

public sealed record LyricsResult(
    IReadOnlyList<LyricsLine> Lines,
    string Provider,
    string TrackName,
    string ArtistName,
    bool IsSynced = true,
    string? PlainText = null);
