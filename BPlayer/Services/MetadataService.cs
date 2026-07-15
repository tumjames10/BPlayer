using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using BPlayer.Models;

namespace BPlayer.Services;

public class MetadataService
{
    private readonly HttpClient _http;
    private readonly List<MetadataSource> _sources;

    public MetadataService(HttpClient http, List<MetadataSource> sources)
    {
        _http = http;
        _sources = sources;
    }

    public async Task<VideoMetadata?> FetchAsync(string title)
    {
        Logger.Info($"MetadataService: fetching '{title}' from {_sources.Count} source(s)");

        foreach (var source in _sources)
        {
            Logger.Info($"MetadataService: trying source '{source.Name}' for '{title}'");
            var provider = new GenericJsonProvider(_http, source);
            var result = await provider.FetchAsync(title);

            // Fallback 1: try without trailing year
            if (result == null)
            {
                var withoutYear = System.Text.RegularExpressions.Regex.Replace(title, @"\s+\d{4}$", "").Trim();
                if (withoutYear != title && withoutYear.Length >= 2)
                {
                    Logger.Info($"MetadataService: retrying '{title}' without year -> '{withoutYear}'");
                    result = await provider.FetchAsync(withoutYear);
                }
            }

            // Fallback 2: try first word only (handles noisy filenames with release groups etc.)
            if (result == null)
            {
                var firstWord = title.Split(' ')[0];
                if (firstWord.Length >= 3 && firstWord != title)
                {
                    Logger.Info($"MetadataService: retrying '{title}' with first word -> '{firstWord}'");
                    result = await provider.FetchAsync(firstWord);
                }
            }

            if (result != null)
            {
                Logger.Info($"MetadataService: FOUND from '{source.Name}' for '{title}' — PosterUrl='{result.PosterUrl}', Rating={result.Rating}, Year={result.Year}");
                return result;
            }

            Logger.Info($"MetadataService: no data from '{source.Name}' for '{title}'");
        }

        Logger.Info($"MetadataService: all sources exhausted for '{title}' — returning null");
        return null;
    }

}
