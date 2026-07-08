using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BPlayer.ThumbnailCore;

namespace BPlayer.Services;

public static class ThumbnailService
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BPlayer", "thumbcache");

    private static VideoThumbnailer? _thumbnailer;
    private static readonly SemaphoreSlim _throttle = new(3, 3);

    public static void EnsureCacheDir()
    {
        Directory.CreateDirectory(CacheDir);
    }

    public static Task<string?> GenerateThumbnailAsync(string videoPath)
    {
        return Task.Run(() => GenerateThumbnail(videoPath));
    }

    private static string? GenerateThumbnail(string videoPath)
    {
        if (!File.Exists(videoPath)) return null;

        var cacheKey = Path.GetFileNameWithoutExtension(videoPath);
        var cachePath = Path.Combine(CacheDir, cacheKey + ".jpg");

        // Cache check outside lock — fast path for already-generated thumbnails
        if (File.Exists(cachePath))
        {
            try
            {
                var fi = new FileInfo(cachePath);
                if (fi.Length > 500) return cachePath;
                File.Delete(cachePath);
            }
            catch { }
        }

        _throttle.Wait();
        try
        {
            _thumbnailer ??= new VideoThumbnailer(CacheDir);

            var result = _thumbnailer.Generate(videoPath);
            if (result.Success)
            {
                Logger.Info($"Thumbnail OK for {Path.GetFileName(videoPath)}");
                return result.CachedPath;
            }

            Logger.Warn($"Thumbnail FAILED for {videoPath}: {result.Error}");
            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }
}
