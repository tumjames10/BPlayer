using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using BPlayer.Models;
using BPlayer.Services;

namespace BPlayer.Pages;

public partial class DashboardPage : Page
{
    private readonly ConfigService _configService = new();
    private readonly VideoScannerService _scannerService = new();
    private MetadataEnricherService _enricherService = new(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, new List<MetadataSource>());
    private readonly ObservableCollection<VideoItem> _allVideos;
    private List<VideoItem> _currentVideos = new();
    private List<Playlist> _playlists;
    private VideoItem? _selectedVideo;
    private VideoItem? _contextVideo;
    private string _currentView = "__all__";
    private string _sortField = "Name";
    private bool _sortDescending;
    private readonly PlaybackStateService _playbackState = PlaybackStateService.Instance;
    private string _searchText = "";
    private readonly List<VideoItem> _selectedVideos = new();
    private bool _isRefreshing;
    private bool _isListView;
    private bool _animateNextLoad;
    private Window? _shortcutsWindow;
    private readonly List<FileSystemWatcher> _fileWatchers = new();
    private List<Collection> _collections = new();
    private readonly MediaInfoService _mediaInfoService = new();
    private string? _dragPlaylistName;


    private Storyboard? _spinnerAnim;

    public DashboardPage(ObservableCollection<VideoItem> allVideos, List<Playlist> playlists)
    {
        _allVideos = allVideos;
        _playlists = playlists;
        InitializeComponent();
        _spinnerAnim = TryFindResource("SpinnerAnim") as Storyboard;
        PlaylistList.ItemsSource = _playlists;
        _ = RefreshFolderListAsync();
        ShowAllVideos();
        allVideos.CollectionChanged += (_, _) => UpdateContinueWatching();
        Unloaded += (_, _) => { StopFileWatchers(); _mediaInfoService.Dispose(); };
        _ = InitFromConfigAsync();
        PopulateThemeSwitcher();
    }

    private void ShowLoadingOverlay(string title, string subtitle)
    {
        LoadingTitle.Text = title;
        LoadingSubtitle.Text = subtitle;
        LoadingOverlay.Visibility = Visibility.Visible;
        _spinnerAnim?.Begin();
    }

