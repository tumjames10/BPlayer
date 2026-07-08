using System.Collections.Generic;

namespace BPlayer.Models;

public class UrlPresets
{
    public List<MetadataSource> MetadataPresets { get; set; } = new();
    public string? RatingServiceUrl { get; set; }
    public string? RatingServiceApiKey { get; set; }
}
