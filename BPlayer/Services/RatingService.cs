using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

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
        _apiKey = config.RatingServiceApiKey ?? "trilogy";
    }

    public async Task<double> FetchRatingAsync(string title)
    {
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
        catch { }

        return 0;
    }
}
