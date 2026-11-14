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
    }

    public async Task<LyricsResult?> SearchAsync(
        string title,
        string artist,
        string album,
        double durationSeconds,
        CancellationToken cancellationToken = default)
    {
        var exact = await GetExactAsync(
            title, artist, album, durationSeconds, cancellationToken);

        var exactResult = ToResult(exact, "LRCLIB exact");
        if (exactResult is not null)
            return exactResult;

        var candidates = await SearchAsync(
            title, artist, cancellationToken);

        var best = SelectBest(
            candidates,
            title,
            artist,
            album,
            durationSeconds);

        return ToResult(best, "LRCLIB search");
    }

    private async Task<LrcRecord?> GetExactAsync(
        string title,
        string artist,
        string album,
        double durationSeconds,
        CancellationToken cancellationToken)
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

    private async Task<List<LrcRecord>> SearchAsync(
        string title,
        string artist,
        CancellationToken cancellationToken)
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
        string provider)
    {
        if (record is null ||
            string.IsNullOrWhiteSpace(record.SyncedLyrics))
            return null;

        var lines = ParseLrc(record.SyncedLyrics);

        return lines.Count == 0
            ? null
            : new LyricsResult(
                lines,
                provider,
                record.TrackName,
                record.ArtistName);
    }

    private static LrcRecord? SelectBest(
        IEnumerable<LrcRecord> candidates,
        string title,
        string artist,
        string album,
        double durationSeconds)
    {
        LrcRecord? best = null;
        var bestScore = 0.0;

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.SyncedLyrics))
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

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return bestScore >= 0.65 ? best : null;
    }

    private static List<LyricsLine> ParseLrc(string lrc)
    {
        var result = new List<LyricsLine>();

        var regex = new Regex(
            @"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]",
            RegexOptions.Compiled);

        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var matches = regex.Matches(line);

            if (matches.Count == 0)
                continue;

            var text = regex.Replace(line, "").Trim();

            if (string.IsNullOrWhiteSpace(text))
                continue;

            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Groups[1].Value, out var minutes) ||
                    !int.TryParse(match.Groups[2].Value, out var seconds))
                    continue;

                var fraction = match.Groups[3].Value;
                var milliseconds = 0;

                if (!string.IsNullOrWhiteSpace(fraction))
                {
                    milliseconds = fraction.Length switch
                    {
                        1 => int.Parse(fraction) * 100,
                        2 => int.Parse(fraction) * 10,
                        _ => int.Parse(fraction[..3])
                    };
                }

                result.Add(new LyricsLine(
                    minutes * 60_000L +
                    seconds * 1_000L +
                    milliseconds,
                    text));
            }
        }

        return result
            .OrderBy(x => x.TimeMs)
            .ToList();
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
    }
}
