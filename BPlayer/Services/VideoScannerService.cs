using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BPlayer.Models;
using BPlayer.Services;

namespace BPlayer.Services;

public class VideoScannerService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase) 
        { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };

    public Task<List<VideoItem>> ScanDirectoriesAsync(IEnumerable<string> paths)
    {
        var videos = new List<VideoItem>();
        var allFiles = new List<string>();

        foreach (var dir in paths)
        {
            if (!Directory.Exists(dir)) { Logger.Warn($"Scan: dir not found: {dir}"); continue; }

            Logger.Info($"Scan: scanning {dir}");

            try
            {
                var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
                Logger.Info($"Scan: found {files.Count} total files in {dir}");
                allFiles.AddRange(files);
            }
            catch (Exception ex) { Logger.Error($"Scan: error in {dir}: {ex.Message}"); }
        }

        foreach (var file in allFiles)
        {
            var ext = Path.GetExtension(file);
            if (!string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext))
            {
                Logger.Info($"Scan: MATCHED {file} -> title='{Path.GetFileNameWithoutExtension(file)}'");
                videos.Add(new VideoItem
                {
                    FilePath = file,
                    Title = Path.GetFileNameWithoutExtension(file),
                    IsLoading = false,
                    AddedAt = System.IO.File.GetCreationTime(file)
                });
            }
        }

        Logger.Info($"Scan: total matched {videos.Count} videos");
        return Task.FromResult(videos.OrderBy(v => v.Title).ToList());
    }
}
