using System;
using System.IO;
using System.Threading.Tasks;
using BPlayer.Models;
using LibVLCSharp.Shared;

namespace BPlayer.Services;

public class MediaInfoService : IDisposable
{
    private LibVLC? _libVlc;
    private bool _initFailed;

    public Task<MediaInfoResult?> GetMediaInfoAsync(string filePath)
    {
        return Task.Run(() => GetMediaInfo(filePath));
    }

    private MediaInfoResult? GetMediaInfo(string filePath)
    {
        try
        {
            if (_initFailed) return null;
            if (!_initFailed && _libVlc == null) InitVlc();
            if (_libVlc == null) { _initFailed = true; return null; }

            var fi = new FileInfo(filePath);
            if (!fi.Exists) return null;

            var result = new MediaInfoResult
            {
                FileSize = fi.Length
            };

            try
            {
                using var media = new Media(_libVlc, filePath);
                media.Parse();

                result.Duration = TimeSpan.FromMilliseconds(media.Duration);

                if (media.Tracks != null)
                {
                    foreach (var track in media.Tracks)
                    {
                        if (track.TrackType == TrackType.Video)
                        {
                            result.VideoCodec = FourCCToString(track.Codec);
                            result.Bitrate = (int)track.Bitrate;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"MediaInfoService: error processing track: {ex.Message}");
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Warn($"MediaInfoService: failed to get media info: {ex.Message}");
            return null;
        }
    }

    private static string? FourCCToString(uint fourcc)
    {
        try
        {
            var bytes = new[]
            {
                (byte)((fourcc >> 0) & 0xFF),
                (byte)((fourcc >> 8) & 0xFF),
                (byte)((fourcc >> 16) & 0xFF),
                (byte)((fourcc >> 24) & 0xFF)
            };
            var str = System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0');
            return string.IsNullOrEmpty(str) ? null : str;
        }
        catch (Exception ex)
        {
            Logger.Warn($"MediaInfoService: FourCC conversion failed: {ex.Message}");
            return null;
        }
    }

    private void InitVlc()
    {
        try
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            var vlcDir = Path.Combine(dir, "libvlc", "win-x64");
            var pluginPath = Path.Combine(vlcDir, "plugins");
            if (!Directory.Exists(vlcDir) || !File.Exists(Path.Combine(vlcDir, "libvlc.dll")))
            {
                _initFailed = true;
                return;
            }
            _libVlc = new LibVLC("--quiet", "--no-osd", "--plugin-path=" + pluginPath);
        }
        catch (Exception ex)
        {
            Logger.Warn($"MediaInfoService: VLC init failed: {ex.Message}");
            _initFailed = true;
            _libVlc = null;
        }
    }

    public void Dispose()
    {
        _libVlc?.Dispose();
        _libVlc = null;
    }
}
