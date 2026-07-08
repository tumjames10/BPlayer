using System;

namespace BPlayer.Models;

public class MediaInfoResult
{
    public long FileSize { get; set; }
    public string? VideoCodec { get; set; }
    public string Resolution { get; set; } = "";
    public int Bitrate { get; set; }
    public TimeSpan Duration { get; set; }
}
