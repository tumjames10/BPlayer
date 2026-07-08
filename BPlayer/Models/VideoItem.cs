using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BPlayer.Models;

public class VideoItem : INotifyPropertyChanged
{
    private string _title = "";
    private string _filePath = "";
    private string? _thumbnailUrl;
    private string? _bannerUrl;
    private string? _customPosterPath;
    private double _rating;
    private int _year;
    private bool _isLoading;
    private bool _isWatched;
    private bool _isSelected;
    private TimeSpan _playbackPosition;
    private DateTime _addedAt;
    private string? _mediaInfoCodec;
    private string _mediaInfoResolution = "";
    private int _mediaInfoBitrate;
    private long _mediaInfoFileSize;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string FilePath
    {
        get => _filePath;
        set { _filePath = value; OnPropertyChanged(); }
    }

    public string? ThumbnailUrl
    {
        get => _thumbnailUrl;
        set { _thumbnailUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasThumbnail)); }
    }

    public string? BannerUrl
    {
        get => _bannerUrl;
        set { _bannerUrl = value; OnPropertyChanged(); }
    }

    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailUrl);

    public double Rating
    {
        get => _rating;
        set { _rating = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayInfo)); }
    }

    public int Year
    {
        get => _year;
        set { _year = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayInfo)); }
    }

    public string Folder => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(FilePath)) ?? "Unknown";

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool IsWatched
    {
        get => _isWatched;
        set { _isWatched = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public TimeSpan PlaybackPosition
    {
        get => _playbackPosition;
        set { _playbackPosition = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayInfo)); }
    }

    public DateTime AddedAt
    {
        get => _addedAt;
        set { _addedAt = value; OnPropertyChanged(); }
    }

    public string? CustomPosterPath
    {
        get => _customPosterPath;
        set { _customPosterPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(EffectivePosterUrl)); OnPropertyChanged(nameof(EffectiveHasPoster)); }
    }

    public string EffectivePosterUrl => CustomPosterPath ?? ThumbnailUrl ?? "";

    public bool EffectiveHasPoster => !string.IsNullOrEmpty(CustomPosterPath) || !string.IsNullOrEmpty(ThumbnailUrl);

    public string? MediaInfoCodec
    {
        get => _mediaInfoCodec;
        set { _mediaInfoCodec = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMediaInfo)); }
    }

    public string MediaInfoResolution
    {
        get => _mediaInfoResolution;
        set { _mediaInfoResolution = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMediaInfo)); }
    }

    public int MediaInfoBitrate
    {
        get => _mediaInfoBitrate;
        set { _mediaInfoBitrate = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMediaInfo)); }
    }

    public long MediaInfoFileSize
    {
        get => _mediaInfoFileSize;
        set { _mediaInfoFileSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMediaInfo)); OnPropertyChanged(nameof(MediaInfoFileSizeFormatted)); }
    }

    public string MediaInfoFileSizeFormatted
    {
        get
        {
            if (_mediaInfoFileSize < 1024) return $"{_mediaInfoFileSize} B";
            if (_mediaInfoFileSize < 1024 * 1024) return $"{_mediaInfoFileSize / 1024.0:F1} KB";
            if (_mediaInfoFileSize < 1024 * 1024 * 1024) return $"{_mediaInfoFileSize / (1024.0 * 1024.0):F1} MB";
            return $"{_mediaInfoFileSize / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public bool HasMediaInfo => !string.IsNullOrEmpty(MediaInfoCodec) || !string.IsNullOrEmpty(MediaInfoResolution) || MediaInfoFileSize > 0;

    public string Initial => Title.Length > 0 ? Title[..1].ToUpper() : "?";

    public string DisplayInfo
    {
        get
        {
            var info = "";
            if (Year > 0) info += Year;
            if (Rating > 0) info += (info.Length > 0 ? "  •  " : "") + $"★ {Rating:F1}";
            return info;
        }
    }

    public string PlaceholderColor
    {
        get
        {
            var hash = FilePath.GetHashCode();
            var colors = new[]
            {
                "#e94560", "#a855f7", "#3b82f6", "#14b8a6",
                "#ef4444", "#06b6d4", "#8b5cf6", "#10b981",
                "#f59e0b", "#6366f1", "#ec4899", "#22c55e"
            };
            return colors[Math.Abs(hash) % colors.Length];
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
