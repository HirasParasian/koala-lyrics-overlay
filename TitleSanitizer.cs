using System.Text.RegularExpressions;

namespace SpotifyLyricsOverlay;

public static class TitleSanitizer
{
    private static readonly Regex CleanPattern = new(
        @"\s*(\([^\)]*(feat\.|ft\.|with|remaster|live|deluxe|version|bonus|edit|mix|soundtrack|ost)[^\)]*\)|\[[^\]]*(feat\.|ft\.|with|remaster|live|deluxe|version|bonus|edit|mix|soundtrack|ost)[^\]]*\]|\s*-\s*(remaster|live|radio edit|deluxe|bonus|mono|stereo|single|acoustic|instrumental).*$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Clean(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";

        var cleaned = CleanPattern.Replace(title, "").Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? title.Trim() : cleaned;
    }
}
