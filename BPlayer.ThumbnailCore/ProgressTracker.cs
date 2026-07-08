using System.Text.Json;

namespace BPlayer.ThumbnailCore;

public class ThumbnailJob
{
    public string FilePath { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? CachedPath { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
}

public class ProgressState
{
    public string Version { get; set; } = "1";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public List<ThumbnailJob> Files { get; set; } = new();
}

public class ProgressTracker
{
    private readonly string _statePath;
    private ProgressState _state = new();

    public ProgressTracker(string cacheDir)
    {
        _statePath = Path.Combine(cacheDir, "progress.json");
    }

    public int TotalFiles => _state.Files.Count;
    public int CompletedFiles => _state.Files.Count(j => j.Status == "done");
    public int FailedFiles => _state.Files.Count(j => j.Status == "failed");
    public int PendingFiles => _state.Files.Count(j => j.Status == "pending");

    public void LoadOrCreate(IEnumerable<string> videoFiles)
    {
        if (File.Exists(_statePath))
        {
            try
            {
                var json = File.ReadAllText(_statePath);
                var loaded = JsonSerializer.Deserialize<ProgressState>(json);
                if (loaded != null)
                {
                    _state = loaded;
                    // Merge in any new files not in state
                    var known = new HashSet<string>(_state.Files.Select(f => f.FilePath), StringComparer.OrdinalIgnoreCase);
                    foreach (var f in videoFiles)
                    {
                        if (!known.Contains(f))
                            _state.Files.Add(new ThumbnailJob { FilePath = f });
                    }
                    // Reset any "processing" files to "pending" (crashed mid-way)
                    foreach (var job in _state.Files)
                    {
                        if (job.Status == "processing")
                            job.Status = "pending";
                    }
                    Save();
                    return;
                }
            }
            catch { }
        }
        _state = new ProgressState
        {
            StartedAt = DateTime.UtcNow,
            Files = videoFiles.Select(f => new ThumbnailJob { FilePath = f }).ToList()
        };
        Save();
    }

    public IEnumerable<string> GetPendingFiles()
    {
        return _state.Files
            .Where(j => j.Status == "pending" && j.Attempts < 3)
            .OrderBy(j => j.FilePath)
            .Select(j => j.FilePath);
    }

    public void MarkProcessing(string filePath)
    {
        var job = _state.Files.FirstOrDefault(j =>
            j.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (job != null)
        {
            job.Status = "processing";
            job.Attempts++;
        }
    }

    public void MarkDone(string filePath, string cachedPath)
    {
        var job = _state.Files.FirstOrDefault(j =>
            j.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (job != null)
        {
            job.Status = "done";
            job.CachedPath = cachedPath;
            job.Error = null;
        }
        Save();
    }

    public void MarkFailed(string filePath, string error)
    {
        var job = _state.Files.FirstOrDefault(j =>
            j.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        if (job != null)
        {
            job.Status = "failed";
            job.Error = error;
        }
        Save();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_statePath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            // Write to temp then atomically replace
            var tmp = _statePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_statePath)) File.Delete(_statePath);
            File.Move(tmp, _statePath);
        }
        catch { }
    }
}
