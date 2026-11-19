using System.Text.RegularExpressions;

namespace SpotifyLyricsOverlay;

public static class LrcParser
{
    private static readonly Regex TimestampRegex = new(
        @"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]",
        RegexOptions.Compiled);

    private static readonly Regex SectionHeaderRegex = new(
        @"^\[(verse|chorus|bridge|intro|outro|hook|pre-chorus|instrumental|solo|drop|refrain)[^\]]*\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<LyricsLine> Parse(string lrc)
    {
        var result = new List<LyricsLine>();

        if (string.IsNullOrWhiteSpace(lrc))
            return result;

        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var matches = TimestampRegex.Matches(line);

            if (matches.Count == 0)
                continue;

            var text = TimestampRegex.Replace(line, "").Trim();

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

    public static List<LyricsLine> AutoTimePlainLyrics(string plainText, double durationSeconds)
    {
        var result = new List<LyricsLine>();

        if (string.IsNullOrWhiteSpace(plainText) || durationSeconds <= 5)
            return result;

        var rawLines = plainText
            .Split('\n')
            .Select(l => l.Trim().TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l) && !SectionHeaderRegex.IsMatch(l))
            .ToList();

        if (rawLines.Count == 0)
            return result;

        var totalMs = durationSeconds * 1000.0;
        var startMs = Math.Min(totalMs * 0.08, 15000.0);
        var endMs = Math.Max(totalMs * 0.92, totalMs - 15000.0);
        var usableMs = Math.Max(1000.0, endMs - startMs);

        var intervalMs = usableMs / rawLines.Count;

        for (var i = 0; i < rawLines.Count; i++)
        {
            var timeMs = (long)(startMs + (i * intervalMs));
            result.Add(new LyricsLine(timeMs, rawLines[i]));
        }

        return result;
    }
}
