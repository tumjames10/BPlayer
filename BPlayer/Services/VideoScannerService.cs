using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BPlayer.Models;

namespace BPlayer.Services;

public class VideoScannerService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };

    public Task<List<VideoItem>> ScanDirectoriesAsync(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var videos = new List<VideoItem>();

        foreach (var dir in paths)
        {
            if (!Directory.Exists(dir)) { Logger.Warn($"Scan: dir not found: {dir}"); continue; }

            Logger.Info($"Scan: scanning {dir}");
            int dirCount = 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file);
                    if (!string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext) && seen.Add(file))
                    {
                        videos.Add(new VideoItem
                        {
                            FilePath = file,
                            Title = Path.GetFileNameWithoutExtension(file),
                            IsLoading = false,
                            AddedAt = File.GetCreationTime(file)
                        });
                    }
                    dirCount++;
                }
            }
            catch (Exception ex) { Logger.Error($"Scan: error in {dir}: {ex.Message}"); }

            Logger.Info($"Scan: scanned {dirCount} files, matched {videos.Count} videos in {dir}");
        }

        Logger.Info($"Scan: total matched {videos.Count} videos");
        return Task.FromResult(videos.OrderBy(v => v.Title).ToList());
    }
}
