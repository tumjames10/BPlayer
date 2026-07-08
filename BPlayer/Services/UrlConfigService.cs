using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BPlayer.Models;

namespace BPlayer.Services;

public static class UrlConfigService
{
    private static UrlPresets? _cache;
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "urls.json");

    public static UrlPresets Load()
    {
        if (_cache != null) return _cache;

        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var presets = JsonSerializer.Deserialize<UrlPresets>(json);
                if (presets != null)
                {
                    _cache = presets;
                    return _cache;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load url config: {ex.Message}");
        }

        _cache = GetDefaults();
        return _cache;
    }

    public static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_cache ?? GetDefaults(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }

    public static UrlPresets GetDefaults() => new()
    {
        RatingServiceUrl = "https://www.omdbapi.com/",
        RatingServiceApiKey = "",
        MetadataPresets = new List<MetadataSource>
        {
            new()
            {
                Name = "TMDB",
                ApiUrl = "https://api.themoviedb.org/3/search/movie?api_key={key}&query={title}",
                ApiKey = "",
                JsonResponsePath = "results[0]",
                PosterBaseUrl = "https://image.tmdb.org/t/p/w500",
                IsBuiltIn = true,
                Fields = new FieldMapping
                {
                    Title = "title", Year = "release_date",
                    Rating = "vote_average", Poster = "poster_path", Plot = "overview"
                }
            },
            new()
            {
                Name = "OMDb",
                ApiUrl = "https://www.omdbapi.com/?t={title}&apikey={key}",
                ApiKey = "",
                IsBuiltIn = true,
                Fields = new FieldMapping()
            },
            new()
            {
                Name = "Custom Example",
                ApiUrl = "https://api.example.com/?q={title}",
                Fields = new FieldMapping()
            }
        }
    };
}
