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

    public Task<LyricsResult?> GetAsync(string title, string artist)
    {
        var candidates = new[]
        {
            $"{artist} - {title}.lrc",
            $"{title} - {artist}.lrc",
            $"{title}.lrc"
        };

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
                        var lines = LrcParser.Parse(text);
                        if (lines.Count > 0)
                        {
                            return Task.FromResult<LyricsResult?>(
                                new LyricsResult(lines, "Local LRC File", title, artist));
                        }
                    }
                    catch { }
                }
            }
        }

        return Task.FromResult<LyricsResult?>(null);
    }
}
