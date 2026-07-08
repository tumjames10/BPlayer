using System.Collections.Generic;

namespace BPlayer.Models;

public class Collection
{
    public string Name { get; set; } = "";
    public List<string> VideoPaths { get; set; } = new();
}
