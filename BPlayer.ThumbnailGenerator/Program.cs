using System.Diagnostics;
using BPlayer.ThumbnailCore;

var dirs = new List<string>();
var cacheDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "BPlayer", "thumbcache");
var throttleMs = 200;
var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "BPlayer", "gen-log.txt");

// Parse args
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--dir" && i + 1 < args.Length)
        dirs.Add(args[++i]);
    else if (args[i] == "--cache" && i + 1 < args.Length)
        cacheDir = args[++i];
    else if (args[i] == "--throttle" && i + 1 < args.Length && int.TryParse(args[++i], out var t))
        throttleMs = Math.Max(0, t);
    else if (args[i] == "--log" && i + 1 < args.Length)
        logPath = args[++i];
    else if (args[i] == "--help" || args[i] == "-h")
    {
        Console.WriteLine("Usage: BPlayer.ThumbnailGenerator [options]");
        Console.WriteLine("  --dir <path>       Directory to scan (repeat for multiple)");
        Console.WriteLine("  --cache <path>     Thumbnail cache directory");
        Console.WriteLine("  --throttle <ms>    Delay between files (default 200)");
        Console.WriteLine("  --log <path>       Log file path");
        Console.WriteLine("  --help             Show this help");
        return;
    }
}

if (dirs.Count == 0)
{
    Console.Error.WriteLine("Error: No directories specified. Use --dir <path>");
    Environment.ExitCode = 1;
    return;
}

// Auto-detect throttle from first directory's drive
if (args.All(a => !a.StartsWith("--throttle")))
{
    var sampleDir = dirs.FirstOrDefault(Directory.Exists);
    if (sampleDir != null)
    {
        var detected = DriveDetector.RecommendThrottleMs(sampleDir);
        if (detected != throttleMs)
        {
            throttleMs = detected;
        }
    }
}

// Setup logging
var sw = new StreamWriter(logPath, append: true) { AutoFlush = true };
void Log(string level, string msg)
{
    var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}";
    sw.WriteLine(line);
    Console.WriteLine(line);
}

Log("INFO", $"Thumbnail Generator started");
Log("INFO", $"Cache dir: {cacheDir}");
Log("INFO", $"Throttle: {throttleMs}ms");
Log("INFO", $"Directories: {string.Join("; ", dirs)}");

// Add libvlc to PATH
var baseDir = AppDomain.CurrentDomain.BaseDirectory;
var vlcDir = Path.Combine(baseDir, "libvlc", "win-x64");
if (Directory.Exists(vlcDir))
{
    var path = Environment.GetEnvironmentVariable("PATH") ?? "";
    if (!path.Contains(vlcDir, StringComparison.OrdinalIgnoreCase))
        Environment.SetEnvironmentVariable("PATH", vlcDir + ";" + path);
    Log("INFO", $"libvlc path: {vlcDir}");
}

// Scan for video files
Log("INFO", "Scanning for video files...");
var watch = Stopwatch.StartNew();
var videoFiles = VideoScanner.Scan(dirs);
watch.Stop();
Log("INFO", $"Found {videoFiles.Count} video files in {watch.Elapsed.TotalSeconds:F1}s");

if (videoFiles.Count == 0)
{
    Log("WARN", "No video files found. Nothing to do.");
    return;
}

// Setup progress tracker
var progress = new ProgressTracker(cacheDir);
progress.LoadOrCreate(videoFiles);


Log("INFO", $"Progress: {progress.CompletedFiles} done, {progress.FailedFiles} failed, {progress.PendingFiles} pending");

// Handle Ctrl+C
var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
    Log("WARN", "Interrupted — saving progress...");
};

// Process files
var thumbnailer = new VideoThumbnailer(cacheDir);
thumbnailer.LogInfo = msg => Log("INFO", msg);
thumbnailer.LogWarn = msg => Log("WARN", msg);

var processed = 0;
var pending = progress.GetPendingFiles().ToList();
Log("INFO", $"Processing {pending.Count} pending files...");

foreach (var filePath in pending)
{
    if (cancel.Token.IsCancellationRequested) break;

    Log("INFO", $"[{processed + 1}/{pending.Count}] {Path.GetFileName(filePath)}");
    progress.MarkProcessing(filePath);

    var result = thumbnailer.Generate(filePath);

    if (result.Success && result.CachedPath != null)
    {
        progress.MarkDone(filePath, result.CachedPath);
        Log("INFO", $"  OK ({Path.GetFileName(result.CachedPath)})");
    }
    else
    {
        progress.MarkFailed(filePath, result.Error ?? "Unknown error");
        Log("WARN", $"  FAILED: {result.Error}");
    }

    processed++;

    // Throttle between files to avoid hammering the HDD
    if (processed < pending.Count && throttleMs > 0)
        Thread.Sleep(throttleMs);
}

Log("INFO", $"Done. Processed {processed} files. Total: {progress.CompletedFiles} done, {progress.FailedFiles} failed.");
Environment.ExitCode = progress.FailedFiles > 0 ? 1 : 0;
