using System;
using System.Text.RegularExpressions;

namespace BPlayer.Services;

public static class FilenameUtils
{
    private static readonly Regex[] CleanupRegex =
    [
        new(@"[\[\(][^\]\)]*[\]\)]", RegexOptions.None),
        new(@"[\s.\-_]*[Ss]\d{1,2}[Ee]\d{1,2}(?:[Ee]\d{1,2})*[\s.\-_]*", RegexOptions.None),
        new(@"[\s.\-_]*(?:Season|Episode|Ep)\s*\d{1,2}[\s.\-_]*", RegexOptions.IgnoreCase),
        new(@"[\s.\-_]+(?:\d{3,4}p|\d{1,3}\s*fps|4K|8K)(?=[\s.\-_]|$)", RegexOptions.IgnoreCase),
        new(@"(?<=[\s.\-_]|^)(?:BluRay|WEB[- ]?DL|WEBRip|HDRip|DVDRip|BRRip|BDRip|HDTV|x264|x265|h264|h265|HEVC|AVC|AV1|DivX|XviD|AAC|AC3|DTS|TrueHD|FLAC|MP3|DDP\d(?:\.\d)?|10bit|8bit|HDR\d*|SDR)(?=[\s.\-_]|$)", RegexOptions.IgnoreCase),
        new(@"(?<=[\s.\-_]|^)(?:DC|EXTENDED|UNRATED|THEATRICAL|DIRECTOR.?CUT|FINAL.?CUT|SPECIAL.?EDITION|REMASTERED)(?=[\s.\-_]|$)", RegexOptions.IgnoreCase),
        new(@"(?<=[\s.\-_]|^)(?:CHINESE|ENGLISH|FRENCH|JAPANESE|KOREAN|SPANISH|GERMAN|ITALIAN|RUSSIAN|HINDI|TAMIL|TELUGU|ARABIC|PORTUGUESE|DUTCH|SWEDISH|NORWEGIAN|DANISH|FINNISH|POLISH|TURKISH|THAI|VIETNAMESE|INDONESIAN|MALAY)(?=[\s.\-_]|$)", RegexOptions.IgnoreCase),
        new(@"(?<=[\s.\-_]|^)(?:PROPER|REPACK|iNTERNAL|MULTi|DUAL|READ\.NFO)(?=[\s.\-_]|$)", RegexOptions.IgnoreCase),
        new(@"(?<=[\s.\-_]|^)[A-Z][A-Za-z0-9]{2,}(?=[\s.\-_]*$)", RegexOptions.None),
    ];

    public static string CleanTitleForSearch(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title;

        // Strategy 1: if filename contains a year, use everything before it as the title
        var yearMatch = Regex.Match(title, @"\b(19\d{2}|20\d{2})\b");
        if (yearMatch.Success && yearMatch.Index > 0)
        {
            var beforeYear = title[..yearMatch.Index];
            beforeYear = Regex.Replace(beforeYear, @"[\[\(][^\]\)]*[\]\)]", " ");
            beforeYear = beforeYear.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
            beforeYear = Regex.Replace(beforeYear, @"\s+", " ").Trim();
            beforeYear = Regex.Replace(beforeYear, @"[^\w\s]", "").Trim();
            if (beforeYear.Length >= 2)
                return beforeYear;
        }

        // Strategy 2: fallback — clean up format tags, groups, etc.
        var result = title;
        foreach (var re in CleanupRegex)
            result = re.Replace(result, " ");
        result = result.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        result = Regex.Replace(result, @"\s+", " ").Trim();
        result = Regex.Replace(result, @"[^\w\s]", "");
        result = Regex.Replace(result, @"\b(?:19\d{2}|20\d{2})\b", "").Trim();
        result = Regex.Replace(result, @"\s+", " ").Trim();
        if (result.Length >= 2)
            return result;

        return title;
    }

    public static int ExtractYearFromFilename(string name)
    {
        var match = Regex.Match(name, @"\b(19\d{2}|20\d{2})\b");
        if (match.Success && int.TryParse(match.Value, out var year))
            return year >= 1900 && year <= 2030 ? year : 0;
        return 0;
    }
}
