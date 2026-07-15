using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using BPlayer.Models;

namespace BPlayer.Services;

public class GenericJsonProvider : IMetadataProvider
{
    private readonly HttpClient _http;
    private readonly MetadataSource _config;

    public GenericJsonProvider(HttpClient http, MetadataSource config)
    {
        _http = http;
        _config = config;
    }

    public async Task<VideoMetadata?> FetchAsync(string title)
    {
        try
        {
            if (string.IsNullOrEmpty(_config.ApiUrl)) return null;

            var url = _config.ApiUrl
                .Replace("{title}", Uri.EscapeDataString(title))
                .Replace("{key}", Uri.EscapeDataString(_config.ApiKey ?? ""));

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme != "https")
                return null;

            Logger.Info($"GenericJsonProvider: requesting '{uri}'");
            var response = await _http.GetStringAsync(uri);
            Logger.Info($"GenericJsonProvider: response length={response.Length} for '{title}'");
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            var target = string.IsNullOrEmpty(_config.JsonResponsePath)
                ? root
                : TraversePath(root, _config.JsonResponsePath);

            if (target.ValueKind != JsonValueKind.Object) return null;

            var meta = new VideoMetadata();

            if (TryGetString(target, _config.Fields.Title, out var t))
                meta.Title = t;

            if (TryGetString(target, _config.Fields.Year, out var yrStr))
            {
                // Handle "2023" or "2023–2025" or "2023-05-15"
                yrStr = (yrStr ?? "").Split('–', '-', '/')[0];
                if (int.TryParse(yrStr, out var yr) && yr >= 1900 && yr <= 2099)
                    meta.Year = yr;
            }

            if (TryGetDouble(target, _config.Fields.Rating, out var rating))
                meta.Rating = rating;

            if (TryGetString(target, _config.Fields.Poster, out var poster) && !string.IsNullOrEmpty(poster) && poster != "N/A")
            {
                if (!string.IsNullOrEmpty(_config.PosterBaseUrl) && !poster.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    meta.PosterUrl = _config.PosterBaseUrl.TrimEnd('/') + "/" + poster.TrimStart('/');
                else
                    meta.PosterUrl = poster;
            }

            if (TryGetString(target, _config.Fields.Plot, out var plot) && plot != "N/A")
                meta.Plot = plot;

            // Return null if no useful data was extracted
            if (string.IsNullOrEmpty(meta.Title) && meta.Year == 0 && meta.Rating == 0
                && string.IsNullOrEmpty(meta.PosterUrl) && string.IsNullOrEmpty(meta.Plot))
                return null;

            return meta;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Metadata fetch failed: {ex.Message}");
            return null;
        }
    }

    private static JsonElement TraversePath(JsonElement root, string path)
    {
        var parts = path.Split('.', '/');
        var current = root;

        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object && current.ValueKind != JsonValueKind.Array)
                return default;

            // Handle array index syntax: results[0]
            var bracketIdx = part.IndexOf('[');
            if (bracketIdx >= 0)
            {
                var name = part[..bracketIdx];
                var idxStr = part[(bracketIdx + 1)..^1];
                if (!int.TryParse(idxStr, out var idx)) return default;

                if (!string.IsNullOrEmpty(name))
                {
                    if (!current.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
                        return default;
                    current = arr;
                }

                if (current.ValueKind == JsonValueKind.Array && idx < current.GetArrayLength())
                    current = current[idx];
                else
                    return default;
            }
            else if (!string.IsNullOrEmpty(part))
            {
                if (!current.TryGetProperty(part, out current))
                    return default;
            }
        }

        return current;
    }

    private static bool TryGetString(JsonElement obj, string path, out string? value)
    {
        value = null;
        try
        {
            var el = TraversePath(obj, path);
            if (el.ValueKind == JsonValueKind.String) { value = el.GetString(); return true; }
            if (el.ValueKind == JsonValueKind.Number) { value = el.GetRawText(); return true; }
        }
        catch { }
        return false;
    }

    private static bool TryGetDouble(JsonElement obj, string path, out double value)
    {
        value = 0;
        try
        {
            var el = TraversePath(obj, path);
            if (el.ValueKind == JsonValueKind.Number) { value = el.GetDouble(); return true; }
            if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), out var d)) { value = d; return true; }
        }
        catch { }
        return false;
    }

}
