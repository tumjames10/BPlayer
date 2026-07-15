using System.IO;
using System.Text.Json;
using BPlayer.Models;

namespace BPlayer.Services;

public class PlaybackStateService
{
    private static PlaybackStateService? _instance;
    public static PlaybackStateService Instance => _instance ??= new PlaybackStateService();

    private static readonly string StoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BPlayer", "playback.json");

    private readonly Dictionary<string, VideoPlaybackState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private PlaybackStateService()
    {
        Load();
    }

    private void Load()
    {
        if (!File.Exists(StoragePath))
            return;

        try
        {
            var json = File.ReadAllText(StoragePath);
            var list = JsonSerializer.Deserialize<List<VideoPlaybackState>>(json);
            if (list == null) return;

            lock (_lock)
            {
                _states.Clear();
                foreach (var state in list)
                {
                    if (!string.IsNullOrEmpty(state.FilePath))
                        _states[state.FilePath] = state;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load playback states: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StoragePath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            List<VideoPlaybackState> list;
            lock (_lock)
            {
                list = new List<VideoPlaybackState>(_states.Values);
            }

            var json = JsonSerializer.Serialize(list, JsonOptions);
            File.WriteAllText(StoragePath, json);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to save playback states: {ex.Message}");
        }
    }

    public double? GetResumePosition(string filePath)
    {
        lock (_lock)
        {
            if (_states.TryGetValue(filePath, out var state) && !state.IsWatched && state.PositionSeconds > 5)
                return state.PositionSeconds;
        }
        return null;
    }

    public void SavePosition(string filePath, double positionSeconds)
    {
        lock (_lock)
        {
            if (_states.TryGetValue(filePath, out var state))
            {
                state.PositionSeconds = positionSeconds;
                state.LastPlayed = DateTime.Now;
            }
            else
            {
                _states[filePath] = new VideoPlaybackState
                {
                    FilePath = filePath,
                    PositionSeconds = positionSeconds,
                    IsWatched = false,
                    LastPlayed = DateTime.Now
                };
            }
        }
        Save();
    }

    public void MarkWatched(string filePath)
    {
        lock (_lock)
        {
            if (_states.TryGetValue(filePath, out var state))
                state.IsWatched = true;
        }
        Save();
    }

    public void MarkUnwatched(string filePath)
    {
        lock (_lock)
        {
            _states.Remove(filePath);
        }
        Save();
    }

    public bool IsWatched(string filePath)
    {
        lock (_lock)
        {
            return _states.TryGetValue(filePath, out var state) && state.IsWatched;
        }
    }
}
