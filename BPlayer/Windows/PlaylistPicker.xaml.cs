using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BPlayer.Models;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace BPlayer.Windows;

public partial class PlaylistPicker : Window
{
    private readonly string _videoPath;
    private readonly List<Playlist> _playlists;

    public string? SelectedPlaylist { get; private set; }
    public bool IsNewPlaylist { get; private set; }

    public PlaylistPicker(string videoPath, List<Playlist> playlists)
    {
        InitializeComponent();
        _videoPath = videoPath;
        _playlists = playlists;

        LoadPlaylists();
    }

    private void LoadPlaylists()
    {
        PlaylistItems.Children.Clear();

        if (_playlists.Count == 0)
        {
            PlaylistItems.Children.Add(new TextBlock
            {
                Text = "No playlists yet",
                Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0x6b, 0x72, 0x80)),
                FontSize = 13,
                Margin = new Thickness(4, 12, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        foreach (var pl in _playlists)
        {
            var alreadyAdded = pl.VideoPaths.Contains(_videoPath);
            var item = CreatePlaylistItem(pl.Name, alreadyAdded);
            PlaylistItems.Children.Add(item);
        }
    }

    private Border CreatePlaylistItem(string name, bool alreadyAdded)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 2, 0, 2),
            Cursor = alreadyAdded ? Cursors.Arrow : Cursors.Hand,
            Opacity = alreadyAdded ? 0.5 : 1.0,
            Background = WpfBrushes.Transparent
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconText = alreadyAdded ? "✓" : "▶";
        var iconColor = alreadyAdded
            ? WpfColor.FromRgb(0x22, 0xc5, 0x5e)
            : WpfColor.FromRgb(0xe9, 0x45, 0x60);

        var icon = new TextBlock
        {
            Text = iconText,
            FontSize = 12,
            Foreground = new WpfSolidColorBrush(iconColor),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(icon, 0);

        var label = new TextBlock
        {
            Text = name,
            FontSize = 14,
            Foreground = new WpfSolidColorBrush(alreadyAdded
                ? WpfColor.FromRgb(0x6b, 0x72, 0x80)
                : WpfColor.FromRgb(0xf3, 0xf4, 0xf6)),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = alreadyAdded ? FontWeights.Normal : FontWeights.Medium
        };
        Grid.SetColumn(label, 1);

        var status = new TextBlock
        {
            Text = alreadyAdded ? "Added" : "",
            FontSize = 11,
            Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0x22, 0xc5, 0x5e)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(status, 2);

        grid.Children.Add(icon);
        grid.Children.Add(label);
        grid.Children.Add(status);
        border.Child = grid;

        if (!alreadyAdded)
        {
            var capturedName = name;
            border.MouseLeftButtonDown += (_, _) =>
            {
                SelectedPlaylist = capturedName;
                IsNewPlaylist = false;
                DialogResult = true;
            };

            border.MouseEnter += (_, _) =>
            {
                border.Background = new WpfSolidColorBrush(WpfColor.FromRgb(0x1e, 0x1e, 0x30));
            };
            border.MouseLeave += (_, _) =>
            {
                border.Background = WpfBrushes.Transparent;
            };
        }

        return border;
    }

    private void OnNewPlaylistClick(object sender, MouseButtonEventArgs e)
    {
        SelectedPlaylist = $"Playlist {_playlists.Count + 1}";
        IsNewPlaylist = true;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnCloseClick(object sender, MouseButtonEventArgs e)
    {
        DialogResult = false;
    }
}
