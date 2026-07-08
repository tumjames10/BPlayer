using System;

namespace BPlayer.Services;

public static class TitleCleaner
{
    private static readonly string[] Suffixes = {
        "1080p", "720p", "2160p", "WEBRip", "WEB-DL", "BluRay", "BRRip",
        "x264", "x265", "H264", "H265", "HEVC", "AAC", "DD5", "DTS",
        "YTS", "YIFY", "RARBG", "XViD", "DVDRip", "HDRip", "CAM"
    };

    public static string Clean(string raw)
    {
        var name = raw;

        foreach (var suffix in Suffixes)
            name = name.Replace(suffix, "", StringComparison.OrdinalIgnoreCase);

        name = name.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        while (name.Contains("  ")) name = name.Replace("  ", " ");
        name = name.Trim();

        var m = System.Text.RegularExpressions.Regex.Match(name, @"\(\s*(19\d{2}|20\d{2})\s*\)$");
        if (m.Success)
            name = name[..m.Index].Trim();

        if (name.Length >= 4 && int.TryParse(name[^4..], out var year) && year >= 1900 && year <= 2030)
            name = name[..^4].Trim();

        return name;
    }
}
