using System.Net.Http;
using System.Text.Json;

namespace BPlayer.Services;

public class RatingService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    public RatingService(HttpClient http)
    {
        _http = http;
        var config = UrlConfigService.Load();
        _baseUrl = config.RatingServiceUrl ?? "https://www.omdbapi.com/";
        _apiKey = config.RatingServiceApiKey ?? "";
    }

    public async Task<double> FetchRatingAsync(string title)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            Logger.Warn("RatingService: no API key configured");
            return 0;
        }

        try
        {
            var url = $"{_baseUrl}?t={Uri.EscapeDataString(title)}&apikey={_apiKey}";
            var response = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("imdbRating", out var ratingEl) &&
                double.TryParse(ratingEl.GetString(), out var rating))
            {
                return rating;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"RatingService: failed to fetch rating for '{title}': {ex.Message}");
        }

        return 0;
    }
}
