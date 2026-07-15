namespace BPlayer.Models;

public class SourceItem
{
    public MetadataSource Source { get; }
    public string Display => Source.Name;
    public SourceItem(MetadataSource source) => Source = source;
}
