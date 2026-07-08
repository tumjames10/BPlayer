namespace BPlayer.Models;

public class MetadataSource
{
    public string Name { get; set; } = "";
    public string ApiUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string JsonResponsePath { get; set; } = "";
    public string PosterBaseUrl { get; set; } = "";
    public FieldMapping Fields { get; set; } = new();
    public bool IsBuiltIn { get; set; }
}
