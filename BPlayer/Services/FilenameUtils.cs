using System.Text.RegularExpressions;

namespace BPlayer.Services;

public static class FilenameUtils
{
    public static int ExtractYearFromFilename(string name)
    {
        var match = Regex.Match(name, @"\b(19\d{2}|20\d{2})\b");
        if (match.Success && int.TryParse(match.Value, out var year))
            return year >= 1900 && year <= 2030 ? year : 0;
        return 0;
    }
}
