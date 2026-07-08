using System.Collections.Generic;

namespace BPlayer.Models;

public class Playlist
{
    public string Name { get; set; } = "New Playlist";
    public List<string> VideoPaths { get; set; } = new();
}
