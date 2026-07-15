using BPlayer.Models;
using BPlayer.Services;

namespace BPlayer.Tests;

[TestClass]
public class ScenePreviewConfigTests
{
    [TestMethod]
    public void ThumbnailCount_IsFive()
    {
        Assert.AreEqual(5, ScenePreviewConfig.ThumbnailCount);
    }

    [TestMethod]
    public void PickRandomPositions_ReturnsCorrectCount()
    {
        var positions = ScenePreviewConfig.PickRandomPositions();
        Assert.AreEqual(ScenePreviewConfig.ThumbnailCount, positions.Length);
    }

    [TestMethod]
    public void PickRandomPositions_AllInRange()
    {
        var positions = ScenePreviewConfig.PickRandomPositions();
        foreach (var p in positions)
        {
            Assert.IsTrue(p >= 0f && p <= 1f, $"Position {p} is out of [0,1] range");
        }
    }

    [TestMethod]
    public void PickRandomPositions_NoDuplicates()
    {
        var positions = ScenePreviewConfig.PickRandomPositions();
        Assert.AreEqual(positions.Length, positions.Distinct().Count());
    }

    [TestMethod]
    public void PickRandomPositions_CalledTwice_ReturnsDifferentOrderings()
    {
        var a = ScenePreviewConfig.PickRandomPositions();
        var b = ScenePreviewConfig.PickRandomPositions();
        var areSame = a.SequenceEqual(b);
        Assert.IsFalse(areSame, "Two consecutive calls produced identical orderings");
    }
}

[TestClass]
public class UrlConfigServiceTests
{
    [TestMethod]
    public void GetDefaults_ReturnsNonNull()
    {
        Assert.IsNotNull(UrlConfigService.GetDefaults());
    }

    [TestMethod]
    public void GetDefaults_HasThreePresets()
    {
        var presets = UrlConfigService.GetDefaults();
        Assert.AreEqual(3, presets.MetadataPresets.Count);
    }

    [TestMethod]
    public void GetDefaults_TmdbPreset_IsCorrect()
    {
        var tmdb = UrlConfigService.GetDefaults().MetadataPresets[0];
        Assert.AreEqual("TMDB", tmdb.Name);
        Assert.AreEqual("https://api.themoviedb.org/3/search/movie?api_key={key}&query={title}", tmdb.ApiUrl);
        Assert.AreEqual("https://image.tmdb.org/t/p/w500", tmdb.PosterBaseUrl);
        Assert.IsTrue(tmdb.IsBuiltIn);
        Assert.IsNotNull(tmdb.Fields);
        Assert.AreEqual("title", tmdb.Fields.Title);
        Assert.AreEqual("release_date", tmdb.Fields.Year);
        Assert.AreEqual("vote_average", tmdb.Fields.Rating);
        Assert.AreEqual("poster_path", tmdb.Fields.Poster);
        Assert.AreEqual("overview", tmdb.Fields.Plot);
    }

    [TestMethod]
    public void GetDefaults_OmdbPreset_IsBuiltIn()
    {
        var omdb = UrlConfigService.GetDefaults().MetadataPresets[1];
        Assert.AreEqual("OMDb", omdb.Name);
        Assert.AreEqual("https://www.omdbapi.com/?t={title}&apikey={key}", omdb.ApiUrl);
        Assert.IsTrue(omdb.IsBuiltIn);
    }

    [TestMethod]
    public void GetDefaults_CustomPreset_IsNotBuiltIn()
    {
        var custom = UrlConfigService.GetDefaults().MetadataPresets[2];
        Assert.AreEqual("Custom Example", custom.Name);
        Assert.IsFalse(custom.IsBuiltIn);
    }

    [TestMethod]
    public void GetDefaults_HasRatingServiceUrl()
    {
        Assert.AreEqual("https://www.omdbapi.com/", UrlConfigService.GetDefaults().RatingServiceUrl);
    }

    [TestMethod]
    public void GetDefaults_RatingServiceApiKey_Empty()
    {
        Assert.AreEqual("", UrlConfigService.GetDefaults().RatingServiceApiKey);
    }
}

