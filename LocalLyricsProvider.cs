using System.IO;

namespace SpotifyLyricsOverlay;

public sealed class LocalLyricsProvider
{
    private readonly string _userLyricsDir;
    private readonly string _appLyricsDir;

    public LocalLyricsProvider()
    {
        _userLyricsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpotifyLyricsOverlay",
            "Lyrics");

        _appLyricsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lyrics");

        try
        {
            Directory.CreateDirectory(_userLyricsDir);
        }
        catch { }
    }

    public Task<LyricsResult?> GetAsync(string title, string artist, double durationSeconds = 180)
    {
        var cleanTitle = TitleSanitizer.Clean(title);
        var candidates = new List<string>
        {
            $"{artist} - {title}.lrc",
            $"{title} - {artist}.lrc",
            $"{title}.lrc",
            $"{artist} - {title}.txt",
            $"{title} - {artist}.txt",
            $"{title}.txt"
        };

        if (!string.Equals(cleanTitle, title, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add($"{artist} - {cleanTitle}.lrc");
            candidates.Add($"{cleanTitle} - {artist}.lrc");
            candidates.Add($"{cleanTitle}.lrc");
            candidates.Add($"{artist} - {cleanTitle}.txt");
            candidates.Add($"{cleanTitle} - {artist}.txt");
            candidates.Add($"{cleanTitle}.txt");
        }

        var searchDirs = new[] { _userLyricsDir, _appLyricsDir };

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var name in candidates)
            {
                var filePath = Path.Combine(dir, name);
                if (File.Exists(filePath))
                {
                    try
                    {
                        var text = File.ReadAllText(filePath);
                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        // Check if it's LRC format with timestamps
                        var lines = LrcParser.Parse(text);
                        if (lines.Count > 0)
                        {
                            return Task.FromResult<LyricsResult?>(
                                new LyricsResult(
                                    lines,
                                    "Local LRC File",
                                    title,
                                    artist,
                                    IsSynced: true));
                        }

                        // Otherwise treated as plain text lyrics
                        var autoTimedLines = LrcParser.AutoTimePlainLyrics(text, durationSeconds);
                        return Task.FromResult<LyricsResult?>(
                            new LyricsResult(
                                autoTimedLines,
                                "Local Text File",
                                title,
                                artist,
                                IsSynced: false,
                                PlainText: text));
                    }
                    catch { }
                }
            }
        }

        return Task.FromResult<LyricsResult?>(null);
    }
}
