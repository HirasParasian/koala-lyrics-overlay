using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpotifyLyricsOverlay;

public sealed class LrcLibProvider
{
    private const string BaseUrl = "https://lrclib.net/api";
    private readonly HttpClient _http = new();

    public LrcLibProvider()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "SpotifyLyricsOverlay/4.0 (personal desktop app)");
        _http.Timeout = TimeSpan.FromSeconds(5);
    }

    public async Task<LyricsResult?> SearchAsync(
        string title,
        string artist,
        string album,
        double durationSeconds,
        CancellationToken cancellationToken = default)
    {
        var cleanTitle = TitleSanitizer.Clean(title);

        // 1. Exact match with original title
        var exact = await GetExactAsync(
            title, artist, album, durationSeconds, cancellationToken);
        var exactResult = ToResult(exact, "LRCLIB exact", durationSeconds);
        if (exactResult is not null)
            return exactResult;

        // 2. Exact match with cleaned title
        if (!string.Equals(cleanTitle, title, StringComparison.OrdinalIgnoreCase))
        {
            var cleanExact = await GetExactAsync(
                cleanTitle, artist, album, durationSeconds, cancellationToken);
            var cleanExactResult = ToResult(cleanExact, "LRCLIB exact (clean)", durationSeconds);
            if (cleanExactResult is not null)
                return cleanExactResult;
        }

        // 3. Search endpoint by track_name & artist_name
        var candidates = await SearchAsync(
            cleanTitle, artist, cancellationToken);

        // 4. Broad search endpoint (?q=...) if candidates empty
        if (candidates.Count == 0)
        {
            candidates = await SearchQueryAsync(
                $"{cleanTitle} {artist}", cancellationToken);
        }

        var best = SelectBest(
            candidates,
            cleanTitle,
            artist,
            album,
            durationSeconds);

        return ToResult(best, "LRCLIB search", durationSeconds);
    }

    private async Task<LrcRecord?> GetExactAsync(
        string title,
        string artist,
        string album,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            var url =
                $"{BaseUrl}/get" +
                $"?track_name={Uri.EscapeDataString(title)}" +
                $"&artist_name={Uri.EscapeDataString(artist)}" +
                $"&album_name={Uri.EscapeDataString(album)}" +
                $"&duration={durationSeconds.ToString(CultureInfo.InvariantCulture)}";

            using var response = await _http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            return await DeserializeAsync<LrcRecord>(
                response,
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<LrcRecord>> SearchAsync(
        string title,
        string artist,
        CancellationToken cancellationToken)
    {
        try
        {
            var url =
                $"{BaseUrl}/search" +
                $"?track_name={Uri.EscapeDataString(title)}" +
                $"&artist_name={Uri.EscapeDataString(artist)}";

            using var response = await _http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new();

            return await DeserializeAsync<List<LrcRecord>>(
                response,
                cancellationToken) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private async Task<List<LrcRecord>> SearchQueryAsync(
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{BaseUrl}/search?q={Uri.EscapeDataString(query)}";

            using var response = await _http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new();

            return await DeserializeAsync<List<LrcRecord>>(
                response,
                cancellationToken) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static async Task<T?> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        return JsonSerializer.Deserialize<T>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
    }

    private static LyricsResult? ToResult(
        LrcRecord? record,
        string provider,
        double durationSeconds)
    {
        if (record is null)
            return null;

        if (!string.IsNullOrWhiteSpace(record.SyncedLyrics))
        {
            var lines = LrcParser.Parse(record.SyncedLyrics);
            if (lines.Count > 0)
            {
                return new LyricsResult(
                    lines,
                    provider,
                    record.TrackName,
                    record.ArtistName);
            }
        }

        // Fallback to plain lyrics with auto-timing
        if (!string.IsNullOrWhiteSpace(record.PlainLyrics))
        {
            var plainLines = LrcParser.AutoTimePlainLyrics(record.PlainLyrics, durationSeconds);
            if (plainLines.Count > 0)
            {
                return new LyricsResult(
                    plainLines,
                    $"{provider} (auto-timed)",
                    record.TrackName,
                    record.ArtistName,
                    IsSynced: false,
                    PlainText: record.PlainLyrics);
            }
        }

        return null;
    }

    private static LrcRecord? SelectBest(
        IEnumerable<LrcRecord> candidates,
        string title,
        string artist,
        string album,
        double durationSeconds)
    {
        LrcRecord? bestSynced = null;
        var bestSyncedScore = 0.0;

        LrcRecord? bestPlain = null;
        var bestPlainScore = 0.0;

        foreach (var candidate in candidates)
        {
            var hasSynced = !string.IsNullOrWhiteSpace(candidate.SyncedLyrics);
            var hasPlain = !string.IsNullOrWhiteSpace(candidate.PlainLyrics);

            if (!hasSynced && !hasPlain)
                continue;

            var titleScore = Similarity(
                Normalize(title),
                Normalize(candidate.TrackName));

            var artistScore = Similarity(
                Normalize(artist),
                Normalize(candidate.ArtistName));

            var albumScore = Similarity(
                Normalize(album),
                Normalize(candidate.AlbumName));

            var durationDiff = Math.Abs(
                candidate.Duration - durationSeconds);

            var durationScore =
                durationDiff <= 2 ? 1.0 :
                durationDiff <= 5 ? 0.75 :
                durationDiff <= 10 ? 0.35 :
                0.0;

            var score =
                titleScore * 0.50 +
                artistScore * 0.35 +
                albumScore * 0.05 +
                durationScore * 0.10;

            if (hasSynced && score > bestSyncedScore)
            {
                bestSyncedScore = score;
                bestSynced = candidate;
            }
            else if (!hasSynced && score > bestPlainScore)
            {
                bestPlainScore = score;
                bestPlain = candidate;
            }
        }

        if (bestSyncedScore >= 0.55)
            return bestSynced;

        if (bestPlainScore >= 0.65)
            return bestPlain;

        return null;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var c in decomposed)
        {
            var category =
                System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);

            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
            else
                sb.Append(' ');
        }

        return Regex.Replace(
            sb.ToString(),
            @"\s+",
            " ").Trim();
    }

    private static double Similarity(string a, string b)
    {
        if (a == b)
            return 1.0;

        if (string.IsNullOrWhiteSpace(a) ||
            string.IsNullOrWhiteSpace(b))
            return 0.0;

        if (a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
            b.Contains(a, StringComparison.OrdinalIgnoreCase))
            return 0.9;

        var aTokens = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bTokens = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var common = aTokens
            .Intersect(bTokens, StringComparer.OrdinalIgnoreCase)
            .Count();

        return Math.Max(aTokens.Length, bTokens.Length) == 0
            ? 0
            : (double)common /
              Math.Max(aTokens.Length, bTokens.Length);
    }

    private sealed class LrcRecord
    {
        public string TrackName { get; set; } = "";
        public string ArtistName { get; set; } = "";
        public string AlbumName { get; set; } = "";
        public double Duration { get; set; }
        public string? SyncedLyrics { get; set; }
        public string? PlainLyrics { get; set; }
    }
}
