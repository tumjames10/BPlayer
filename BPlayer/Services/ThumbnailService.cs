using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BPlayer.Models;
using BPlayer.ThumbnailCore;

namespace BPlayer.Services;

public class ThumbnailService
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BPlayer", "thumbcache");

    private readonly PosterService _posterService;
    private readonly HttpClient _httpClient;
    private VideoThumbnailer? _thumbnailer;
    private static readonly SemaphoreSlim _throttle = new(3, 3);

    public ThumbnailService(PosterService posterService, HttpClient httpClient)
    {
        _posterService = posterService;
        _httpClient = httpClient;
    }

    private static string CachePath(string videoPath)
    {
        return Path.Combine(CacheDir, Path.GetFileNameWithoutExtension(videoPath) + ".jpg");
    }

    public void PreloadCachedThumbnails(IEnumerable<VideoItem> videos)
    {
        Directory.CreateDirectory(CacheDir);
        foreach (var video in videos)
        {
            var cachePath = CachePath(video.FilePath);
            if (File.Exists(cachePath))
            {
                try
                {
                    var fi = new FileInfo(cachePath);
                    if (fi.Length > 500)
                        video.ThumbnailUrl = cachePath;
                }
                catch { }
            }
        }
    }

    public async Task<string?> DownloadAndCachePosterAsync(VideoItem video, string posterUrl)
    {
        var cachePath = CachePath(video.FilePath);

        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(posterUrl);
            if (bytes.Length > 500)
            {
                Directory.CreateDirectory(CacheDir);
                await File.WriteAllBytesAsync(cachePath, bytes);
                Logger.Info($"Poster cached locally for {Path.GetFileName(video.FilePath)}");
                return cachePath;
            }
        }
        catch (System.Exception ex)
        {
            Logger.Warn($"Poster download failed for {Path.GetFileName(video.FilePath)}: {ex.Message}");
        }

        return null;
    }

    public Task<string?> GenerateVlcThumbnailAsync(string videoPath)
    {
        return Task.Run(() => GenerateVlcThumbnail(videoPath));
    }

    private string? GenerateVlcThumbnail(string videoPath)
    {
        if (!File.Exists(videoPath)) return null;

        var cachePath = CachePath(videoPath);

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
