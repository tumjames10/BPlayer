using System.Windows;
using System.Windows.Media;
using BPlayer.Models;

namespace BPlayer.Services;

public static class ThemeService
{
    public const string DefaultTheme = "Dark";

    private static List<ThemeDefinition> _themes = new();
    private static readonly object _lock = new();

    public static string CurrentTheme { get; private set; } = DefaultTheme;
    public static IReadOnlyList<ThemeDefinition> Themes => _themes.AsReadOnly();

    public static void Initialize()
    {
        Logger.Debug("ThemeService.Initialize (sync) started");
        _themes = ThemesConfig.Load();
        Logger.Debug($"Loaded {_themes.Count} themes from config");
    }

    public static async Task InitializeAsync()
    {
        Logger.Debug("ThemeService.InitializeAsync started");
        _themes = await ThemesConfig.LoadAsync();
        Logger.Debug($"Loaded {_themes.Count} themes from config");
    }

    public static ThemeDefinition? GetTheme(string name)
    {
        return _themes.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static void ApplyTheme(string themeName)
    {
        Logger.Debug($"ApplyTheme: {themeName}");

        var theme = GetTheme(themeName);
        if (theme == null)
        {
            Logger.Warn($"Theme '{themeName}' not found, falling back to {DefaultTheme}");
            theme = GetTheme(DefaultTheme);
            if (theme == null)
            {
                Logger.Error("No themes available, cannot apply");
                return;
            }
            themeName = DefaultTheme;
        }

        lock (_lock)
        {
            CurrentTheme = themeName;
            var app = Application.Current;
            if (app == null)
            {
                Logger.Error("Application.Current is null");
                return;
            }

            var dict = BuildResourceDictionary(theme);

            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(dict);

            SetSystemColors(app);
            Logger.Info($"Applied theme: {themeName}");
        }
    }

    public static string LoadSavedTheme()
    {
        try
        {
            var config = Task.Run(() => new ConfigService().LoadAsync()).GetAwaiter().GetResult();
            var saved = config.SelectedTheme;
            if (!string.IsNullOrEmpty(saved) && GetTheme(saved) != null)
            {
                Logger.Debug($"Loaded saved theme: {saved}");
                return saved;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"LoadSavedTheme: {ex.Message}");
        }
        return DefaultTheme;
    }

    public static async Task<string> LoadSavedThemeAsync()
    {
        try
        {
            var configService = new ConfigService();
            var config = await configService.LoadAsync();
            var saved = config.SelectedTheme;
            if (!string.IsNullOrEmpty(saved) && GetTheme(saved) != null)
            {
                Logger.Debug($"Loaded saved theme: {saved}");
                return saved;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"LoadSavedThemeAsync: {ex.Message}");
        }
        return DefaultTheme;
    }

    public static async Task SaveThemeAsync(string themeName)
    {
        try
        {
            var configService = new ConfigService();
            var config = await configService.LoadAsync();
            config.SelectedTheme = themeName;
            await configService.SaveAsync(config);
            Logger.Debug($"Saved theme preference: {themeName}");
        }
        catch (Exception ex)
        {
            Logger.Error($"SaveThemeAsync: {ex.Message}");
        }
    }

    private static ResourceDictionary BuildResourceDictionary(ThemeDefinition theme)
    {
        var dict = new ResourceDictionary();

        AddColor(dict, "BgDark", theme.BgDark);
        AddColor(dict, "BgSurface", theme.BgSurface);
        AddColor(dict, "BgCard", theme.BgCard);
        AddColor(dict, "BgCardHover", theme.BgCardHover);
        AddColor(dict, "Accent", theme.Accent);
        AddColor(dict, "AccentHover", theme.AccentHover);
        AddColor(dict, "TextPrimary", theme.TextPrimary);
        AddColor(dict, "TextSecondary", theme.TextSecondary);
        AddColor(dict, "BorderColor", theme.BorderColor);
        AddColor(dict, "ButtonBg", theme.ButtonBg);
        AddColor(dict, "ButtonHoverBg", theme.ButtonHoverBg);

        AddBrush(dict, "BgDarkBrush", theme.BgDark);
        AddBrush(dict, "BgSurfaceBrush", theme.BgSurface);
        AddBrush(dict, "BgCardBrush", theme.BgCard);
        AddBrush(dict, "BgCardHoverBrush", theme.BgCardHover);
        AddBrush(dict, "AccentBrush", theme.Accent);
        AddBrush(dict, "AccentHoverBrush", theme.AccentHover);
        AddBrush(dict, "TextPrimaryBrush", theme.TextPrimary);
        AddBrush(dict, "TextSecondaryBrush", theme.TextSecondary);
        AddBrush(dict, "BorderColorBrush", theme.BorderColor);
        AddBrush(dict, "ButtonBgBrush", theme.ButtonBg);
        AddBrush(dict, "ButtonHoverBgBrush", theme.ButtonHoverBg);
        AddBrush(dict, "ScrollThumbBrush", theme.ScrollThumb);

        return dict;
    }

    private static void AddColor(ResourceDictionary dict, string key, string hex)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            dict[key] = color;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to parse color '{hex}' for key '{key}': {ex.Message}");
            dict[key] = System.Windows.Media.Colors.Black;
        }
    }

    private static void AddBrush(ResourceDictionary dict, string key, string hex)
    {
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            dict[key] = brush;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to create brush '{hex}' for key '{key}': {ex.Message}");
            dict[key] = new SolidColorBrush(System.Windows.Media.Colors.Black);
        }
    }

    private static void SetSystemColors(Application app)
    {
        try
        {
            app.Resources[System.Windows.SystemColors.WindowBrushKey] = app.Resources["BgSurfaceBrush"];
            app.Resources[System.Windows.SystemColors.WindowTextBrushKey] = app.Resources["TextPrimaryBrush"];
            app.Resources[System.Windows.SystemColors.MenuBrushKey] = app.Resources["BgSurfaceBrush"];
            app.Resources[System.Windows.SystemColors.MenuTextBrushKey] = app.Resources["TextPrimaryBrush"];
            app.Resources[System.Windows.SystemColors.HighlightBrushKey] = app.Resources["AccentBrush"];
            app.Resources[System.Windows.SystemColors.HighlightTextBrushKey] = new SolidColorBrush(Colors.White);
            app.Resources[System.Windows.SystemColors.ControlBrushKey] = app.Resources["BgCardBrush"];
            app.Resources[System.Windows.SystemColors.ControlTextBrushKey] = app.Resources["TextPrimaryBrush"];
            Logger.Debug("System colors updated");
        }
        catch (Exception ex)
        {
            Logger.Error($"SetSystemColors: {ex.Message}");
        }
    }
}
