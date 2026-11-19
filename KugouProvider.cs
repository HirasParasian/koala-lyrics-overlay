using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SpotifyLyricsOverlay;

public sealed class KugouProvider
{
    private readonly HttpClient _http = new();

    public KugouProvider()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _http.Timeout = TimeSpan.FromSeconds(5);
    }

    public async Task<LyricsResult?> SearchAsync(
        string title,
        string artist,
        double durationSeconds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var keyword = $"{title} {artist}".Trim();
            var searchUrl =
                $"http://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword={Uri.EscapeDataString(keyword)}&page=1&pagesize=5";

            using var searchResp = await _http.GetAsync(searchUrl, cancellationToken);
            if (!searchResp.IsSuccessStatusCode)
                return null;

            var searchJson = await searchResp.Content.ReadAsStringAsync(cancellationToken);
            using var searchDoc = JsonDocument.Parse(searchJson);

            if (!searchDoc.RootElement.TryGetProperty("data", out var dataElem) ||
                !dataElem.TryGetProperty("info", out var infoElem) ||
                infoElem.GetArrayLength() == 0)
                return null;

            string? bestHash = null;
            var bestDurationDiff = double.MaxValue;

            foreach (var item in infoElem.EnumerateArray())
            {
                if (!item.TryGetProperty("hash", out var hashElem))
                    continue;

                var hash = hashElem.GetString();
                if (string.IsNullOrWhiteSpace(hash))
                    continue;

                var duration = item.TryGetProperty("duration", out var durElem)
                    ? durElem.GetDouble()
                    : 0;

                var diff = Math.Abs(duration - durationSeconds);
                if (diff < bestDurationDiff)
                {
                    bestDurationDiff = diff;
                    bestHash = hash;
                }
            }

            if (string.IsNullOrWhiteSpace(bestHash))
                return null;

            // Step 2: Search lyric candidates by hash
            var candUrl =
                $"http://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword=&duration=&hash={bestHash}";

            using var candResp = await _http.GetAsync(candUrl, cancellationToken);
            if (!candResp.IsSuccessStatusCode)
                return null;

            var candJson = await candResp.Content.ReadAsStringAsync(cancellationToken);
            using var candDoc = JsonDocument.Parse(candJson);

            if (!candDoc.RootElement.TryGetProperty("candidates", out var candArray) ||
                candArray.GetArrayLength() == 0)
                return null;

            var firstCand = candArray[0];
            if (!firstCand.TryGetProperty("id", out var idElem) ||
                !firstCand.TryGetProperty("accesskey", out var keyElem))
                return null;

            var id = idElem.ToString();
            var accessKey = keyElem.GetString();

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(accessKey))
                return null;

            // Step 3: Download LRC
            var lrcUrl =
                $"http://lyrics.kugou.com/download?ver=1&client=pc&id={id}&accesskey={accessKey}&fmt=lrc&charset=utf8";

            using var lrcResp = await _http.GetAsync(lrcUrl, cancellationToken);
            if (!lrcResp.IsSuccessStatusCode)
                return null;

            var lrcJson = await lrcResp.Content.ReadAsStringAsync(cancellationToken);
            using var lrcDoc = JsonDocument.Parse(lrcJson);

            if (!lrcDoc.RootElement.TryGetProperty("content", out var contentElem))
                return null;

            var base64Content = contentElem.GetString();
            if (string.IsNullOrWhiteSpace(base64Content))
                return null;

            var lrcBytes = Convert.FromBase64String(base64Content);
            var lrcText = Encoding.UTF8.GetString(lrcBytes);

            var lines = LrcParser.Parse(lrcText);
            if (lines.Count == 0)
                return null;

            return new LyricsResult(lines, "Kugou Music", title, artist);
        }
        catch
        {
            return null;
        }
    }
}
