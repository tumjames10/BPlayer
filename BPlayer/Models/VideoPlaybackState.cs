namespace BPlayer.Models;

internal class VideoPlaybackState
{
    public string FilePath { get; set; } = string.Empty;
    public double PositionSeconds { get; set; }
    public bool IsWatched { get; set; }
    public DateTime LastPlayed { get; set; }
}
