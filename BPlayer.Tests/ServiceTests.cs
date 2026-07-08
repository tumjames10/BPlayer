using BPlayer.Services;

namespace BPlayer.Tests;

[TestClass]
public class ServiceTests
{
    // ── TitleCleaner Tests ──

    [TestMethod]
    public void TitleCleaner_RemovesSuffixes()
    {
        Assert.AreEqual("The Movie", TitleCleaner.Clean("The Movie 1080p WEBRip x264"));
    }

    [TestMethod]
    public void TitleCleaner_RemovesYearInParentheses()
    {
        Assert.AreEqual("The Movie", TitleCleaner.Clean("The Movie (2023)"));
    }

    [TestMethod]
    public void TitleCleaner_RemovesTrailingYear()
    {
        Assert.AreEqual("The Movie", TitleCleaner.Clean("The Movie 2023"));
    }

    [TestMethod]
    public void TitleCleaner_ReplacesSeparators()
    {
        Assert.AreEqual("The Movie", TitleCleaner.Clean("The.Movie.2023"));
        Assert.AreEqual("The Movie", TitleCleaner.Clean("The_Movie_2023"));
    }

    [TestMethod]
    public void TitleCleaner_TrimsWhitespace()
    {
        Assert.AreEqual("Title", TitleCleaner.Clean("  Title  2022  "));
    }

    [TestMethod]
    public void TitleCleaner_HandlesEmptyString()
    {
        Assert.AreEqual("", TitleCleaner.Clean(""));
    }

    [TestMethod]
    public void TitleCleaner_HandlesOnlyYear()
    {
        Assert.AreEqual("", TitleCleaner.Clean("2023"));
    }

    [TestMethod]
    public void TitleCleaner_RetainsShortNamesWithoutYear()
    {
        Assert.AreEqual("Alien", TitleCleaner.Clean("Alien"));
    }

    // ── FormattingUtils Tests ──

    [TestMethod]
    public void FormatTime_ShowsHoursMinutesSeconds_WhenOverAnHour()
    {
        Assert.AreEqual("1:23:45", FormattingUtils.FormatTime(new TimeSpan(1, 23, 45)));
    }

    [TestMethod]
    public void FormatTime_ShowsMinutesSeconds_WhenUnderAnHour()
    {
        Assert.AreEqual("42:05", FormattingUtils.FormatTime(new TimeSpan(0, 42, 5)));
    }

    [TestMethod]
    public void FormatTime_PadsMinutesAndSeconds()
    {
        Assert.AreEqual("5:04", FormattingUtils.FormatTime(new TimeSpan(0, 5, 4)));
    }

    [TestMethod]
    public void FormatTime_HandlesZero()
    {
        Assert.AreEqual("0:00", FormattingUtils.FormatTime(TimeSpan.Zero));
    }

    [TestMethod]
    public void FormatTime_HandlesExactlyOneHour()
    {
        Assert.AreEqual("1:00:00", FormattingUtils.FormatTime(new TimeSpan(1, 0, 0)));
    }

    // ── FilenameUtils Tests ──

    [TestMethod]
    public void ExtractYearFromFilename_FindsFourDigitYear()
    {
        Assert.AreEqual(2023, FilenameUtils.ExtractYearFromFilename("The Movie 2023"));
    }

    [TestMethod]
    public void ExtractYearFromFilename_FindsYearInParens()
    {
        Assert.AreEqual(1999, FilenameUtils.ExtractYearFromFilename("The Matrix (1999)"));
    }

    [TestMethod]
    public void ExtractYearFromFilename_ReturnsZero_WhenNoYear()
    {
        Assert.AreEqual(0, FilenameUtils.ExtractYearFromFilename("The Movie"));
    }

    [TestMethod]
    public void ExtractYearFromFilename_IgnoresOutOfRange()
    {
        Assert.AreEqual(0, FilenameUtils.ExtractYearFromFilename("Movie 1800"));
        Assert.AreEqual(0, FilenameUtils.ExtractYearFromFilename("Movie 2099"));
    }

    [TestMethod]
    public void ExtractYearFromFilename_PicksFirstYear()
    {
        Assert.AreEqual(2001, FilenameUtils.ExtractYearFromFilename("Movie 2001 2023"));
    }

    [TestMethod]
    public void ExtractYearFromFilename_HandlesEmptyString()
    {
        Assert.AreEqual(0, FilenameUtils.ExtractYearFromFilename(""));
    }
}
