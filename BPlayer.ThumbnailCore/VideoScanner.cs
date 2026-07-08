namespace BPlayer.ThumbnailCore;

public static class VideoScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };

    public static List<string> Scan(IEnumerable<string> directories)
    {
        var result = new List<string>();
        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file);
                    if (!string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext))
                        result.Add(file);
                }
            }
            catch { }
        }
        return result;
    }
}
