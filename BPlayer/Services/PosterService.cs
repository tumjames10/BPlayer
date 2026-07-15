using BPlayer.Models;

namespace BPlayer.Services;

public class PosterService
{
    private readonly MetadataService _metadataService;

    public PosterService(MetadataService metadataService)
    {
        _metadataService = metadataService;
    }

    public async Task<PosterFetchResult?> FetchAsync(string title)
    {
        var cleanTitle = FilenameUtils.CleanTitleForSearch(title);
        Logger.Info($"PosterService: fetching for '{title}' (clean='{cleanTitle}')");
        var meta = await _metadataService.FetchAsync(cleanTitle);
        if (meta != null && (meta.Rating > 0 || meta.Year > 0 || !string.IsNullOrEmpty(meta.PosterUrl)))
        {
            Logger.Info($"PosterService: got result for '{title}' — PosterUrl='{meta.PosterUrl}', Rating={meta.Rating}, Year={meta.Year}");
            return new PosterFetchResult { Rating = meta.Rating, Year = meta.Year, PosterUrl = meta.PosterUrl };
        }
        Logger.Info($"PosterService: no useful result for '{title}'");
        return null;
    }
}
