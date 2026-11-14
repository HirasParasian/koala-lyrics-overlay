namespace SpotifyLyricsOverlay;

public sealed class LyricsService
{
    private readonly LrcLibProvider _lrcLib = new();
    private readonly Dictionary<string, LyricsResult?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<LyricsResult?> GetAsync(
        string title,
        string artist,
        string album,
        double durationSeconds,
        CancellationToken cancellationToken = default)
    {
        var key = $"{artist}|{title}|{album}|{Math.Round(durationSeconds)}";

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = await _lrcLib.SearchAsync(
            title,
            artist,
            album,
            durationSeconds,
            cancellationToken);

        _cache[key] = result;

        return result;
    }
}
