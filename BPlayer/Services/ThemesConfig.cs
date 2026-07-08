using System.IO;
using System.Text.Json;
using BPlayer.Models;

namespace BPlayer.Services;

public static class ThemesConfig
{
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "themes.json");

    private static List<ThemeDefinition>? _cache;

    public static List<ThemeDefinition> Load()
    {
        if (_cache != null) return _cache;

        if (File.Exists(ConfigPath))
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                var themes = JsonSerializer.Deserialize<List<ThemeDefinition>>(json);
                if (themes != null && themes.Count > 0)
                {
                    _cache = themes;
                    Logger.Info($"Loaded {themes.Count} themes from {ConfigPath}");
                    return _cache;
                }
                Logger.Warn("Themes file empty or invalid, reseeding defaults");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load themes: {ex.Message}");
            }
        }

        _cache = GetDefaultThemes();
        Logger.Info($"Using {_cache.Count} default themes");
        return _cache;
    }

    public static async Task<List<ThemeDefinition>> LoadAsync()
    {
        if (_cache != null) return _cache;

        if (File.Exists(ConfigPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(ConfigPath);
                var themes = JsonSerializer.Deserialize<List<ThemeDefinition>>(json);
                if (themes != null && themes.Count > 0)
                {
                    _cache = themes;
                    Logger.Info($"Loaded {themes.Count} themes from {ConfigPath}");
                    return _cache;
                }
                Logger.Warn("Themes file empty or invalid, using defaults");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load themes: {ex.Message}");
            }
        }

        _cache = GetDefaultThemes();
        Logger.Info($"Using {_cache.Count} default themes");
        return _cache;
    }

    public static void Save(List<ThemeDefinition> themes)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (dir != null) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(themes, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
            _cache = themes;
            Logger.Info($"Saved {themes.Count} themes to {ConfigPath}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save themes: {ex.Message}");
        }
    }

    public static async Task SaveAsync(List<ThemeDefinition> themes)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (dir != null) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(themes, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(ConfigPath, json);
            _cache = themes;
            Logger.Info($"Saved {themes.Count} themes to {ConfigPath}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save themes: {ex.Message}");
        }
    }

    public static List<ThemeDefinition> GetDefaultThemes() => new()
    {
        new ThemeDefinition
        {
            Name = "Dark", BgDark = "#0d0d0d", BgSurface = "#161616",
            BgCard = "#1e1e1e", BgCardHover = "#2a2a2a",
            Accent = "#e94560", AccentHover = "#ff6b81",
            TextPrimary = "#f5f5f5", TextSecondary = "#9ca3af",
            BorderColor = "#2a2a2a", ButtonBg = "#252525",
            ButtonHoverBg = "#333333", ScrollThumb = "#3a3a3a"
        },
        new ThemeDefinition
        {
            Name = "Blue", BgDark = "#0a0e1a", BgSurface = "#141f33",
            BgCard = "#1a2a42", BgCardHover = "#223854",
            Accent = "#3b82f6", AccentHover = "#60a5fa",
            TextPrimary = "#f3f4f6", TextSecondary = "#6b7280",
            BorderColor = "#1e3a5f", ButtonBg = "#1e2d44",
            ButtonHoverBg = "#2a4060", ScrollThumb = "#2a4060"
        },
        new ThemeDefinition
        {
            Name = "Red", BgDark = "#0f0a0a", BgSurface = "#201414",
            BgCard = "#2e1a1a", BgCardHover = "#402222",
            Accent = "#dc2626", AccentHover = "#ef4444",
            TextPrimary = "#f3f4f6", TextSecondary = "#6b7280",
            BorderColor = "#5f1e1e", ButtonBg = "#2e1a1a",
            ButtonHoverBg = "#402222", ScrollThumb = "#3a2222"
        },
        new ThemeDefinition
        {
            Name = "Amber", BgDark = "#0d0d09", BgSurface = "#1a1814",
            BgCard = "#24211c", BgCardHover = "#302c25",
            Accent = "#f59e0b", AccentHover = "#fbbf24",
            TextPrimary = "#f5f5f4", TextSecondary = "#a8a29e",
            BorderColor = "#2e2a24", ButtonBg = "#2a2722",
            ButtonHoverBg = "#38342d", ScrollThumb = "#403c35"
        },
        new ThemeDefinition
        {
            Name = "Modern", BgDark = "#0f1117", BgSurface = "#181b24",
            BgCard = "#1f2330", BgCardHover = "#2a2f3f",
            Accent = "#06b6d4", AccentHover = "#22d3ee",
            TextPrimary = "#e2e8f0", TextSecondary = "#94a3b8",
            BorderColor = "#2d3348", ButtonBg = "#252a3a",
            ButtonHoverBg = "#323a50", ScrollThumb = "#3a4258"
        }
    };
}
