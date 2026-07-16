using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using BPlayer.Models;
using BPlayer.Pages;
using BPlayer.Services;

namespace BPlayer;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly VideoScannerService _scannerService = new();
    private ObservableCollection<VideoItem>? _allVideos;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var config = await _configService.LoadAsync();
        var videos = await _scannerService.ScanDirectoriesAsync(config.VideoSourcePaths);

        _allVideos = new ObservableCollection<VideoItem>(videos);

        var enricher = new MetadataEnricherService(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, config.MetadataSources);

        // Pre-load cached thumbnails from disk so the UI doesn't show blank placeholders
        enricher.ThumbnailService.PreloadCachedThumbnails(videos);

        var dashboard = new DashboardPage(_allVideos, config.Playlists ?? new());
        MainFrame.Navigate(dashboard);
    }
}
