using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BPlayer.ThumbnailCore;

namespace BPlayer.Services;

public class PreviewThumbnailService
{
    private static readonly string PreviewDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BPlayer", "previewthumbs");

    public static List<string> GetCachedThumbnails(string videoPath)
    {
        var dir = GetVideoDir(videoPath);
        if (!Directory.Exists(dir)) return new List<string>();

        var files = Directory.GetFiles(dir, "preview_*.jpg")
            .OrderBy(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var parts = name.Split('_');
                return parts.Length == 2 && int.TryParse(parts[1], out var n) ? n : 0;
            })
            .ToList();

        return files.Count == 5 ? files : new List<string>();
    }

    public static Task<List<string>> GenerateThumbnailsAsync(string videoPath)
    {
        return Task.Run(() =>
        {
            var dir = GetVideoDir(videoPath);
            Directory.CreateDirectory(dir);

            // Check cache first
            var cached = GetCachedThumbnails(videoPath);
            if (cached.Count == 5)
                return cached;

            var results = PreviewFrameGenerator.GenerateFrames(videoPath, dir);
            return results;
        });
    }

    private static string GetVideoDir(string videoPath)
    {
        var key = Path.GetFileNameWithoutExtension(videoPath);
        return Path.Combine(PreviewDir, key);
    }
}
