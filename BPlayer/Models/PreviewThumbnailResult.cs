namespace BPlayer.Models;

public class PreviewThumbnailResult
{
    public List<string> FilePaths { get; set; } = new();
    public float[] Positions { get; set; } = Array.Empty<float>();
}
