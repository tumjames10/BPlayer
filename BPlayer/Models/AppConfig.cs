using System.Collections.Generic;
using BPlayer.Services;

namespace BPlayer.Models;

public class AppConfig
{
    public List<string> VideoSourcePaths { get; set; } = new();
    public List<Playlist> Playlists { get; set; } = new();
    public List<Collection> Collections { get; set; } = new();
    public List<VideoItem> Videos { get; set; } = new();
    public bool EnableOnlineMetadata { get; set; } = true;
    public bool EnableVideoThumbnails { get; set; } = true;
    public List<MetadataSource> MetadataSources { get; set; } = new();
    public string? SelectedTheme { get; set; }
    public string SubtitleFontFamily { get; set; } = "Arial";
    public int SubtitleFontSize { get; set; } = 16;
}