[TestClass]
public class ThemesConfigTests
{
    [TestMethod]
    public void GetDefaultThemes_ReturnsFiveThemes()
    {
        var themes = ThemesConfig.GetDefaultThemes();
        Assert.AreEqual(5, themes.Count);
    }

    [TestMethod]
    public void GetDefaultThemes_HasExpectedNames()
    {
        var names = ThemesConfig.GetDefaultThemes().Select(t => t.Name).OrderBy(n => n).ToArray();
        CollectionAssert.AreEqual(
            new[] { "Amber", "Blue", "Dark", "Modern", "Red" },
            names);
    }

    [TestMethod]
    public void GetDefaultThemes_EachTheme_HasAllProperties()
    {
        foreach (var theme in ThemesConfig.GetDefaultThemes())
        {
            Assert.IsFalse(string.IsNullOrEmpty(theme.Name), "Name is empty");
            Assert.IsFalse(string.IsNullOrEmpty(theme.BgDark), $"{theme.Name}: BgDark is empty");
            Assert.IsFalse(string.IsNullOrEmpty(theme.BgSurface), $"{theme.Name}: BgSurface is empty");
            Assert.IsFalse(string.IsNullOrEmpty(theme.BgCard), $"{theme.Name}: BgCard is empty");
            Assert.IsFalse(string.IsNullOrEmpty(theme.BgCardHover), $"{theme.Name}: BgCardHover is empty");
            Assert.IsFalse(string.IsNullOrEmpty(theme.Accent), $"{theme.Name}: Accent is empty");
            Assert.IsFalse(string.IsNullOrEmpty(theme.AccentHover), $"{theme.Name}: AccentHover is empty");
            Assert.IsFalse(string.IsNullOrEmpty(theme.TextPrimary), $"{theme.Name}: TextPrimary is empty");
            Assert.IsFalse(string.IsNullOrEmpty(theme.TextSecondary), $"{theme.Name}: TextSecondary is empty");
            Assert.IsFalse(string.IsNullOrEmpty(theme.BorderColor), $"{theme.Name}: BorderColor is empty");
            Assert.IsFalse(string.IsNullOrEmpty(theme.ButtonBg), $"{theme.Name}: ButtonBg is empty");
            Assert.IsFalse(string.IsNullOrEmpty(theme.ButtonHoverBg), $"{theme.Name}: ButtonHoverBg is empty");
            Assert.IsFalse(string.IsNullOrEmpty(theme.ScrollThumb), $"{theme.Name}: ScrollThumb is empty");
        }
    }

    [TestMethod]
    public void GetDefaultThemes_EachTheme_HexColorsStartWithHash()
    {
        foreach (var theme in ThemesConfig.GetDefaultThemes())
        {
            Assert.IsTrue(theme.BgDark.StartsWith('#'), $"{theme.Name}: BgDark");
            Assert.IsTrue(theme.Accent.StartsWith('#'), $"{theme.Name}: Accent");
            Assert.IsTrue(theme.TextPrimary.StartsWith('#'), $"{theme.Name}: TextPrimary");
        }
    }
}

