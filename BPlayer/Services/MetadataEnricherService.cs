using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BPlayer.Models;

namespace BPlayer.Services;

public class MetadataEnricherService
{
    private readonly MetadataService _metadataService;
    private readonly PlaybackStateService _playbackState;

    public MetadataEnricherService(HttpClient http, List<MetadataSource> sources)
    {
        _metadataService = new MetadataService(http, sources);
        _playbackState = PlaybackStateService.Instance;
    }

    public async Task EnrichAsync(ObservableCollection<VideoItem> videos, bool enableOnline, bool enableThumbnails)
    {
        var snapshot = videos.ToList();

        // Phase 1: Online metadata (sequential to avoid rate-limiting APIs)
        var thumbnailCandidates = new List<VideoItem>();
        foreach (var video in snapshot)
        {
            var filenameYear = FilenameUtils.ExtractYearFromFilename(video.Title);

            bool usedOnline = false;
            VideoMetadata? meta = null;
            if (enableOnline)
            {
                meta = await _metadataService.FetchAsync(video.Title);
                if (meta != null)
                {
                    if (filenameYear > 0 && meta.Year > 0 && meta.Year != filenameYear)
                    {
                        Logger.Warn($"Metadata mismatch for '{video.Title}': provider year {meta.Year} != filename year {filenameYear}, rejecting");
                        meta = null;
                    }
                }
                if (meta != null && (meta.Rating > 0 || meta.Year > 0 || !string.IsNullOrEmpty(meta.PosterUrl)))
                {
                    usedOnline = true;
                    if (meta.Rating > 0) video.Rating = meta.Rating;
                    if (meta.Year > 0) video.Year = meta.Year;
                    if (!string.IsNullOrEmpty(meta.PosterUrl))
                    {
                        video.ThumbnailUrl = meta.PosterUrl;
                        video.BannerUrl = meta.PosterUrl;
                    }
                }
            }

            if (!usedOnline)
                thumbnailCandidates.Add(video);

            if (video.Year == 0)
                video.Year = filenameYear;
        }

        // Phase 2: Generate thumbnails in parallel (max 3 concurrent VLC instances)
        if (enableThumbnails && thumbnailCandidates.Count > 0)
        {
            var throttle = new SemaphoreSlim(3, 3);
            var tasks = thumbnailCandidates.Select(async video =>
            {
                await throttle.WaitAsync();
                try
                {
                    var thumb = await ThumbnailService.GenerateThumbnailAsync(video.FilePath);
                    if (thumb != null)
                    {
                        video.ThumbnailUrl = thumb;
                        video.BannerUrl = thumb;
                    }
                }
                finally
                {
                    throttle.Release();
                }
            });
            await Task.WhenAll(tasks);
        }

        // Phase 3: Watched state
        foreach (var v in snapshot)
            v.IsWatched = _playbackState.IsWatched(v.FilePath);
    }
}
