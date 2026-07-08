using System.Threading.Tasks;
using BPlayer.Models;

namespace BPlayer.Services;

public interface IMetadataProvider
{
    Task<VideoMetadata?> FetchAsync(string title);
}
