using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BPlayer.Models;
using BPlayer.Services;

namespace BPlayer;

public partial class SettingsWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly ObservableCollection<string> _folders = new();
    private readonly ObservableCollection<SourceItem> _sources = new();
    private MetadataSource? _selectedSource;
    private bool _loading;

    public SettingsWindow()
    {
        try
        {
            InitializeComponent();
            FolderList.ItemsSource = _folders;
            SourceList.ItemsSource = _sources;
            SourceList.SelectionChanged += OnSourceSelectionChanged;
            Loaded += OnLoaded;
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Init error: {ex.Message}");
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _loading = true;
            var config = await _configService.LoadAsync();

            _folders.Clear();
            foreach (var path in config.VideoSourcePaths)
                _folders.Add(path);

            EnableMetadataCb.IsChecked = config.EnableOnlineMetadata;
            EnableThumbnailsCb.IsChecked = config.EnableVideoThumbnails;

            // Set subtitle font family
            for (int i = 0; i < SubtitleFontBox.Items.Count; i++)
                if (SubtitleFontBox.Items[i] is ComboBoxItem ci && ci.Content.ToString() == config.SubtitleFontFamily)
                    SubtitleFontBox.SelectedIndex = i;
            if (SubtitleFontBox.SelectedIndex < 0)
                SubtitleFontBox.SelectedIndex = 0;

            // Set subtitle font size
            var targetSize = config.SubtitleFontSize.ToString();
            for (int i = 0; i < SubtitleSizeBox.Items.Count; i++)
                if (SubtitleSizeBox.Items[i] is ComboBoxItem ci && ci.Content.ToString() == targetSize)
                    SubtitleSizeBox.SelectedIndex = i;
            if (SubtitleSizeBox.SelectedIndex < 0)
                SubtitleSizeBox.SelectedIndex = 1; // default 16

            _sources.Clear();
            foreach (var src in config.MetadataSources)
                _sources.Add(new SourceItem(src));

            UpdateNoSourcesLabel();
            SyncMetadataSection();

            (ThemeService.CurrentTheme switch
            {
                "Blue" => ThemeBlue,
                "Red" => ThemeRed,
                "Amber" => ThemeAmber,
                "Modern" => ThemeModern,
                _ => ThemeDark,
            }).IsChecked = true;
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Load error: {ex.Message}");
        }
        finally
        {
            _loading = false;
        }

        if (_sources.Count > 0)
            SourceList.SelectedIndex = 0;
    }

    private void UpdateNoSourcesLabel()
    {
        NoSourcesLabel.Visibility = _sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMetadataCheckChanged(object sender, RoutedEventArgs e)
    {
        SyncMetadataSection();
    }

    private void SyncMetadataSection()
    {
        MetadataSection.Visibility = EnableMetadataCb.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSourceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        if (SourceList.SelectedItem is SourceItem item)
        {
            _selectedSource = item.Source;
            SourceNameBox.Text = _selectedSource.Name;
            ApiKeyBox.Text = _selectedSource.ApiKey;
            ApiUrlBox.Text = _selectedSource.ApiUrl;
            ResponsePathBox.Text = _selectedSource.JsonResponsePath;
            PosterBaseBox.Text = _selectedSource.PosterBaseUrl;
            FieldTitle.Text = _selectedSource.Fields.Title;
            FieldYear.Text = _selectedSource.Fields.Year;
            FieldRating.Text = _selectedSource.Fields.Rating;
            FieldPoster.Text = _selectedSource.Fields.Poster;
            FieldPlot.Text = _selectedSource.Fields.Plot;
            SourceConfigPanel.Visibility = Visibility.Visible;
        }
        else
        {
            _selectedSource = null;
            SourceConfigPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void AddSource(MetadataSource src)
    {
        _sources.Add(new SourceItem(src));
        UpdateNoSourcesLabel();
        SourceList.SelectedIndex = _sources.Count - 1;
    }

    private void OnAddSourcePreset(object sender, RoutedEventArgs e)
    {
        var tag = (sender as System.Windows.Controls.Button)?.Tag as string;
        var presets = UrlConfigService.Load().MetadataPresets;

        MetadataSource? preset = null;
        if (tag == "TMDB")
            preset = presets.FirstOrDefault(p => p.Name == "TMDB");
        else
            preset = presets.FirstOrDefault(p => p.Name == "OMDb");

        if (preset != null)
        {
            var src = new MetadataSource
            {
                Name = preset.Name,
                ApiUrl = preset.ApiUrl,
                ApiKey = preset.ApiKey,
                JsonResponsePath = preset.JsonResponsePath,
                PosterBaseUrl = preset.PosterBaseUrl,
                IsBuiltIn = true,
                Fields = new FieldMapping
                {
                    Title = preset.Fields.Title,
                    Year = preset.Fields.Year,
                    Rating = preset.Fields.Rating,
                    Poster = preset.Fields.Poster,
                    Plot = preset.Fields.Plot
                }
            };
            AddSource(src);
        }
    }

    private void OnAddSourceCustom(object sender, RoutedEventArgs e)
    {
        var presets = UrlConfigService.Load().MetadataPresets;
        var example = presets.FirstOrDefault(p => p.Name == "Custom Example");
        AddSource(new MetadataSource
        {
            Name = "Custom",
            ApiUrl = example?.ApiUrl ?? "https://api.example.com/?q={title}",
        });
    }

    private void OnRemoveSource(object sender, RoutedEventArgs e)
    {
        if (SourceList.SelectedItem is SourceItem item)
        {
            if (item.Source.IsBuiltIn)
            {
                MessageBox.Show($"'{item.Source.Name}' is a built-in source and cannot be removed.",
                    "Protected Source", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _sources.Remove(item);
            _selectedSource = null;
            SourceConfigPanel.Visibility = Visibility.Collapsed;
            UpdateNoSourcesLabel();
        }
    }

    private void OnAddFolders(object sender, RoutedEventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var path = NativeHelper.ShowFolderPicker(hwnd);
        if (path != null && !_folders.Contains(path))
            _folders.Add(path);

        while (path != null)
        {
            var result = MessageBox.Show(
                "Add another folder?",
                "Add Folders",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) break;

            path = NativeHelper.ShowFolderPicker(hwnd);
            if (path != null && !_folders.Contains(path))
                _folders.Add(path);
        }
    }

    private void OnRemoveFolder(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is string path)
            _folders.Remove(path);
    }

    private static bool IsValidApiUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               uri.Scheme == "https";
    }

    private void FlushSelectedSource()
    {
        if (_selectedSource == null) return;
        _selectedSource.Name = SourceNameBox.Text;
        _selectedSource.ApiKey = ApiKeyBox.Text;
        _selectedSource.ApiUrl = ApiUrlBox.Text;
        _selectedSource.JsonResponsePath = ResponsePathBox.Text;
        _selectedSource.PosterBaseUrl = PosterBaseBox.Text;
        _selectedSource.Fields.Title = FieldTitle.Text;
        _selectedSource.Fields.Year = FieldYear.Text;
        _selectedSource.Fields.Rating = FieldRating.Text;
        _selectedSource.Fields.Poster = FieldPoster.Text;
        _selectedSource.Fields.Plot = FieldPlot.Text;
    }

    private void OnGeneralTabClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var accent = TryFindResource("AccentBrush") as System.Windows.Media.Brush
            ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xe9, 0x45, 0x60));
        GeneralTab.Background = accent;
        ThemesTab.Background = System.Windows.Media.Brushes.Transparent;
        GeneralContent.Visibility = Visibility.Visible;
        ThemesContent.Visibility = Visibility.Collapsed;
        ActiveTabLabel.Text = "General";
    }

    private void OnThemesTabClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var accent = TryFindResource("AccentBrush") as System.Windows.Media.Brush
            ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xe9, 0x45, 0x60));
        GeneralTab.Background = System.Windows.Media.Brushes.Transparent;
        ThemesTab.Background = accent;
        GeneralContent.Visibility = Visibility.Collapsed;
        ThemesContent.Visibility = Visibility.Visible;
        ActiveTabLabel.Text = "Themes";
    }

    private void OnThemeSelected(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        string theme;
        if (ThemeDark.IsChecked == true) theme = "Dark";
        else if (ThemeBlue.IsChecked == true) theme = "Blue";
        else if (ThemeRed.IsChecked == true) theme = "Red";
        else if (ThemeAmber.IsChecked == true) theme = "Amber";
        else if (ThemeModern.IsChecked == true) theme = "Modern";
        else return;

        ThemeService.ApplyTheme(theme);
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            FlushSelectedSource();

            foreach (var src in _sources.Select(s => s.Source))
            {
                if (!string.IsNullOrEmpty(src.ApiUrl) && !IsValidApiUrl(src.ApiUrl))
                {
                    MessageBox.Show($"'{src.Name}' has an invalid or non-HTTPS API URL.\nOnly HTTPS URLs are allowed for security.",
                        "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!string.IsNullOrEmpty(src.PosterBaseUrl) && !IsValidApiUrl(src.PosterBaseUrl))
                {
                    MessageBox.Show($"'{src.Name}' has an invalid or non-HTTPS Poster Base URL.\nOnly HTTPS URLs are allowed for security.",
                        "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var config = await _configService.LoadAsync();
            config.VideoSourcePaths = _folders.ToList();
            config.EnableOnlineMetadata = EnableMetadataCb.IsChecked == true;
            config.EnableVideoThumbnails = EnableThumbnailsCb.IsChecked == true;
            config.MetadataSources = _sources.Select(s => s.Source).ToList();
            config.SelectedTheme = ThemeService.CurrentTheme;
            config.SubtitleFontFamily = (SubtitleFontBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Arial";
            config.SubtitleFontSize = int.TryParse((SubtitleSizeBox.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var sz) ? sz : 16;

            await _configService.SaveAsync(config);
            DialogResult = true;
            Close();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Save error: {ex.Message}");
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public class SourceItem
{
    public MetadataSource Source { get; }
    public string Display => Source.Name;
    public SourceItem(MetadataSource source) => Source = source;
}
