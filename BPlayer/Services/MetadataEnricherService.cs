using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BPlayer.Models;

namespace BPlayer.Services;

public class MetadataEnricherService
{
    private readonly PosterService _posterService;
    private readonly ThumbnailService _thumbnailService;
    private readonly PlaybackStateService _playbackState = PlaybackStateService.Instance;

    public event Action<double>? ProgressChanged;

    public MetadataEnricherService(HttpClient http, List<MetadataSource> sources)
    {
        var metadataService = new MetadataService(http, sources);
        _posterService = new PosterService(metadataService);
        _thumbnailService = new ThumbnailService(_posterService, http);
    }

    public ThumbnailService ThumbnailService => _thumbnailService;

    private static readonly string MetaCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BPlayer", "metacache");

    private class CachedMeta
    {
        public double Rating { get; set; }
        public int Year { get; set; }
        public string? PosterUrl { get; set; }
    }

    private static string MetaCachePath(string videoPath)
    {
        var key = Path.GetFileNameWithoutExtension(videoPath);
        return Path.Combine(MetaCacheDir, key + ".json");
    }

    private static void SaveMetaCache(VideoItem video)
    {
        try
        {
            Directory.CreateDirectory(MetaCacheDir);
            var meta = new CachedMeta
            {
                Rating = video.Rating,
                Year = video.Year,
                PosterUrl = !string.IsNullOrEmpty(video.BannerUrl) && video.BannerUrl.StartsWith("http")
                    ? video.BannerUrl
                    : null
            };
            File.WriteAllText(MetaCachePath(video.FilePath), JsonSerializer.Serialize(meta));
        }
        catch (Exception ex) { Logger.Warn($"Failed to save meta cache: {ex.Message}"); }
    }

    private static CachedMeta? LoadMetaCache(string videoPath)
    {
        var path = MetaCachePath(videoPath);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<CachedMeta>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    public async Task EnrichAsync(ObservableCollection<VideoItem> videos, bool enableOnline, bool enableThumbnails)
    {
        var snapshot = videos.ToList();
        var total = snapshot.Count;
        if (total == 0) return;

        void Report(double pct) => ProgressChanged?.Invoke(pct);

        // Phase 0: Restore cached metadata from disk — poster URL takes priority over local thumbnail
        foreach (var video in snapshot)
        {
            var cached = LoadMetaCache(video.FilePath);
            if (cached != null)
            {
                if (cached.Rating > 0) video.Rating = cached.Rating;
                if (cached.Year > 0) video.Year = cached.Year;
                if (!string.IsNullOrEmpty(cached.PosterUrl))
                {
                    video.BannerUrl = cached.PosterUrl;
                    video.ThumbnailUrl = cached.PosterUrl; // poster overrides local thumbnail
                }
            }
        }

        // Phase 1: Online metadata — set poster URL immediately for display
        // Posters always take priority over generated thumbnails
        int onlineOk = 0, onlineFail = 0;
        int p1Done = 0;
        foreach (var video in snapshot)
        {
            bool hasPoster = !string.IsNullOrEmpty(video.BannerUrl) && video.BannerUrl.StartsWith("http");
            bool hasYearOrRating = video.Rating > 0 || video.Year > 0;

            // Only skip if we already have a poster AND year/rating
            if (hasPoster && hasYearOrRating)
            {
                Report(0.4 * (double)(++p1Done) / total);
                continue;
            }

            var filenameYear = FilenameUtils.ExtractYearFromFilename(video.Title);

            if (enableOnline && (!hasPoster || !hasYearOrRating))
            {
                var result = await _posterService.FetchAsync(video.Title);
                if (result != null)
                {
                    if (filenameYear > 0 && result.Year > 0 && result.Year != filenameYear)
                    {
                        Logger.Warn($"Metadata mismatch for '{video.Title}': provider year {result.Year} != filename year {filenameYear}, rejecting");
                        result = null;
                    }
                }
                if (result != null)
                {
                    onlineOk++;
                    if (result.Rating > 0) video.Rating = result.Rating;
                    if (result.Year > 0) video.Year = result.Year;
                    if (!string.IsNullOrEmpty(result.PosterUrl))
                    {
                        video.BannerUrl = result.PosterUrl;
                        video.ThumbnailUrl = result.PosterUrl; // show poster immediately
                        Logger.Info($"Poster OK for '{video.Title}' -> {result.PosterUrl}");
                    }
                }
                else
                {
                    onlineFail++;
                    Logger.Info($"Online no data for '{video.Title}'");
                }
            }

            if (video.Year == 0)
                video.Year = filenameYear;

            Report(0.4 * (double)(++p1Done) / total);
        }

        Logger.Info($"Online metadata: {onlineOk} OK, {onlineFail} no data");

        // Phase 2: Download posters as local thumbnails (fast, HTTP, with throttling)
        var posterBatch = snapshot.Where(v => !string.IsNullOrEmpty(v.BannerUrl) && v.BannerUrl.StartsWith("http")).ToList();
        if (posterBatch.Count > 0)
        {
            var throttle = new SemaphoreSlim(6, 6);
            var p2Done = 0;
            var tasks = posterBatch.Select(async video =>
            {
                await throttle.WaitAsync();
                try
                {
                    var localPath = await _thumbnailService.DownloadAndCachePosterAsync(video, video.BannerUrl!);
                    if (localPath != null)
                        video.ThumbnailUrl = localPath;
                }
                finally
                {
                    throttle.Release();
                    Report(0.4 + 0.3 * (double)(++p2Done) / posterBatch.Count);
                }
            });
            await Task.WhenAll(tasks);
        }
        else
        {
            Report(0.7);
        }

        // Phase 3: VLC thumbnails for videos that still have no image at all
        if (enableThumbnails)
        {
            var vlcQueue = snapshot
                .Where(v => string.IsNullOrEmpty(v.ThumbnailUrl))
                .ToList();

            if (vlcQueue.Count > 0)
            {
                Logger.Info($"Generating {vlcQueue.Count} VLC thumbnails");
                var throttle = new SemaphoreSlim(3, 3);
                var p3Done = 0;
                var tasks = vlcQueue.Select(async video =>
                {
                    await throttle.WaitAsync();
                    try
                    {
                        var thumb = await _thumbnailService.GenerateVlcThumbnailAsync(video.FilePath);
                        if (thumb != null)
                            video.ThumbnailUrl = thumb;
                    }
                    finally
                    {
                        throttle.Release();
                        Report(0.7 + 0.3 * (double)(++p3Done) / vlcQueue.Count);
                    }
                });
                await Task.WhenAll(tasks);
            }
        }

        // Phase 4: Watched state
        foreach (var v in snapshot)
            v.IsWatched = _playbackState.IsWatched(v.FilePath);

        // Persist metacache
        foreach (var v in snapshot)
            SaveMetaCache(v);

        Report(1.0);
    }
}
