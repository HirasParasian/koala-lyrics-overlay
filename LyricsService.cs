using System.IO;

namespace SpotifyLyricsOverlay;

public sealed class LyricsService
{
    private readonly LocalLyricsProvider _local = new();
    private readonly LrcLibProvider _lrcLib = new();
    private readonly KugouProvider _kugou = new();
    private readonly PlainLyricsProvider _plain = new();
    private readonly Dictionary<string, LyricsResult?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SpotifyLyricsOverlay", "lyrics_debug.log");

    public string? GeniusToken { get; set; }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public async Task<LyricsResult?> GetAsync(
        string title,
        string artist,
        string album,
        double durationSeconds,
        CancellationToken cancellationToken = default)
    {
        var key = $"{artist}|{title}|{album}|{Math.Round(durationSeconds)}";

        Log($"=== Search: {artist} - {title} (album: {album}, dur: {durationSeconds:F1}s) ===");

        if (_cache.TryGetValue(key, out var cached))
        {
            Log($"  Cache hit: {(cached is not null ? cached.Provider : "null")}");
            return cached;
        }

        // 1. Try local .lrc or .txt file first (custom offline lyrics)
        var localResult = await _local.GetAsync(title, artist, durationSeconds);
        if (localResult is not null)
        {
            Log($"  ✓ Local: {localResult.Lines.Count} lines (synced={localResult.IsSynced})");
            _cache[key] = localResult;
            return localResult;
        }
        Log("  ✗ Local: not found");

        // 2. Try LRCLIB (exact + fuzzy + cleaned title + plain text fallback)
        var lrcLibResult = await _lrcLib.SearchAsync(
            title,
            artist,
            album,
            durationSeconds,
            cancellationToken);

        if (lrcLibResult is not null)
        {
            Log($"  ✓ LRCLIB: {lrcLibResult.Provider}, {lrcLibResult.Lines.Count} lines, synced={lrcLibResult.IsSynced}");
            _cache[key] = lrcLibResult;
            return lrcLibResult;
        }
        Log("  ✗ LRCLIB: not found");

        // 3. Try Kugou Music as synced fallback (extensive Asian & international synced LRC catalog)
        var cleanTitle = TitleSanitizer.Clean(title);
        var kugouResult = await _kugou.SearchAsync(
            cleanTitle,
            artist,
            durationSeconds,
            cancellationToken);

        if (kugouResult is not null)
        {
            Log($"  ✓ Kugou: {kugouResult.Lines.Count} lines");
            _cache[key] = kugouResult;
            return kugouResult;
        }
        Log($"  ✗ Kugou (clean '{cleanTitle}'): not found");

        if (!string.Equals(cleanTitle, title, StringComparison.OrdinalIgnoreCase))
        {
            kugouResult = await _kugou.SearchAsync(
                title,
                artist,
                durationSeconds,
                cancellationToken);

            if (kugouResult is not null)
            {
                Log($"  ✓ Kugou (original): {kugouResult.Lines.Count} lines");
                _cache[key] = kugouResult;
                return kugouResult;
            }
            Log("  ✗ Kugou (original): not found");
        }

        // 4. Final fallback: Genius & open web plain lyrics (unsynced with auto-timing)
        Log($"  Trying PlainLyricsProvider (token={(!string.IsNullOrEmpty(GeniusToken) ? "yes" : "no")})...");
        var plainResult = await _plain.SearchAsync(
            title,
            artist,
            durationSeconds,
            GeniusToken,
            cancellationToken);

        if (plainResult is not null)
        {
            Log($"  ✓ Plain: {plainResult.Provider}, {plainResult.Lines.Count} lines, plainText={plainResult.PlainText?.Length ?? 0} chars");
            _cache[key] = plainResult;
            return plainResult;
        }
        Log("  ✗ Plain: not found");

        Log("  === RESULT: No lyrics found ===");
        _cache[key] = null;
        return null;
    }
}