    private void HideLoadingOverlay()
    {
        _spinnerAnim?.Stop();
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowAllVideos()
    {
        _currentView = "__all__";
        ViewTitle.Text = "All Videos";
        SidebarPlaylistActions.Visibility = Visibility.Collapsed;
        ApplySortAndRefresh(_allVideos);
        DeselectPreview();
    }

    private void ShowFolder(string folderPath)
    {
        _currentView = "__folder__" + folderPath;
        ViewTitle.Text = Path.GetFileName(folderPath);
        SidebarPlaylistActions.Visibility = Visibility.Collapsed;
        var filtered = _allVideos.Where(v => v.FilePath.StartsWith(folderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList();
        ApplySortAndRefresh(new ObservableCollection<VideoItem>(filtered));
        DeselectPreview();
        Logger.Info($"ShowFolder: {folderPath} -> {filtered.Count} videos");
    }

    private void ShowPlaylist(Playlist playlist)
    {
        _currentView = playlist.Name;
        ViewTitle.Text = playlist.Name;
        SidebarPlaylistActions.Visibility = Visibility.Visible;
        var filtered = _allVideos.Where(v => playlist.VideoPaths.Contains(v.FilePath)).ToList();
        ApplySortAndRefresh(new ObservableCollection<VideoItem>(filtered));
        DeselectPreview();
    }

    private void ApplySortAndRefresh(ObservableCollection<VideoItem> source)
    {
        IEnumerable<VideoItem> filtered = string.IsNullOrEmpty(_searchText)
            ? source
            : source.Where(v =>
                v.Title.ToLower().Contains(_searchText) ||
                v.Folder.ToLower().Contains(_searchText) ||
                (v.Year > 0 && v.Year.ToString().Contains(_searchText)));
        _currentVideos = _sortField switch
        {
            "Year" => (_sortDescending
                ? filtered.OrderBy(v => v.Year).ThenBy(v => v.Title)
                : filtered.OrderByDescending(v => v.Year).ThenBy(v => v.Title)).ToList(),
            "Folder" => (_sortDescending
                ? filtered.OrderByDescending(v => v.Folder).ThenBy(v => v.Title)
                : filtered.OrderBy(v => v.Folder).ThenBy(v => v.Title)).ToList(),
            _ => (_sortDescending
                ? filtered.OrderByDescending(v => v.Title)
                : filtered.OrderBy(v => v.Title)).ToList(),
        };

        VideosList.ItemsSource = null;
        VideosList.ItemsSource = _currentVideos;
        if (_isListView)
        {
            VideosListView.ItemsSource = null;
            VideosListView.ItemsSource = _currentVideos;
        }
        HideProgress();
        if (_currentView == "__all__")
        {
            UpdateContinueWatching();
            UpdateRecentSection();
        }
        else
        {
            ContinueWatchingSection.Visibility = Visibility.Collapsed;
            RecentSection.Visibility = Visibility.Collapsed;
        }
        var isEmpty = _currentVideos.Count == 0;
        EmptyOverlay.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (isEmpty) SidebarPlaylistActions.Visibility = Visibility.Collapsed;

        if (_animateNextLoad && _currentVideos.Count > 0)
        {
            _animateNextLoad = false;
            VideosList.Opacity = 0;
            VideosListView.Opacity = 0;
            Dispatcher.BeginInvoke(() =>
            {
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
                {
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                VideosList.BeginAnimation(OpacityProperty, fadeIn);
                VideosListView.BeginAnimation(OpacityProperty, fadeIn);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void UpdateRecentSection()
    {
        try
        {
            var recent = _allVideos
                .OrderByDescending(v => v.AddedAt)
                .Take(10)
                .ToList();

            RecentPanel.Children.Clear();

            if (recent.Count == 0)
            {
                RecentSection.Visibility = Visibility.Collapsed;
                return;
            }

            RecentSection.Visibility = Visibility.Visible;
            foreach (var video in recent)
            {
                var border = new Border
                {
                    Width = 240,
                    Height = 360,
                    Margin = new Thickness(0, 0, 12, 0),
                    CornerRadius = new CornerRadius(12),
                    ClipToBounds = true,
                    Cursor = Cursors.Hand,
                    Tag = video.FilePath,
                    Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(video.PlaceholderColor))
                };
                border.MouseLeftButtonDown += (s, _) =>
                {
                    if (s is Border b && b.Tag is string path)
                    {
                        var v = _allVideos.FirstOrDefault(x => x.FilePath == path);
                        if (v != null)
                        {
                            var pl = GetCurrentPlaylistVideos();
                            var idx = pl?.IndexOf(v) ?? -1;
                            NavigationService?.Navigate(new DetailPage(v, pl, idx >= 0 ? idx : 0));
                        }
                    }
                };

                var grid = new Grid();
                var initial = new TextBlock
                {
                    Text = video.Initial,
                    FontSize = 56,
                    FontWeight = FontWeights.Light,
                    Foreground = System.Windows.Media.Brushes.White,
                    Opacity = 0.35,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                grid.Children.Add(initial);

                var img = new System.Windows.Controls.Image
                {
                    Stretch = System.Windows.Media.Stretch.UniformToFill
                };
                img.SetBinding(System.Windows.Controls.Image.SourceProperty, new System.Windows.Data.Binding("ThumbnailUrl") { Source = video });
                var boolVis = FindResource("BoolVis") as System.Windows.Data.IValueConverter;
                if (boolVis != null)
                    img.SetBinding(System.Windows.UIElement.VisibilityProperty, new System.Windows.Data.Binding("HasThumbnail") { Source = video, Converter = boolVis });
                grid.Children.Add(img);

                var overlay = new Border
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Height = 65,
                    Background = new System.Windows.Media.LinearGradientBrush(
                        System.Windows.Media.Color.FromArgb(0, 0, 0, 0),
                        System.Windows.Media.Color.FromArgb(0xDD, 0, 0, 0),
                        0)
                };
                var titleText = new TextBlock
                {
                    Text = video.Title,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(10, 0, 10, 8),
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                overlay.Child = titleText;
                grid.Children.Add(overlay);

                border.Child = grid;
                RecentPanel.Children.Add(border);
            }

            Dispatcher.BeginInvoke(new Action(() => UpdateScrollButtons(RecentScroll, RecentScrollLeftBtn, RecentScrollRightBtn)), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex) { Logger.Warn($"UpdateRecentSection failed: {ex.Message}"); }
    }

    private void PopulateThemeSwitcher()
    {
        ThemeSwitcher.Items.Clear();
        var themes = new[] { "Dark", "Blue", "Red", "Amber", "Modern" };
        foreach (var t in themes) ThemeSwitcher.Items.Add(t);
        try
        {
            var config = _configService.GetSavedThemeName();
            ThemeSwitcher.SelectedItem = config ?? "Dark";
        }
        catch { ThemeSwitcher.SelectedIndex = 0; }
    }

    private void OnThemeSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSwitcher.SelectedItem is string theme)
        {
            ThemeService.ApplyTheme(theme);
            _ = _configService.SaveThemeNameAsync(theme);
        }
    }

    private void DeselectPreview()
    {
        _selectedVideo = null;
        PreviewCol.Width = new GridLength(0);
        MainContentCol.Width = new GridLength(1, GridUnitType.Star);
        PreviewPanel.Visibility = Visibility.Collapsed;
    }

    private void OnTileClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string path)
        {
            var video = _allVideos.FirstOrDefault(v => v.FilePath == path);
            if (video == null) return;

            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                video.IsSelected = !video.IsSelected;
                if (video.IsSelected)
                    _selectedVideos.Add(video);
                else
                    _selectedVideos.Remove(video);
            }
            else
            {
                foreach (var sv in _selectedVideos)
                    sv.IsSelected = false;
                _selectedVideos.Clear();
                SelectVideo(video);
            }
        }
    }

    private void OnTileRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string path)
        {
            _contextVideo = _allVideos.FirstOrDefault(v => v.FilePath == path);
            if (_contextVideo == null) return;

            var menu = new ContextMenu();
            menu.PlacementTarget = sender as UIElement;

            var resumePos = _playbackState.GetResumePosition(_contextVideo.FilePath);
            if (resumePos.HasValue)
            {
                var resumeItem = new MenuItem { Header = "▶ Resume" };
                resumeItem.Click += (_, _) =>
                {
                    if (_contextVideo != null)
                    {
                        var pl = GetCurrentPlaylistVideos();
                        var idx = pl?.IndexOf(_contextVideo) ?? -1;
                        NavigationService?.Navigate(new DetailPage(_contextVideo, pl, idx >= 0 ? idx : 0));
                    }
                };
                menu.Items.Add(resumeItem);

                var fromStartItem = new MenuItem { Header = "↺ Play from Beginning" };
                fromStartItem.Click += (_, _) =>
                {
                    if (_contextVideo != null)
                    {
                        _playbackState.MarkUnwatched(_contextVideo.FilePath);
                        var pl = GetCurrentPlaylistVideos();
                        var idx = pl?.IndexOf(_contextVideo) ?? -1;
                        NavigationService?.Navigate(new DetailPage(_contextVideo, pl, idx >= 0 ? idx : 0));
                    }
                };
                menu.Items.Add(fromStartItem);
            }
            else
            {
                var playItem = new MenuItem { Header = "▶ Play" };
                playItem.Click += (_, _) =>
                {
                    if (_contextVideo != null)
                    {
                        var pl = GetCurrentPlaylistVideos();
                        var idx = pl?.IndexOf(_contextVideo) ?? -1;
                        NavigationService?.Navigate(new DetailPage(_contextVideo, pl, idx >= 0 ? idx : 0));
                    }
                };
                menu.Items.Add(playItem);
            }

            menu.Items.Add(new Separator());

            var openFolderItem = new MenuItem { Header = "📂 Open folder location" };
            openFolderItem.Click += (_, _) =>
            {
                if (_contextVideo != null)
                {
                    var dir = Path.GetDirectoryName(_contextVideo.FilePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_contextVideo.FilePath}\"");
                        Logger.Info($"Open folder: {dir}");
                    }
                }
            };
            menu.Items.Add(openFolderItem);

            menu.Items.Add(new Separator());

            var addItem = new MenuItem { Header = "+ Add to Playlist" };
            addItem.Click += (_, _) => ShowPlaylistPicker(_contextVideo);
            menu.Items.Add(addItem);

            if (_currentView != "__all__")
            {
                var removeItem = new MenuItem { Header = "− Remove from Playlist" };
                removeItem.Click += (_, _) => RemoveFromCurrentPlaylist(_contextVideo);
                menu.Items.Add(removeItem);
            }

            menu.Items.Add(new Separator());

            var setPosterItem = new MenuItem { Header = "🖼 Set custom image..." };
            setPosterItem.Click += (_, _) => SetCustomPoster(_contextVideo);
            menu.Items.Add(setPosterItem);

            if (!string.IsNullOrEmpty(_contextVideo?.CustomPosterPath))
            {
                var removePosterItem = new MenuItem { Header = "🗑 Remove custom image" };
                removePosterItem.Click += (_, _) => RemoveCustomPoster(_contextVideo);
                menu.Items.Add(removePosterItem);
            }

            menu.IsOpen = true;
        }
    }

    private List<VideoItem>? GetCurrentPlaylistVideos()
    {
        if (_currentView == "__all__")
            return _allVideos.ToList();

        if (_currentView.StartsWith("__collection__"))
        {
            var name = _currentView["__collection__".Length..];
            var col = _collections.FirstOrDefault(c => c.Name == name);
            if (col != null)
                return _allVideos.Where(v => col.VideoPaths.Contains(v.FilePath)).ToList();
            return _allVideos.ToList();
        }

        var pl = _playlists.FirstOrDefault(p => p.Name == _currentView);
        if (pl != null)
            return _allVideos.Where(v => pl.VideoPaths.Contains(v.FilePath)).ToList();
        return null;
    }

    private void SelectVideo(VideoItem video)
    {
        _selectedVideo = video;

        PreviewCol.Width = new GridLength(35, GridUnitType.Star);
        MainContentCol.Width = new GridLength(65, GridUnitType.Star);

        PreviewTitle.Text = video.Title;
        PreviewInitial.Text = video.Initial;
        var fallbackColor = System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x20);
        var fallbackPalette = new Palette
        {
            Background = fallbackColor,
            Accent = System.Windows.Media.Color.FromRgb(0xe9, 0x45, 0x60),
            Dark = System.Windows.Media.Color.FromRgb(0x0a, 0x0a, 0x0f)
        };