[TestClass]
public class VideoScannerServiceTests
{
    [TestMethod]
    public async Task ScanDirectoriesAsync_FindsVideoFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "movie.mp4"), "test");
            File.WriteAllText(Path.Combine(dir, "show.avi"), "test");
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "test");
            File.WriteAllText(Path.Combine(dir, "clip.mkv"), "test");

            var service = new VideoScannerService();
            var results = await service.ScanDirectoriesAsync(new[] { dir });

            Assert.AreEqual(3, results.Count);
            Assert.IsTrue(results.Any(v => v.FilePath.EndsWith(".mp4")));
            Assert.IsTrue(results.Any(v => v.FilePath.EndsWith(".avi")));
            Assert.IsTrue(results.Any(v => v.FilePath.EndsWith(".mkv")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public async Task ScanDirectoriesAsync_RecursiveScan()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var subdir = Path.Combine(dir, "sub");
        Directory.CreateDirectory(subdir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "root.mp4"), "test");
            File.WriteAllText(Path.Combine(subdir, "deep.mov"), "test");

            var service = new VideoScannerService();
            var results = await service.ScanDirectoriesAsync(new[] { dir });

            Assert.AreEqual(2, results.Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public async Task ScanDirectoriesAsync_EmptyDir_ReturnsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            var service = new VideoScannerService();
            var results = await service.ScanDirectoriesAsync(new[] { dir });
            Assert.AreEqual(0, results.Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public async Task ScanDirectoriesAsync_MissingDir_Skips()
    {
        var dir = @"X:\nonexistent_" + Guid.NewGuid();
        var service = new VideoScannerService();
        var results = await service.ScanDirectoriesAsync(new[] { dir });
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task ScanDirectoriesAsync_OnlyVideoExtensions()
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

            var service = new VideoScannerService();
            var results = await service.ScanDirectoriesAsync(new[] { dir });
            Assert.AreEqual(7, results.Count);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public async Task ScanDirectoriesAsync_ReturnsOrderedByTitle()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "zeta.mp4"), "test");
            File.WriteAllText(Path.Combine(dir, "alpha.mp4"), "test");
            File.WriteAllText(Path.Combine(dir, "beta.mp4"), "test");

            var service = new VideoScannerService();
            var results = await service.ScanDirectoriesAsync(new[] { dir });

            Assert.AreEqual(3, results.Count);
            Assert.AreEqual("alpha", results[0].Title);
            Assert.AreEqual("beta", results[1].Title);
            Assert.AreEqual("zeta", results[2].Title);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public async Task ScanDirectoriesAsync_CreatesVideoItemWithCorrectProperties()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);

        try
        {
            var filePath = Path.Combine(dir, "My Movie.mkv");
            File.WriteAllText(filePath, "test");
            var beforeCreation = File.GetCreationTime(filePath);

            var service = new VideoScannerService();
            var results = await service.ScanDirectoriesAsync(new[] { dir });

            Assert.AreEqual(1, results.Count);
            var item = results[0];
            Assert.AreEqual(filePath, item.FilePath);
            Assert.AreEqual("My Movie", item.Title);
            Assert.IsFalse(item.IsLoading);
            Assert.AreEqual(beforeCreation, item.AddedAt);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

[TestClass]
public class VideoItemTests
{
    [TestMethod]
    public void HasThumbnail_ReturnsTrue_WhenUrlSet()
    {
        var item = new VideoItem { ThumbnailUrl = "http://example.com/poster.jpg" };
        Assert.IsTrue(item.HasThumbnail);
    }

    [TestMethod]
    public void HasThumbnail_ReturnsFalse_WhenUrlNull()
    {
        var item = new VideoItem { ThumbnailUrl = null };
        Assert.IsFalse(item.HasThumbnail);
    }

    [TestMethod]
    public void HasThumbnail_ReturnsFalse_WhenUrlEmpty()
    {
        var item = new VideoItem { ThumbnailUrl = "" };
        Assert.IsFalse(item.HasThumbnail);
    }

    [TestMethod]
    public void EffectivePosterUrl_UsesCustomPosterPath_WhenSet()
    {
        var item = new VideoItem
        {
            ThumbnailUrl = "http://example.com/thumb.jpg",
            CustomPosterPath = @"C:\posters\custom.jpg"
        };
        Assert.AreEqual(@"C:\posters\custom.jpg", item.EffectivePosterUrl);
    }

    [TestMethod]
    public void EffectivePosterUrl_FallsBackToThumbnailUrl()
    {
        var item = new VideoItem { ThumbnailUrl = "http://example.com/thumb.jpg" };
        Assert.AreEqual("http://example.com/thumb.jpg", item.EffectivePosterUrl);
    }

    [TestMethod]
    public void EffectivePosterUrl_ReturnsEmpty_WhenNothingSet()
    {
        var item = new VideoItem();
        Assert.AreEqual("", item.EffectivePosterUrl);
    }

    [TestMethod]
    public void HasFlipImages_True_WhenDifferentUrls()
    {
        var item = new VideoItem
        {
            ThumbnailUrl = "http://example.com/a.jpg",
            BannerUrl = "http://example.com/b.jpg"
        };
        Assert.IsTrue(item.HasFlipImages);
    }

    [TestMethod]
    public void HasFlipImages_False_WhenSameUrls()
    {
        var item = new VideoItem
        {
            ThumbnailUrl = "http://example.com/a.jpg",
            BannerUrl = "http://example.com/a.jpg"
        };
        Assert.IsFalse(item.HasFlipImages);
    }

    [TestMethod]
    public void HasFlipImages_False_WhenNull()
    {
        var item = new VideoItem { ThumbnailUrl = null, BannerUrl = null };
        Assert.IsFalse(item.HasFlipImages);
    }

    [TestMethod]
    public void DisplayInfo_ShowsYear_WhenOnlyYear()
    {
        var item = new VideoItem { Year = 1999 };
        Assert.AreEqual("1999", item.DisplayInfo);
    }

    [TestMethod]
    public void DisplayInfo_ShowsRating_WhenOnlyRating()
    {
        var item = new VideoItem { Rating = 8.5 };
        Assert.AreEqual("★ 8.5", item.DisplayInfo);
    }

    [TestMethod]
    public void DisplayInfo_ShowsYearAndRating()
    {
        var item = new VideoItem { Year = 1999, Rating = 8.5 };
        Assert.AreEqual("1999  •  ★ 8.5", item.DisplayInfo);
    }

    [TestMethod]
    public void DisplayInfo_Empty_WhenNoYearOrRating()
    {
        var item = new VideoItem();
        Assert.AreEqual("", item.DisplayInfo);
    }

    [TestMethod]
    public void Initial_ReturnsFirstCharUpper()
    {
        var item = new VideoItem { Title = "alien" };
        Assert.AreEqual("A", item.Initial);
    }

    [TestMethod]
    public void Initial_ReturnsQuestionMark_WhenEmpty()
    {
        var item = new VideoItem { Title = "" };
        Assert.AreEqual("?", item.Initial);
    }

    [TestMethod]
    public void Folder_ReturnsDirectoryName()
    {
        var item = new VideoItem { FilePath = @"C:\Movies\Sci-Fi\blade-runner.mp4" };
        Assert.AreEqual("Sci-Fi", item.Folder);
    }

    [TestMethod]
    public void MediaInfoFileSizeFormatted_ShowsBytes()
    {
        var item = new VideoItem { MediaInfoFileSize = 500 };
        Assert.AreEqual("500 B", item.MediaInfoFileSizeFormatted);
    }

    [TestMethod]
    public void MediaInfoFileSizeFormatted_ShowsKB()
    {
        var item = new VideoItem { MediaInfoFileSize = 2048 };
        Assert.AreEqual("2.0 KB", item.MediaInfoFileSizeFormatted);
    }

    [TestMethod]
    public void MediaInfoFileSizeFormatted_ShowsMB()
    {
        var item = new VideoItem { MediaInfoFileSize = 5 * 1024 * 1024 };
        Assert.AreEqual("5.0 MB", item.MediaInfoFileSizeFormatted);
    }

    [TestMethod]
    public void MediaInfoFileSizeFormatted_ShowsGB()
    {
        var item = new VideoItem { MediaInfoFileSize = 3L * 1024 * 1024 * 1024 };
        Assert.AreEqual("3.00 GB", item.MediaInfoFileSizeFormatted);
    }

    [TestMethod]
    public void HasMediaInfo_True_WhenCodecSet()
    {
        var item = new VideoItem { MediaInfoCodec = "h264" };
        Assert.IsTrue(item.HasMediaInfo);
    }

    [TestMethod]
    public void HasMediaInfo_False_WhenNothingSet()
    {
        var item = new VideoItem();
        Assert.IsFalse(item.HasMediaInfo);
    }

    [TestMethod]
    public void PlaceholderColor_Stable_ForSamePath()
    {
        var a = new VideoItem { FilePath = @"C:\videos\movie.mp4" };
        var b = new VideoItem { FilePath = @"C:\videos\movie.mp4" };
        Assert.AreEqual(a.PlaceholderColor, b.PlaceholderColor);
    }

    [TestMethod]
    public void PlaceholderColor_ReturnsValidHex()
    {
        var item = new VideoItem { FilePath = @"C:\videos\any.mp4" };
        Assert.IsTrue(item.PlaceholderColor.StartsWith('#'));
        Assert.AreEqual(7, item.PlaceholderColor.Length);
    }
}
