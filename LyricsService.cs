namespace SpotifyLyricsOverlay;

public sealed class LyricsService
{
    private readonly LocalLyricsProvider _local = new();
    private readonly LrcLibProvider _lrcLib = new();
    private readonly KugouProvider _kugou = new();
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

        // 1. Try local .lrc file first (custom offline lyrics)
        var localResult = await _local.GetAsync(title, artist);
        if (localResult is not null)
        {
            _cache[key] = localResult;
            return localResult;
        }

        // 2. Try LRCLIB (exact + fuzzy + cleaned title + plain text fallback)
        var lrcLibResult = await _lrcLib.SearchAsync(
            title,
            artist,
            album,
            durationSeconds,
            cancellationToken);

        if (lrcLibResult is not null)
        {
            _cache[key] = lrcLibResult;
            return lrcLibResult;
        }

        // 3. Try Kugou Music as fallback (extensive Asian & international synced LRC catalog)
        var cleanTitle = TitleSanitizer.Clean(title);
        var kugouResult = await _kugou.SearchAsync(
            cleanTitle,
            artist,
            durationSeconds,
            cancellationToken);

        if (kugouResult is not null)
        {
            _cache[key] = kugouResult;
            return kugouResult;
        }

        // 4. If Kugou with cleaned title failed, try with original title
        if (!string.Equals(cleanTitle, title, StringComparison.OrdinalIgnoreCase))
        {
            kugouResult = await _kugou.SearchAsync(
                title,
                artist,
                durationSeconds,
                cancellationToken);

            if (kugouResult is not null)
            {
                _cache[key] = kugouResult;
                return kugouResult;
            }
        }

        _cache[key] = null;
        return null;
    }
}