        // Set initial gradient from placeholder color
        var pc = new System.Windows.Media.BrushConverter();
        if (pc.ConvertFrom(video.PlaceholderColor) is System.Windows.Media.Color phColor)
        {
            var initPalette = new Palette
            {
                Background = phColor,
                Accent = phColor,
                Dark = System.Windows.Media.Color.FromArgb(255, (byte)(phColor.R / 3), (byte)(phColor.G / 3), (byte)(phColor.B / 3))
            };
            PreviewGradient.Fill = ColorExtractor.CreateGradientBrush(initPalette);
        }
        else
        {
            PreviewGradient.Fill = ColorExtractor.CreateGradientBrush(fallbackPalette);
        }

        PreviewThumb.Source = !string.IsNullOrEmpty(video.EffectivePosterUrl)
            ? new BitmapImage(new System.Uri(video.EffectivePosterUrl))
            : null;

        var info = "";
        if (video.Year > 0) info += video.Year;
        if (video.Rating > 0)
            info += (info.Length > 0 ? "  •  " : "") + $"★ {video.Rating:F1}";
        PreviewMeta.Text = info;

        PreviewRating.Text = video.Rating > 0 ? $"★ {video.Rating:F1}/10" : "";
        PreviewRatingBadge.Visibility = video.Rating > 0
            ? Visibility.Visible : Visibility.Collapsed;

        // Resume info
        var resumePos = _playbackState.GetResumePosition(video.FilePath);
        if (resumePos.HasValue)
        {
            var ts = TimeSpan.FromSeconds(resumePos.Value);
            var resumeText = ts.Hours > 0
                ? $"Resume at {ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"Resume at {ts.Minutes}:{ts.Seconds:D2}";
            PreviewMeta.Text = info + (info.Length > 0 ? "  •  " : "") + resumeText;
            PreviewResumeBorder.Visibility = Visibility.Visible;
            PreviewPlayFromStartBorder.Visibility = Visibility.Visible;
            PreviewPlayBorder.Visibility = Visibility.Collapsed;
        }
        else
        {
            PreviewResumeBorder.Visibility = Visibility.Collapsed;
            PreviewPlayFromStartBorder.Visibility = Visibility.Collapsed;
            PreviewPlayBorder.Visibility = Visibility.Visible;
        }

