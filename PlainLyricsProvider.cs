using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpotifyLyricsOverlay;

public sealed class PlainLyricsProvider
{
    private readonly HttpClient _http = new();
    private static readonly Regex HeaderTagRegex = new(
        @"^\[(verse|chorus|bridge|intro|outro|hook|pre-chorus|instrumental|solo|drop|refrain)[^\]]*\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public PlainLyricsProvider()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _http.Timeout = TimeSpan.FromSeconds(6);
    }

    public async Task<LyricsResult?> SearchAsync(
        string title,
        string artist,
        double durationSeconds,
        string? geniusToken = null,
        CancellationToken cancellationToken = default)
    {
        var cleanTitle = TitleSanitizer.Clean(title);

        // 1. If Genius token is configured, try official Genius API
        if (!string.IsNullOrWhiteSpace(geniusToken))
        {
            var geniusResult = await TryGeniusApiAsync(
                cleanTitle,
                artist,
                durationSeconds,
                geniusToken,
                cancellationToken);

            if (geniusResult is not null)
                return geniusResult;
        }

        // 2. Try Lyrics.ovh (open multi-source plain lyrics database including Genius)
        var ovhResult = await TryLyricsOvhAsync(
            cleanTitle,
            artist,
            durationSeconds,
            cancellationToken);

        if (ovhResult is not null)
            return ovhResult;

        // 3. Retry Lyrics.ovh with original title if cleaned was different
        if (!string.Equals(cleanTitle, title, StringComparison.OrdinalIgnoreCase))
        {
            ovhResult = await TryLyricsOvhAsync(
                title,
                artist,
                durationSeconds,
                cancellationToken);

            if (ovhResult is not null)
                return ovhResult;
        }

        return null;
    }

    private async Task<LyricsResult?> TryLyricsOvhAsync(
        string title,
        string artist,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://api.lyrics.ovh/v1/{Uri.EscapeDataString(artist)}/{Uri.EscapeDataString(title)}";

            using var resp = await _http.GetAsync(url, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return null;

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("lyrics", out var lyricsElem))
                return null;

            var plainText = lyricsElem.GetString();
            if (string.IsNullOrWhiteSpace(plainText))
                return null;

            var lines = LrcParser.AutoTimePlainLyrics(plainText, durationSeconds);
            if (lines.Count == 0)
                return null;

            return new LyricsResult(
                lines,
                "Genius / Plain (unsynced)",
                title,
                artist);
        }
        catch
        {
            return null;
        }
    }

    private async Task<LyricsResult?> TryGeniusApiAsync(
        string title,
        string artist,
        double durationSeconds,
        string token,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = $"{title} {artist}".Trim();
            var searchUrl = $"https://api.genius.com/search?q={Uri.EscapeDataString(query)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            req.Headers.Add("Authorization", $"Bearer {token}");

            using var resp = await _http.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return null;

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var hits = doc.RootElement
                .GetProperty("response")
                .GetProperty("hits");

            if (hits.GetArrayLength() == 0)
                return null;

            var firstHit = hits[0].GetProperty("result");
            var songUrl = firstHit.GetProperty("url").GetString();

            if (string.IsNullOrWhiteSpace(songUrl))
                return null;

            // Fetch page HTML to extract lyrics
            using var pageResp = await _http.GetAsync(songUrl, cancellationToken);
            if (!pageResp.IsSuccessStatusCode)
                return null;

            var html = await pageResp.Content.ReadAsStringAsync(cancellationToken);
            var extracted = ExtractLyricsFromGeniusHtml(html);

            if (string.IsNullOrWhiteSpace(extracted))
                return null;

            var lines = LrcParser.AutoTimePlainLyrics(extracted, durationSeconds);
            if (lines.Count == 0)
                return null;

            return new LyricsResult(
                lines,
                "Genius (unsynced)",
                title,
                artist);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractLyricsFromGeniusHtml(string html)
    {
        var matches = Regex.Matches(
            html,
            @"<div[^>]*data-lyrics-container=""true""[^>]*>(.*?)<\/div>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        if (matches.Count == 0)
            return "";

        var sb = new System.Text.StringBuilder();

        foreach (Match m in matches)
        {
            var fragment = m.Groups[1].Value;
            fragment = Regex.Replace(fragment, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            fragment = Regex.Replace(fragment, @"<[^>]+>", "", RegexOptions.Singleline);
            fragment = System.Net.WebUtility.HtmlDecode(fragment);
            sb.AppendLine(fragment);
        }

        return sb.ToString().Trim();
    }
}
