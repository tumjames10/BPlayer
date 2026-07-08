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
        foreach (var source in _sources)
        {
            var provider = new GenericJsonProvider(_http, source);
            var result = await provider.FetchAsync(title);

            if (result == null)
            {
                var cleaned = CleanTitle(title);
                var shortTitle = cleaned.Split(' ', '-', '_', '.')[0];
                if (shortTitle.Length >= 2)
                    result = await provider.FetchAsync(shortTitle);
            }

            if (result != null)
            {
                Logger.Info($"Metadata found from '{source.Name}' for '{title}'");
                return result;
            }
        }

        return null;
    }

    private static string CleanTitle(string raw) => TitleCleaner.Clean(raw);
}