        // Animate preview panel entrance: opacity fade-in + slide from right
        PreviewPanel.Opacity = 0;
        var translate = new System.Windows.Media.TranslateTransform(30, 0);
        PreviewPanel.RenderTransform = translate;
        PreviewPanel.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        PreviewPanel.Visibility = Visibility.Visible;

        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = 1, Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        var slideIn = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = 0, Duration = TimeSpan.FromSeconds(0.35),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        PreviewPanel.BeginAnimation(OpacityProperty, fadeIn);
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slideIn);

        // Extract dominant colors from thumbnail (async)
        if (!string.IsNullOrEmpty(video.EffectivePosterUrl))
        {
            var url = video.EffectivePosterUrl;
            _ = Task.Run(() =>
            {
                var palette = ColorExtractor.ExtractFromImage(url, fallbackPalette);
                var brush = ColorExtractor.CreateGradientBrush(palette);
                Dispatcher.Invoke(() => PreviewGradient.Fill = brush);
            });
        }

        // Show cached media info or load it
        if (video.HasMediaInfo)
        {
            ShowMediaInfo(video);
        }
        else
        {
            VideoInfoSection.Visibility = Visibility.Collapsed;
            _ = LoadMediaInfoAsync(video);
        }
    }

    private void ShowMediaInfo(VideoItem video)
    {
        VideoInfoSection.Visibility = Visibility.Visible;
        InfoFileSize.Text = "Size: " + video.MediaInfoFileSizeFormatted;
        InfoResolution.Text = !string.IsNullOrEmpty(video.MediaInfoResolution) ? "Resolution: " + video.MediaInfoResolution : "";
        InfoCodec.Text = !string.IsNullOrEmpty(video.MediaInfoCodec) ? "Codec: " + video.MediaInfoCodec.ToUpper() : "";
        InfoBitrate.Text = video.MediaInfoBitrate > 0 ? $"Bitrate: {video.MediaInfoBitrate / 1000} kb/s" : "";

        InfoResolution.Visibility = !string.IsNullOrEmpty(video.MediaInfoResolution) ? Visibility.Visible : Visibility.Collapsed;
        InfoCodec.Visibility = !string.IsNullOrEmpty(video.MediaInfoCodec) ? Visibility.Visible : Visibility.Collapsed;
        InfoBitrate.Visibility = video.MediaInfoBitrate > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async System.Threading.Tasks.Task LoadMediaInfoAsync(VideoItem video)
    {
        try
        {
            var info = await Task.Run(() => _mediaInfoService.GetMediaInfoAsync(video.FilePath));
            if (info == null) return;

            await Dispatcher.InvokeAsync(() =>
            {
                video.MediaInfoFileSize = info.FileSize;
                video.MediaInfoCodec = info.VideoCodec;
                video.MediaInfoResolution = info.Resolution;
                video.MediaInfoBitrate = info.Bitrate;

                if (_selectedVideo == video)
                    ShowMediaInfo(video);
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"LoadMediaInfoAsync: {ex.Message}");
        }
    }

    private void OnPreviewPlay(object sender, RoutedEventArgs e)
    {
        if (_selectedVideo == null) return;
        var playlistVideos = GetCurrentPlaylistVideos();
        var idx = playlistVideos?.IndexOf(_selectedVideo) ?? -1;
        NavigationService?.Navigate(new DetailPage(_selectedVideo, playlistVideos, idx >= 0 ? idx : 0));
    }

    private void OnPreviewResume(object sender, RoutedEventArgs e)
    {
        if (_selectedVideo == null) return;
        var playlistVideos = GetCurrentPlaylistVideos();
        var idx = playlistVideos?.IndexOf(_selectedVideo) ?? -1;
        NavigationService?.Navigate(new DetailPage(_selectedVideo, playlistVideos, idx >= 0 ? idx : 0));
    }

    private void OnPreviewPlayFromStart(object sender, RoutedEventArgs e)
    {
        if (_selectedVideo == null) return;
        _playbackState.MarkUnwatched(_selectedVideo.FilePath);
        var playlistVideos = GetCurrentPlaylistVideos();
        var idx = playlistVideos?.IndexOf(_selectedVideo) ?? -1;
        NavigationService?.Navigate(new DetailPage(_selectedVideo, playlistVideos, idx >= 0 ? idx : 0));
    }

    private void OnAddToPlaylist(object sender, RoutedEventArgs e)
    {
        if (_selectedVideo == null) return;

        var dialog = new Windows.PlaylistPicker(_selectedVideo.FilePath, _playlists)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            if (dialog.IsNewPlaylist)
            {
                var pl = new Playlist { Name = dialog.SelectedPlaylist! };
                pl.VideoPaths.Add(_selectedVideo.FilePath);
                _playlists.Add(pl);
                RefreshPlaylistList();
                _ = SavePlaylistsAsync();
            }
            else if (dialog.SelectedPlaylist != null)
            {
                AddToPlaylist(dialog.SelectedPlaylist);
            }
        }
    }

    private void AddToPlaylist(string playlistName)
    {
        if (_selectedVideo == null) return;
        var pl = _playlists.FirstOrDefault(p => p.Name == playlistName);
        if (pl != null && !pl.VideoPaths.Contains(_selectedVideo.FilePath))
        {
            pl.VideoPaths.Add(_selectedVideo.FilePath);
            _ = SavePlaylistsAsync();
        }
    }

    private void ShowPlaylistPicker(VideoItem video)
    {
        var dialog = new Windows.PlaylistPicker(video.FilePath, _playlists)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            if (dialog.IsNewPlaylist)
            {
                var name = dialog.SelectedPlaylist!;
                var pl = new Playlist { Name = name };
                pl.VideoPaths.Add(video.FilePath);
                _playlists.Add(pl);
                RefreshPlaylistList();
                _ = SavePlaylistsAsync();
            }
            else if (dialog.SelectedPlaylist != null)
            {
                var p = _playlists.FirstOrDefault(x => x.Name == dialog.SelectedPlaylist);
                if (p != null && !p.VideoPaths.Contains(video.FilePath))
                {
                    p.VideoPaths.Add(video.FilePath);
                    _ = SavePlaylistsAsync();
                }
            }
        }
    }

    private void RemoveFromCurrentPlaylist(VideoItem video)
    {
        var pl = _playlists.FirstOrDefault(p => p.Name == _currentView);
        if (pl != null)
        {
            pl.VideoPaths.Remove(video.FilePath);
            _ = SavePlaylistsAsync();

            // Refresh the current playlist view
            var filtered = _allVideos.Where(v => pl.VideoPaths.Contains(v.FilePath)).ToList();
            ApplySortAndRefresh(new ObservableCollection<VideoItem>(filtered));
            DeselectPreview();
        }
    }

    private void SetCustomPoster(VideoItem? video)
    {
        if (video == null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp",
            Title = "Select custom poster image"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var posterDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BPlayer", "posters");
                Directory.CreateDirectory(posterDir);
                var ext = Path.GetExtension(dialog.FileName);
                var cacheName = Path.GetFileNameWithoutExtension(video.FilePath) + ext;
                var dest = Path.Combine(posterDir, cacheName);
                File.Copy(dialog.FileName, dest, true);
                video.CustomPosterPath = dest;

                // Also update the preview if this video is selected
                if (_selectedVideo == video)
                    SelectVideo(video);
            }
            catch (Exception ex)
            {
                Logger.Error($"SetCustomPoster: {ex.Message}");
            }
        }
    }

    private void RemoveCustomPoster(VideoItem? video)
    {
        if (video == null || string.IsNullOrEmpty(video.CustomPosterPath)) return;
        try
        {
            if (File.Exists(video.CustomPosterPath))
                File.Delete(video.CustomPosterPath);
        }
        catch { }
        video.CustomPosterPath = null;

        if (_selectedVideo == video)
            SelectVideo(video);
    }

    private void OnNewPlaylistClick(object sender, MouseButtonEventArgs e)
    {
        var name = $"Playlist {_playlists.Count + 1}";
        _playlists.Add(new Playlist { Name = name });
        RefreshPlaylistList();
        _ = SavePlaylistsAsync();
    }

    private void OnPlaylistPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ItemsControl ic)
        {
            var pos = e.GetPosition(ic);
            var result = VisualTreeHelper.HitTest(ic, pos);
            if (result?.VisualHit is DependencyObject dp)
            {
                var border = FindVisualParent<Border>(dp);
                if (border?.Tag is string name)
                    _dragPlaylistName = name;
            }
        }
    }

    private static T? FindVisualParent<T>(DependencyObject element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T t) return t;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private void OnPlaylistPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragPlaylistName != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(sender as IInputElement);
            if (pos.X > 2 || pos.Y > 2)
            {
                DragDrop.DoDragDrop(PlaylistList, _dragPlaylistName, DragDropEffects.Move);
                _dragPlaylistName = null;
            }
        }
    }

    private void OnPlaylistDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.StringFormat)) return;
        var srcName = e.Data.GetData(DataFormats.StringFormat) as string;
        if (string.IsNullOrEmpty(srcName)) return;

        var src = _playlists.FirstOrDefault(p => p.Name == srcName);
        if (src == null) return;

        var pos = e.GetPosition(PlaylistList);
        var result = VisualTreeHelper.HitTest(PlaylistList, pos);
        var targetBorder = result?.VisualHit != null ? FindVisualParent<Border>(result.VisualHit) : null;
        var targetName = targetBorder?.Tag as string;

        if (string.IsNullOrEmpty(targetName) || targetName == srcName) return;

        var target = _playlists.FirstOrDefault(p => p.Name == targetName);
        if (target == null) return;

        var srcIdx = _playlists.IndexOf(src);
        var tgtIdx = _playlists.IndexOf(target);

        _playlists.RemoveAt(srcIdx);
        var insertIdx = _playlists.IndexOf(target);
        _playlists.Insert(insertIdx, src);

        RefreshPlaylistList();
        _ = SavePlaylistsAsync();
        e.Handled = true;
    }

    private void OnExportPlaylistsClick(object sender, MouseButtonEventArgs e)
    {
        if (_playlists.Count == 0)
        {
            MessageBox.Show("No playlists to export.", "Export Playlists", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "M3U playlist|*.m3u",
            FileName = "BPlayer_Playlists.m3u",
            Title = "Export Playlists"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                using var writer = new StreamWriter(dialog.FileName);
                writer.WriteLine("#EXTM3U");
                foreach (var pl in _playlists)
                {
                    writer.WriteLine($"#PLAYLIST:{pl.Name}");
                    foreach (var path in pl.VideoPaths)
                    {
                        var video = _allVideos.FirstOrDefault(v => v.FilePath == path);
                        var title = video?.Title ?? Path.GetFileNameWithoutExtension(path);
                        writer.WriteLine($"#EXTINF:-1,{title}");
                        writer.WriteLine(path);
                    }
                }
                Logger.Info($"Exported {_playlists.Count} playlists to {dialog.FileName}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Export playlists: {ex.Message}");
                MessageBox.Show($"Failed to export: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void OnImportPlaylistsClick(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "M3U playlist|*.m3u",
            Title = "Import Playlists",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(dialog.FileName);
                Playlist? currentPlaylist = null;
                int imported = 0;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("#PLAYLIST:", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = trimmed["#PLAYLIST:".Length..].Trim();
                        if (!string.IsNullOrEmpty(name))
                        {
                            currentPlaylist = _playlists.FirstOrDefault(p => p.Name == name);
                            if (currentPlaylist == null)
                            {
                                currentPlaylist = new Playlist { Name = name };
                                _playlists.Add(currentPlaylist);
                            }
                        }
                    }
                    else if (!trimmed.StartsWith("#") && !string.IsNullOrEmpty(trimmed) && File.Exists(trimmed))
                    {
                        var ext = Path.GetExtension(trimmed).ToLowerInvariant();
                        if (!VideoExtensions.Contains(ext)) continue;
                        if (currentPlaylist != null && !currentPlaylist.VideoPaths.Contains(trimmed))
                        {
                            currentPlaylist.VideoPaths.Add(trimmed);
                            imported++;
                        }
                    }
                }

                RefreshPlaylistList();
                await SavePlaylistsAsync();
                Logger.Info($"Imported {imported} videos into {_playlists.Count} playlists");
                MessageBox.Show($"Imported {imported} videos.", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Logger.Error($"Import playlists: {ex.Message}");
                MessageBox.Show($"Failed to import: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnPlaylistClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string name)
        {
            var pl = _playlists.FirstOrDefault(p => p.Name == name);
            if (pl != null) ShowPlaylist(pl);
        }
    }

    private void OnDeletePlaylistClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock tb && tb.Tag is string name)
        {
            var pl = _playlists.FirstOrDefault(p => p.Name == name);
            if (pl != null)
            {
                _playlists.Remove(pl);
                RefreshPlaylistList();
                if (_currentView == name) ShowAllVideos();
                _ = SavePlaylistsAsync();
            }
            e.Handled = true;
        }
    }

    private void OnAllVideosClick(object sender, MouseButtonEventArgs e)
    {
        foreach (var sv in _selectedVideos)
            sv.IsSelected = false;
        _selectedVideos.Clear();
        ShowAllVideos();
    }

    private void OnSortClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        string[] options = { "Name", "Year", "Folder" };

        foreach (var opt in options)
        {
            var marked = opt == _sortField ? "✓ " : "  ";
            var item = new MenuItem { Header = marked + opt };
            var captured = opt;
            item.Click += (_, _) =>
            {
                _sortField = captured;
                RefreshCurrentView();
            };
            menu.Items.Add(item);
        }

        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
    }

    private void OnSortDirClick(object sender, MouseButtonEventArgs e)
    {
        _sortDescending = !_sortDescending;
        SortDirLabel.Text = _sortDescending ? "↓" : "↑";
        RefreshCurrentView();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text?.Trim().ToLower() ?? "";
        RefreshCurrentView();
    }

    private void OnPlaylistRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string name)
        {
            var menu = new ContextMenu();
            menu.PlacementTarget = sender as UIElement;

            var renameItem = new MenuItem { Header = "✏ Rename" };
            renameItem.Click += (_, _) => ShowRenameDialog(name);
            menu.Items.Add(renameItem);

            var dupItem = new MenuItem { Header = "⧉ Duplicate" };
            dupItem.Click += (_, _) => DuplicatePlaylist(name);
            menu.Items.Add(dupItem);

            var deleteItem = new MenuItem { Header = "× Delete", Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xef, 0x44, 0x44)) };
            deleteItem.Click += (_, _) => DeletePlaylist(name);
            menu.Items.Add(deleteItem);

            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void ShowRenameDialog(string currentName)
    {
        var dialog = new Windows.InputDialog("Rename Playlist", currentName)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            var newName = dialog.InputText.Trim();
            var pl = _playlists.FirstOrDefault(p => p.Name == currentName);
            if (pl != null && !string.IsNullOrWhiteSpace(newName) && newName != currentName)
            {
                pl.Name = newName;
                if (_currentView == currentName)
                {
                    _currentView = newName;
                    ViewTitle.Text = newName;
                }
                RefreshPlaylistList();
                _ = SavePlaylistsAsync();
            }
        }
    }

    private void DeletePlaylist(string name)
    {
        var pl = _playlists.FirstOrDefault(p => p.Name == name);
        if (pl != null)
        {
            _playlists.Remove(pl);
            RefreshPlaylistList();
            if (_currentView == name) ShowAllVideos();
            _ = SavePlaylistsAsync();
        }
    }

    private void OnRenamePlaylistClick(object sender, RoutedEventArgs e)
    {
        if (_currentView != "__all__")
            ShowRenameDialog(_currentView);
    }

    private void OnDeletePlaylistFromBarClick(object sender, RoutedEventArgs e)
    {
        if (_currentView != "__all__")
            DeletePlaylist(_currentView);
    }

    private void OnDuplicatePlaylistClick(object sender, RoutedEventArgs e)
    {
        if (_currentView != "__all__")
            DuplicatePlaylist(_currentView);
    }

    private void DuplicatePlaylist(string name)
    {
        var src = _playlists.FirstOrDefault(p => p.Name == name);
        if (src == null) return;

        var newName = name + " (Copy)";
        var copy = new Playlist
        {
            Name = newName,
            VideoPaths = new List<string>(src.VideoPaths)
        };
        _playlists.Add(copy);
        RefreshPlaylistList();
        ShowPlaylist(copy);
        _ = SavePlaylistsAsync();
    }

    private void OnAddPlaylistFromBarClick(object sender, RoutedEventArgs e)
    {
        var name = $"Playlist {_playlists.Count + 1}";
        _playlists.Add(new Playlist { Name = name });
        RefreshPlaylistList();
        _ = SavePlaylistsAsync();
    }

    private void RefreshCurrentView()
    {
        SortLabel.Text = _sortField;
        if (_currentView == "__all__")
        {
            ApplySortAndRefresh(_allVideos);
        }
        else if (_currentView.StartsWith("__folder__"))
        {
            var folder = _currentView["__folder__".Length..];
            var filtered = _allVideos.Where(v => v.FilePath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList();
            ApplySortAndRefresh(new ObservableCollection<VideoItem>(filtered));
        }
        else if (_currentView.StartsWith("__collection__"))
        {
            var name = _currentView["__collection__".Length..];
            var col = _collections.FirstOrDefault(c => c.Name == name);
            if (col != null)
            {
                var filtered = _allVideos.Where(v => col.VideoPaths.Contains(v.FilePath)).ToList();
                ApplySortAndRefresh(new ObservableCollection<VideoItem>(filtered));
            }
            else
            {
                ShowAllVideos();
            }
        }
        else
        {
            var source = new ObservableCollection<VideoItem>(
                _allVideos.Where(v => _playlists.FirstOrDefault(p => p.Name == _currentView)
                    ?.VideoPaths.Contains(v.FilePath) == true).ToList());
            ApplySortAndRefresh(source);
        }
    }

    private void OnEmptyAddFoldersClick(object sender, MouseButtonEventArgs e)
    {
        OnSettingsClick(sender, new RoutedEventArgs());
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            await SavePlaylistsAsync();

            var settings = new SettingsWindow { Owner = Window.GetWindow(this) };

            if (settings.ShowDialog() == true)
            {
                Logger.Info("Settings saved — reloading dashboard");
                _animateNextLoad = _allVideos.Count == 0;
                ShowLoadingOverlay("Scanning folders", "Looking for video files...");
                ShowProgress();
                try
                {
                    var config = await _configService.LoadAsync();
                    Logger.Info($"Folders in config: {string.Join("; ", config.VideoSourcePaths)}");
                    var videos = await _scannerService.ScanDirectoriesAsync(config.VideoSourcePaths);
                    Logger.Info($"Scanned {videos.Count} videos");

                    _allVideos.Clear();
                    foreach (var v in videos) _allVideos.Add(v);

                    if (config.EnableOnlineMetadata || config.EnableVideoThumbnails)
                    {
                        _enricherService = new MetadataEnricherService(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, config.MetadataSources);
                        await _enricherService.EnrichAsync(_allVideos, config.EnableOnlineMetadata, config.EnableVideoThumbnails);
                    }

                    _playlists = config.Playlists ?? new();
                    _collections = config.Collections ?? new();
                    RefreshPlaylistList();
                    RefreshCollectionsSidebar();
                    await RefreshFolderListAsync();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Settings reload error: {ex.Message}");
                }
                HideLoadingOverlay();
                ShowAllVideos();
                HideProgress();
                Logger.Info("Dashboard reload complete");
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void OnAboutClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var about = new Windows.AboutWindow { Owner = Window.GetWindow(this) };
        about.ShowDialog();
    }

    private void OnSendFeedbackClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "mailto:bbtumulak@gmail.com?subject=BPlayer%20Feedback",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        ShowLoadingOverlay("Refreshing", "Scanning for new videos...");
        ShowProgress();
        try
        {
            var config = await _configService.LoadAsync();
            Logger.Info("Refresh: scanning directories");
            var videos = await _scannerService.ScanDirectoriesAsync(config.VideoSourcePaths);
            Logger.Info($"Refresh: scanned {videos.Count} videos");

            // Find new videos not already in the collection
            var existingPaths = new HashSet<string>(_allVideos.Select(v => v.FilePath), StringComparer.OrdinalIgnoreCase);
            var newVideos = videos.Where(v => !existingPaths.Contains(v.FilePath)).ToList();
            var removedPaths = existingPaths.Where(p => !videos.Any(v => v.FilePath.Equals(p, StringComparison.OrdinalIgnoreCase))).ToList();

            Logger.Info($"Refresh: {newVideos.Count} new, {removedPaths.Count} removed");

            // Remove stale entries
            foreach (var path in removedPaths)
            {
                var stale = _allVideos.FirstOrDefault(v => v.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (stale != null) _allVideos.Remove(stale);
            }

            // Add new videos only — existing ones keep their metadata/thumbnails
            foreach (var v in newVideos)
                _allVideos.Add(v);

            // Only enrich new videos (thumbnails + metadata)
            if (newVideos.Count > 0 && (config.EnableOnlineMetadata || config.EnableVideoThumbnails))
            {
                var newCollection = new ObservableCollection<VideoItem>(newVideos);
                _enricherService = new MetadataEnricherService(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, config.MetadataSources);
                await _enricherService.EnrichAsync(newCollection, config.EnableOnlineMetadata, config.EnableVideoThumbnails);
            }

            // Remove stale playlist entries
            foreach (var pl in _playlists)
                pl.VideoPaths.RemoveAll(p => !_allVideos.Any(v => v.FilePath == p));

            // Reload collections
            _collections = config.Collections ?? new();
            RefreshCollectionsSidebar();

            RefreshPlaylistList();
            await RefreshFolderListAsync();
            RefreshCurrentView();
            SetupFileWatchers(config.VideoSourcePaths);
            Logger.Info("Refresh complete");
        }
        finally
        {
            HideLoadingOverlay();
            HideProgress();
            _isRefreshing = false;
        }
    }

    private void RefreshPlaylistList()
    {
        PlaylistList.ItemsSource = null;
        PlaylistList.ItemsSource = _playlists;
    }

    private void RefreshCollectionsSidebar()
    {
        CollectionsSidebar.ItemsSource = null;
        CollectionsSidebar.ItemsSource = _collections;
    }

    private void ShowCollection(Collection collection)
    {
        _currentView = "__collection__" + collection.Name;
        ViewTitle.Text = collection.Name;
        SidebarPlaylistActions.Visibility = Visibility.Collapsed;
        var filtered = _allVideos.Where(v => collection.VideoPaths.Contains(v.FilePath)).ToList();
        ApplySortAndRefresh(new ObservableCollection<VideoItem>(filtered));
        DeselectPreview();
    }

    private void OnCollectionClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string name)
        {
            var col = _collections.FirstOrDefault(c => c.Name == name);
            if (col != null) ShowCollection(col);
        }
    }

    private void OnCollectionsInfoClick(object sender, MouseButtonEventArgs e)
    {
        CollectionsInfoPopup.Visibility = CollectionsInfoPopup.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnDetectCollectionsClick(object sender, MouseButtonEventArgs e)
    {
        if (_allVideos.Count == 0)
        {
            MessageBox.Show("No videos loaded. Add folders in Settings first.", "No collections found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var detected = AutoDetectCollections();
        if (detected.Count == 0)
        {
            MessageBox.Show("No common patterns detected among your video files.\nTry different file naming or add videos grouped in folders.", "No collections found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Add detected collections that don't already exist
        foreach (var col in detected)
        {
            if (!_collections.Any(c => c.Name.Equals(col.Name, StringComparison.OrdinalIgnoreCase)))
                _collections.Add(col);
        }
        RefreshCollectionsSidebar();
        await SaveCollectionsAsync();
    }

    private List<Collection> AutoDetectCollections()
    {
        var collections = new List<Collection>();
        var titles = _allVideos.Select(v => (v.Title, v.FilePath)).ToList();

        // 1. Detect episode patterns: "Show Name S01E01" -> extract show name
        var episodeRegex = new Regex(@"^(.+?)[.\s_-]*[Ss]\d+[Ee]\d+", RegexOptions.Compiled);
        var episodeGroups = titles
            .Select(t => new { Match = episodeRegex.Match(t.Title), t.FilePath })
            .Where(x => x.Match.Success)
            .GroupBy(x => x.Match.Groups[1].Value.Trim().Replace(".", " ").Replace("_", " "))
            .Where(g => g.Count() >= 2);

        foreach (var group in episodeGroups)
            collections.Add(new Collection { Name = group.Key, VideoPaths = group.Select(x => x.FilePath).ToList() });

        var groupedPaths = new HashSet<string>(collections.SelectMany(c => c.VideoPaths));

        // 2. Group by common prefix (files sharing same base name differentiated by "Part X", number suffix, etc.)
        var partRegex = new Regex(@"^(.+?)[.\s_-]*(?:Part|Pt|Chapter|Ch|Vol|Volume)?[.\s_-]*\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        var remaining = titles.Where(t => !groupedPaths.Contains(t.FilePath)).ToList();

        var prefixGroups = remaining
            .Select(t => new
            {
                Base = partRegex.Match(t.Title).Success
                    ? partRegex.Match(t.Title).Groups[1].Value.Trim().Replace(".", " ").Replace("_", " ")
                    : t.Title,
                t.FilePath
            })
            .GroupBy(x => x.Base)
            .Where(g => g.Count() >= 2 && g.Key.Length > 2);

        foreach (var group in prefixGroups)
        {
            if (!collections.Any(c => c.Name.Equals(group.Key, StringComparison.OrdinalIgnoreCase)))
                collections.Add(new Collection { Name = group.Key, VideoPaths = group.Select(x => x.FilePath).ToList() });
        }

        var folderGrouped = new HashSet<string>(collections.SelectMany(c => c.VideoPaths));

        // 3. Group by parent folder (handles numbered files grouped in season-like folders)
        var folderGroups = titles
            .Where(t => !folderGrouped.Contains(t.FilePath))
            .Select(t => new
            {
                Folder = Path.GetFileName(Path.GetDirectoryName(t.FilePath)) ?? "Unknown",
                t.FilePath
            })
            .GroupBy(x => x.Folder)
            .Where(g => g.Count() >= 3);

        foreach (var group in folderGroups)
        {
            if (!collections.Any(c => c.Name.Equals(group.Key, StringComparison.OrdinalIgnoreCase)))
                collections.Add(new Collection { Name = group.Key, VideoPaths = group.Select(x => x.FilePath).ToList() });
        }

        return collections;
    }

    private async Task RefreshFolderListAsync()
    {
        var config = await _configService.LoadAsync();
        var folders = config.VideoSourcePaths
            .Where(Directory.Exists)
            .OrderBy(f => f)
            .ToList();
        FolderListSidebar.ItemsSource = folders;
        Logger.Info($"Folders sidebar: {folders.Count} folders");
    }

    private void OnFolderClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string folder)
        {
            ShowFolder(folder);
        }
    }

    private void ShowProgress()
    {
        ProgressBar.Visibility = Visibility.Visible;
        ProgressFill.Width = 0;
    }

    private void HideProgress()
    {
        ProgressBar.Visibility = Visibility.Collapsed;
        ProgressFill.Width = 0;
    }

    private void SetupFileWatchers(List<string> paths)
    {
        StopFileWatchers();
        foreach (var path in paths)
        {
            if (!Directory.Exists(path)) continue;
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                    Filter = "*.*"
                };
                watcher.Created += OnFileCreated;
                watcher.Deleted += OnFileDeleted;
                watcher.Renamed += OnFileRenamed;
                watcher.EnableRaisingEvents = true;
                _fileWatchers.Add(watcher);
                Logger.Info($"FileWatcher started: {path}");
            }
            catch (Exception ex)
            {
                Logger.Error($"FileWatcher failed for {path}: {ex.Message}");
            }
        }
    }

    private void StopFileWatchers()
    {
        foreach (var w in _fileWatchers)
        {
            try { w.EnableRaisingEvents = false; w.Dispose(); }
            catch { }
        }
        _fileWatchers.Clear();
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        var ext = Path.GetExtension(e.Name ?? "").ToLower();
        if (!IsVideoExtension(ext)) return;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_isRefreshing) return;
            try
            {
                // Check if file is ready (fully written)
                if (!File.Exists(e.FullPath)) return;
                var fi = new FileInfo(e.FullPath);
                if (fi.Length == 0) return;

                // Check if already tracked
                if (_allVideos.Any(v => v.FilePath.Equals(e.FullPath, StringComparison.OrdinalIgnoreCase))) return;

                var video = new VideoItem
                {
                    FilePath = e.FullPath,
                    Title = Path.GetFileNameWithoutExtension(e.FullPath),
                    IsLoading = false,
                    AddedAt = File.GetCreationTime(e.FullPath)
                };
                _allVideos.Add(video);

                // Enrich new video in background
                try
                {
                    var config = await _configService.LoadAsync();
                    if (config.EnableOnlineMetadata || config.EnableVideoThumbnails)
                    {
                        var enricher = new MetadataEnricherService(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, config.MetadataSources);
                        var single = new ObservableCollection<VideoItem> { video };
                        await enricher.EnrichAsync(single, config.EnableOnlineMetadata, config.EnableVideoThumbnails);
                    }
                }
                catch (Exception enrichEx)
                {
                    Logger.Error($"Enrich new file: {enrichEx.Message}");
                }

                RefreshCurrentView();
            }
            catch (Exception ex)
            {
                Logger.Error($"OnFileCreated: {ex.Message}");
            }
        }));
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        var ext = Path.GetExtension(e.Name ?? "").ToLower();
        if (!IsVideoExtension(ext)) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var stale = _allVideos.FirstOrDefault(v => v.FilePath.Equals(e.FullPath, StringComparison.OrdinalIgnoreCase));
            if (stale != null)
            {
                _allVideos.Remove(stale);
                RefreshCurrentView();
            }
        }));
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        var oldExt = Path.GetExtension(e.OldName ?? "").ToLower();
        var newExt = Path.GetExtension(e.Name ?? "").ToLower();
        var wasVideo = IsVideoExtension(oldExt);
        var isVideo = IsVideoExtension(newExt);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (wasVideo && isVideo)
                {
                    // Video renamed to video: update path
                    var stale = _allVideos.FirstOrDefault(v => v.FilePath.Equals(e.OldFullPath, StringComparison.OrdinalIgnoreCase));
                    if (stale != null)
                    {
                        stale.FilePath = e.FullPath;
                        stale.Title = Path.GetFileNameWithoutExtension(e.FullPath);
                        RefreshCurrentView();
                    }
                }
                else if (wasVideo && !isVideo)
                {
                    // Video renamed to non-video: remove
                    var stale = _allVideos.FirstOrDefault(v => v.FilePath.Equals(e.OldFullPath, StringComparison.OrdinalIgnoreCase));
                    if (stale != null)
                    {
                        _allVideos.Remove(stale);
                        RefreshCurrentView();
                    }
                }
                else if (!wasVideo && isVideo)
                {
                    // Non-video renamed to video: add
                    if (_allVideos.Any(v => v.FilePath.Equals(e.FullPath, StringComparison.OrdinalIgnoreCase))) return;
                    var video = new VideoItem
                    {
                        FilePath = e.FullPath,
                        Title = Path.GetFileNameWithoutExtension(e.FullPath),
                        IsLoading = false,
                        AddedAt = File.GetCreationTime(e.FullPath)
                    };
                    _allVideos.Add(video);
                    RefreshCurrentView();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"OnFileRenamed: {ex.Message}");
            }
        }));
    }

    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };

    private static bool IsVideoExtension(string ext) => VideoExtensions.Contains(ext);

    private async Task InitFromConfigAsync()
    {
        try
        {
            var config = await _configService.LoadAsync();
            if (config.VideoSourcePaths.Count > 0)
                SetupFileWatchers(config.VideoSourcePaths);
            _collections = config.Collections ?? new();
            RefreshCollectionsSidebar();
        }
        catch { }
    }

    private void UpdateContinueWatching()
    {
        var resumeVideos = _allVideos
            .Where(v => _playbackState.GetResumePosition(v.FilePath).HasValue)
            .OrderByDescending(v => v.Title)
            .Take(10)
            .ToList();

        ContinueWatchingPanel.Children.Clear();

        if (resumeVideos.Count == 0)
        {
            ContinueWatchingSection.Visibility = Visibility.Collapsed;
            return;
        }

        var boolVis = FindResource("BoolVis") as System.Windows.Data.IValueConverter;

        ContinueWatchingSection.Visibility = Visibility.Visible;
        foreach (var video in resumeVideos)
        {
            var border = new Border
            {
                Width = 240,
                Height = 360,
                Margin = new Thickness(0, 0, 12, 0),
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                Tag = video.FilePath,
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(video.PlaceholderColor))
            };
            border.MouseLeftButtonDown += (s, _) =>
            {
                if (s is Border b && b.Tag is string path)
                {
                    var v = _allVideos.FirstOrDefault(x => x.FilePath == path);
                    if (v != null)
                    {
                        var pl = GetCurrentPlaylistVideos();
                        var idx = pl?.IndexOf(v) ?? -1;
                        NavigationService?.Navigate(new DetailPage(v, pl, idx >= 0 ? idx : 0));
                    }
                }
            };

            var grid = new Grid();
            var initial = new TextBlock
            {
                Text = video.Initial,
                FontSize = 56,
                FontWeight = FontWeights.Light,
                Foreground = System.Windows.Media.Brushes.White,
                Opacity = 0.35,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(initial);

            var img = new System.Windows.Controls.Image
            {
                Stretch = System.Windows.Media.Stretch.UniformToFill
            };
            img.SetBinding(System.Windows.Controls.Image.SourceProperty, new System.Windows.Data.Binding("ThumbnailUrl") { Source = video });
            if (boolVis != null)
                img.SetBinding(System.Windows.UIElement.VisibilityProperty, new System.Windows.Data.Binding("HasThumbnail") { Source = video, Converter = boolVis });
            grid.Children.Add(img);

            var overlay = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 65,
                Background = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Color.FromArgb(0, 0, 0, 0),
                    System.Windows.Media.Color.FromArgb(0xDD, 0, 0, 0),
                    0)
            };
            var titleText = new TextBlock
            {
                Text = video.Title,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
                Margin = new Thickness(10, 0, 10, 8),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            overlay.Child = titleText;
            grid.Children.Add(overlay);

            border.Child = grid;
            ContinueWatchingPanel.Children.Add(border);
        }

        Dispatcher.BeginInvoke(new Action(() => UpdateScrollButtons(ContinueWatchingScroll, CWScrollLeftBtn, CWScrollRightBtn)), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnViewToggleClick(object sender, RoutedEventArgs e)
    {
        _isListView = !_isListView;
        ViewToggleBtn.Content = _isListView ? "▦ Grid" : "☰ List";

        if (_isListView)
        {
            VideosList.Visibility = Visibility.Collapsed;
            VideosListView.Visibility = Visibility.Visible;
            VideosListView.ItemsSource = null;
            VideosListView.ItemsSource = _currentVideos;
        }
        else
        {
            VideosListView.Visibility = Visibility.Collapsed;
            VideosList.Visibility = Visibility.Visible;
            VideosList.ItemsSource = null;
            VideosList.ItemsSource = _currentVideos;
        }
    }

    private void OnOuterPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Delta == 0) return;

        var target = e.OriginalSource as System.Windows.DependencyObject;
        if (target != null && (IsChildOfScrollViewer(target, ContinueWatchingScroll) || IsChildOfScrollViewer(target, RecentScroll)))
        {
            OuterScrollViewer.ScrollToVerticalOffset(OuterScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private static bool IsChildOfScrollViewer(System.Windows.DependencyObject element, System.Windows.Controls.ScrollViewer sv)
    {
        while (element != null)
        {
            if (element == sv) return true;
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private void UpdateScrollButtons(System.Windows.Controls.ScrollViewer sv, System.Windows.UIElement leftBtn, System.Windows.UIElement rightBtn)
    {
        var canScroll = sv.ExtentWidth > sv.ViewportWidth;
        leftBtn.Visibility = canScroll && sv.HorizontalOffset > 0 ? Visibility.Visible : Visibility.Collapsed;
        rightBtn.Visibility = canScroll && sv.HorizontalOffset < sv.ExtentWidth - sv.ViewportWidth ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCWScrollLeft(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ContinueWatchingScroll.ScrollToHorizontalOffset(Math.Max(0, ContinueWatchingScroll.HorizontalOffset - 260));
        UpdateScrollButtons(ContinueWatchingScroll, CWScrollLeftBtn, CWScrollRightBtn);
    }

    private void OnCWScrollRight(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ContinueWatchingScroll.ScrollToHorizontalOffset(Math.Min(ContinueWatchingScroll.ExtentWidth - ContinueWatchingScroll.ViewportWidth, ContinueWatchingScroll.HorizontalOffset + 260));
        UpdateScrollButtons(ContinueWatchingScroll, CWScrollLeftBtn, CWScrollRightBtn);
    }

    private void OnRecentScrollLeft(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        RecentScroll.ScrollToHorizontalOffset(Math.Max(0, RecentScroll.HorizontalOffset - 260));
        UpdateScrollButtons(RecentScroll, RecentScrollLeftBtn, RecentScrollRightBtn);
    }

    private void OnRecentScrollRight(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        RecentScroll.ScrollToHorizontalOffset(Math.Min(RecentScroll.ExtentWidth - RecentScroll.ViewportWidth, RecentScroll.HorizontalOffset + 260));
        UpdateScrollButtons(RecentScroll, RecentScrollLeftBtn, RecentScrollRightBtn);
    }

    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1 || e.Key == Key.OemQuestion)
        {
            ShowShortcutsOverlay();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _shortcutsWindow != null)
        {
            CloseShortcutsOverlay();
            e.Handled = true;
        }
    }

    private void ShowShortcutsOverlay()
    {
        if (_shortcutsWindow != null) { _shortcutsWindow.Focus(); return; }

        var win = Window.GetWindow(this);
        if (win == null) return;

        _shortcutsWindow = new Window
        {
            Title = "Keyboard Shortcuts",
            Width = 480,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = win,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowState = WindowState.Normal,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false
        };

        var overlay = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0x0d, 0x0d, 0x0d)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(28, 24, 28, 24),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2a, 0x2a, 0x2a)),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel();

        var title = new TextBlock
        {
            Text = "⌨  Keyboard Shortcuts",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 16)
        };
        stack.Children.Add(title);

        var shortcuts = new (string key, string desc)[]
        {
            ("Space", "Play / Pause"),
            ("← / →", "Seek -10s / +10s"),
            ("↑ / ↓", "Volume +5 / -5"),
            ("N", "Next in playlist"),
            ("[ / ]", "Speed -0.25 / +0.25"),
            ("F", "Toggle fullscreen"),
            ("S", "Save screenshot"),
            ("Esc", "Exit fullscreen / close"),
            ("? / F1", "Show this overlay"),
        };

        foreach (var (key, desc) in shortcuts)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var keyBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2a, 0x2a, 0x2a)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 16, 6)
            };
            var keyText = new TextBlock
            {
                Text = key,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            keyBorder.Child = keyText;
            Grid.SetColumn(keyBorder, 0);
            row.Children.Add(keyBorder);

            var descText = new TextBlock
            {
                Text = desc,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9c, 0xa3, 0xaf)),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(descText, 1);
            row.Children.Add(descText);

            stack.Children.Add(row);
        }

        var closeHint = new TextBlock
        {
            Text = "Press Esc or click outside to close",
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6b, 0x72, 0x80)),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0)
        };
        stack.Children.Add(closeHint);

        overlay.Child = stack;
        _shortcutsWindow.Content = overlay;

        _shortcutsWindow.MouseLeftButtonDown += (_, _) => CloseShortcutsOverlay();
        _shortcutsWindow.Deactivated += (_, _) => CloseShortcutsOverlay();
        _shortcutsWindow.KeyDown += (_, e2) => { if (e2.Key == Key.Escape) CloseShortcutsOverlay(); };

        _shortcutsWindow.Show();
    }

    private void CloseShortcutsOverlay()
    {
        if (_shortcutsWindow != null)
        {
            _shortcutsWindow.Close();
            _shortcutsWindow = null;
        }
    }

    private static int ExtractYearFromFilename(string name) => FilenameUtils.ExtractYearFromFilename(name);

    private async System.Threading.Tasks.Task SavePlaylistsAsync()
    {
        var config = await _configService.LoadAsync();
        config.Playlists = _playlists;
        config.Collections = _collections;
        await _configService.SaveAsync(config);
    }

    private async System.Threading.Tasks.Task SaveCollectionsAsync()
    {
        var config = await _configService.LoadAsync();
        config.Collections = _collections;
        await _configService.SaveAsync(config);
    }
}

