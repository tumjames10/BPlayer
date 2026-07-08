using BPlayer.ThumbnailCore;

namespace BPlayer.Tests;

[TestClass]
public class ThumbnailCoreTests
{
    // ── ProgressTracker Tests ──

    [TestMethod]
    public void ProgressTracker_LoadOrCreate_CreatesNewState()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tracker = new ProgressTracker(dir);
        var files = new[] { @"C:\videos\a.mp4", @"C:\videos\b.mkv" };

        tracker.LoadOrCreate(files);

        Assert.AreEqual(2, tracker.TotalFiles);
        Assert.AreEqual(0, tracker.CompletedFiles);
        Assert.AreEqual(0, tracker.FailedFiles);
        Assert.AreEqual(2, tracker.PendingFiles);
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void ProgressTracker_MarkDone_TracksCompletion()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tracker = new ProgressTracker(dir);
        var files = new[] { @"C:\videos\a.mp4" };

        tracker.LoadOrCreate(files);
        tracker.MarkDone(files[0], @"C:\cache\a.jpg");

        Assert.AreEqual(1, tracker.CompletedFiles);
        Assert.AreEqual(0, tracker.PendingFiles);
        Assert.AreEqual(0, tracker.FailedFiles);
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void ProgressTracker_MarkFailed_TracksFailure()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tracker = new ProgressTracker(dir);
        var files = new[] { @"C:\videos\a.mp4" };

        tracker.LoadOrCreate(files);
        tracker.MarkFailed(files[0], "VLC error");

        Assert.AreEqual(1, tracker.FailedFiles);
        Assert.AreEqual(0, tracker.CompletedFiles);
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void ProgressTracker_Resume_SkipsDoneFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tracker1 = new ProgressTracker(dir);
        var files = new[] { @"C:\videos\a.mp4", @"C:\videos\b.mkv", @"C:\videos\c.avi" };

        // First run: process a.mp4 and b.mkv
        tracker1.LoadOrCreate(files);
        tracker1.MarkDone(files[0], @"C:\cache\a.jpg");
        tracker1.MarkFailed(files[1], "error");

        // Second run: should only have c.avi pending
        var tracker2 = new ProgressTracker(dir);
        tracker2.LoadOrCreate(files);

        Assert.AreEqual(1, tracker2.CompletedFiles);  // a.mp4 done
        Assert.AreEqual(1, tracker2.FailedFiles);     // b.mkv failed
        Assert.AreEqual(1, tracker2.PendingFiles);    // c.avi pending
        Assert.AreEqual(files[2], tracker2.GetPendingFiles().First());
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void ProgressTracker_Resume_ResetsProcessingFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tracker1 = new ProgressTracker(dir);
        var files = new[] { @"C:\videos\a.mp4" };

        // Simulate a crash mid-processing
        tracker1.LoadOrCreate(files);
        tracker1.MarkProcessing(files[0]);
        // (no MarkDone — simulates crash)
        tracker1.Save();

        // Resume: should reset "processing" to "pending"
        var tracker2 = new ProgressTracker(dir);
        tracker2.LoadOrCreate(files);

        Assert.AreEqual(1, tracker2.PendingFiles);
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void ProgressTracker_MergeNewFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tracker1 = new ProgressTracker(dir);
        var files1 = new[] { @"C:\videos\a.mp4" };

        tracker1.LoadOrCreate(files1);
        tracker1.MarkDone(files1[0], @"C:\cache\a.jpg");

        // Add new file not in original set
        var files2 = new[] { @"C:\videos\a.mp4", @"C:\videos\b.mkv" };
        var tracker2 = new ProgressTracker(dir);
        tracker2.LoadOrCreate(files2);

        Assert.AreEqual(1, tracker2.CompletedFiles);  // a.mp4 still done
        Assert.AreEqual(1, tracker2.PendingFiles);    // b.mkv new
        Assert.AreEqual(2, tracker2.TotalFiles);
        Directory.Delete(dir, true);
    }

    // ── VideoScanner Tests ──

    [TestMethod]
    public void VideoScanner_FindsVideoFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "movie.mp4"), "test");
            File.WriteAllText(Path.Combine(dir, "show.avi"), "test");
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "test");
            File.WriteAllText(Path.Combine(dir, "clip.mkv"), "test");

            var results = VideoScanner.Scan(new[] { dir });

            Assert.AreEqual(3, results.Count);
            Assert.IsTrue(results.Any(r => r.EndsWith(".mp4")));
            Assert.IsTrue(results.Any(r => r.EndsWith(".avi")));
            Assert.IsTrue(results.Any(r => r.EndsWith(".mkv")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void VideoScanner_RecursiveScan()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var subdir = Path.Combine(dir, "sub");
        Directory.CreateDirectory(subdir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "root.mp4"), "test");
            File.WriteAllText(Path.Combine(subdir, "deep.mov"), "test");

            var results = VideoScanner.Scan(new[] { dir });

            Assert.AreEqual(2, results.Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void VideoScanner_EmptyDir_ReturnsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            var results = VideoScanner.Scan(new[] { dir });
            Assert.AreEqual(0, results.Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void VideoScanner_MissingDir_Skips()
    {
        var dir = @"X:\nonexistent_" + Guid.NewGuid();
        var results = VideoScanner.Scan(new[] { dir });
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void VideoScanner_OnlyVideoExtensions()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            foreach (var ext in new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" })
                File.WriteAllText(Path.Combine(dir, "file" + ext), "test");

            File.WriteAllText(Path.Combine(dir, "file.jpg"), "test");
            File.WriteAllText(Path.Combine(dir, "file.srt"), "test");
            File.WriteAllText(Path.Combine(dir, "file.nfo"), "test");

            var results = VideoScanner.Scan(new[] { dir });
            Assert.AreEqual(7, results.Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
