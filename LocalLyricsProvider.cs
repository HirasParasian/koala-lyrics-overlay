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

        var searchDirs = new List<string>
        {
            _userLyricsDir,
            _appLyricsDir,
            Path.Combine(Directory.GetCurrentDirectory(), "Lyrics"),
            @"D:\PROJECT SPOTIFY\LyricLocal\Lyrics"
        };

        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var parent1 = Directory.GetParent(baseDir)?.FullName;
            var parent2 = parent1 != null ? Directory.GetParent(parent1)?.FullName : null;
            var parent3 = parent2 != null ? Directory.GetParent(parent2)?.FullName : null;

            if (parent1 != null) searchDirs.Add(Path.Combine(parent1, "Lyrics"));
            if (parent2 != null) searchDirs.Add(Path.Combine(parent2, "Lyrics"));
            if (parent3 != null) searchDirs.Add(Path.Combine(parent3, "Lyrics"));
        }
        catch { }

        // Remove duplicate directories
        var uniqueDirs = searchDirs
            .Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 1. Exact match pass
        foreach (var dir in uniqueDirs)
        {
            foreach (var name in candidates)
            {
                var filePath = Path.Combine(dir, name);
                if (File.Exists(filePath))
                {
                    var res = TryReadFile(filePath, title, artist, durationSeconds);
                    if (res is not null) return Task.FromResult<LyricsResult?>(res);
                }
            }
        }

        // 2. Fuzzy / contains filename match pass
        foreach (var dir in uniqueDirs)
        {
            try
            {
                var files = Directory.GetFiles(dir, "*.*")
                    .Where(f => f.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

                foreach (var file in files)
                {
                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);

                    // Check if file name contains title or cleanTitle
                    if (fileNameWithoutExt.Contains(title, StringComparison.OrdinalIgnoreCase) ||
                        fileNameWithoutExt.Contains(cleanTitle, StringComparison.OrdinalIgnoreCase) ||
                        title.Contains(fileNameWithoutExt, StringComparison.OrdinalIgnoreCase))
                    {
                        var res = TryReadFile(file, title, artist, durationSeconds);
                        if (res is not null) return Task.FromResult<LyricsResult?>(res);
                    }
                }
            }
            catch { }
        }

        return Task.FromResult<LyricsResult?>(null);
    }

    private static LyricsResult? TryReadFile(
        string filePath,
        string title,
        string artist,
        double durationSeconds)
    {
        try
        {
            var text = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Check if it's LRC format with timestamps
            var lines = LrcParser.Parse(text);
            if (lines.Count > 0)
            {
                return new LyricsResult(
                    lines,
                    $"Local File ({Path.GetFileName(filePath)})",
                    title,
                    artist,
                    IsSynced: true);
            }

            // Otherwise treated as plain text lyrics
            var autoTimedLines = LrcParser.AutoTimePlainLyrics(text, durationSeconds);
            return new LyricsResult(
                autoTimedLines,
                $"Local File ({Path.GetFileName(filePath)})",
                title,
                artist,
                IsSynced: false,
                PlainText: text);
        }
        catch
        {
            return null;
        }
    }
}
