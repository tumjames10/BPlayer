using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BPlayer.ThumbnailCore;

namespace BPlayer.Services;

public class PreviewThumbnailResult
{
    public List<string> FilePaths { get; set; } = new();
    public float[] Positions { get; set; } = Array.Empty<float>();
}

public class PreviewThumbnailService
{
    private static readonly string PreviewDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BPlayer", "previewthumbs");

    private static string GetPositionsFile(string dir) => Path.Combine(dir, "_positions.txt");

    public static float[]? ReadCachedPositions(string dir)
    {
        var pf = GetPositionsFile(dir);
        if (!File.Exists(pf)) return null;
        var lines = File.ReadAllLines(pf);
        var vals = lines.Where(l => float.TryParse(l, out _)).Select(float.Parse).ToArray();
        return vals.Length == 5 ? vals : null;
    }

    private static void WritePositions(string dir, float[] positions)
    {
        File.WriteAllLines(GetPositionsFile(dir), positions.Select(p => p.ToString("F4")));
    }

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

    public static Task<PreviewThumbnailResult> GenerateThumbnailsAsync(string videoPath)
    {
        return Task.Run(() =>
        {
            var dir = GetVideoDir(videoPath);
            Directory.CreateDirectory(dir);

            var positions = ScenePreviewConfig.PickRandomPositions();

            // Check cache first — verify positions match
            var cached = GetCachedThumbnails(videoPath);
            var cachedPositions = ReadCachedPositions(dir);
            if (cached.Count == 5 && cachedPositions != null && cachedPositions.SequenceEqual(positions))
            {
                return new PreviewThumbnailResult
                {
                    FilePaths = cached,
                    Positions = positions
                };
            }

            // Clear stale cache
            foreach (var f in Directory.GetFiles(dir, "preview_*.jpg"))
                try { File.Delete(f); } catch { }

            var paths = PreviewFrameGenerator.GenerateFrames(videoPath, dir, positions);
            WritePositions(dir, positions);
            return new PreviewThumbnailResult
            {
                FilePaths = paths,
                Positions = positions.Take(paths.Count).ToArray()
            };
        });
    }

    private static string GetVideoDir(string videoPath)
    {
        var key = Path.GetFileNameWithoutExtension(videoPath);
        return Path.Combine(PreviewDir, key);
    }
}
