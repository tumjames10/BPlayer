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
        try
        {
            Logger.Debug("MainWindow.OnLoaded: loading config");
            var config = await _configService.LoadAsync();
            Logger.Debug($"MainWindow.OnLoaded: config loaded, sources={config.VideoSourcePaths.Count}");
            var videos = await _scannerService.ScanDirectoriesAsync(config.VideoSourcePaths);
            Logger.Debug($"MainWindow.OnLoaded: scanned {videos.Count} videos");

            _allVideos = new ObservableCollection<VideoItem>(videos);

            var enricher = new MetadataEnricherService(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, config.MetadataSources);

            enricher.ThumbnailService.PreloadCachedThumbnails(videos);

            Logger.Debug("MainWindow.OnLoaded: navigating to Dashboard");
            var dashboard = new DashboardPage(_allVideos, config.Playlists ?? new());
            MainFrame.Navigate(dashboard);
            Logger.Info("MainWindow.OnLoaded: Dashboard loaded");
        }
        catch (Exception ex)
        {
            Logger.Error($"MainWindow.OnLoaded failed: {ex}");
            try
            {
                MessageBox.Show($"Failed to load dashboard:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }
    }
}
