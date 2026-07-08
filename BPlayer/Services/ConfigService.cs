using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BPlayer.Models;

namespace BPlayer.Services;

public class ConfigService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BPlayer", "config.json");

    public async Task<AppConfig> LoadAsync()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new AppConfig();

            var json = await File.ReadAllTextAsync(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();

            // Migrate old single-provider config to new multi-source format
            if (config.MetadataSources.Count == 0)
                TryMigrateLegacyProvider(json, config);

            // If still empty, seed a default source from url presets
            if (config.MetadataSources.Count == 0)
            {
                var presets = UrlConfigService.Load().MetadataPresets;
                var defaultSrc = presets.FirstOrDefault(p => p.Name == "OMDb");
                if (defaultSrc != null)
                {
                    config.MetadataSources.Add(new MetadataSource
                    {
                        Name = defaultSrc.Name,
                        ApiUrl = defaultSrc.ApiUrl,
                        ApiKey = defaultSrc.ApiKey,
                        IsBuiltIn = true
                    });
                }
            }

            return config;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load config: {ex.Message}");
            return new AppConfig();
        }
    }

    private static void TryMigrateLegacyProvider(string json, AppConfig config)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("MetadataProvider", out var old)) return;

            var src = new MetadataSource { IsBuiltIn = true };
            if (old.TryGetProperty("ApiUrl", out var url)) src.ApiUrl = url.GetString() ?? src.ApiUrl;
            if (old.TryGetProperty("ApiKey", out var key)) src.ApiKey = key.GetString() ?? src.ApiKey;
            if (old.TryGetProperty("ProviderType", out var pt)) src.Name = pt.GetString() ?? src.Name;
            if (old.TryGetProperty("JsonResponsePath", out var jp)) src.JsonResponsePath = jp.GetString() ?? "";
            if (old.TryGetProperty("PosterBaseUrl", out var pb)) src.PosterBaseUrl = pb.GetString() ?? "";

            config.MetadataSources.Add(src);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Config migration failed: {ex.Message}");
        }
    }

    public async Task SaveAsync(AppConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(ConfigPath, json);
    }

    public string? GetSavedThemeName()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json);
            return config?.SelectedTheme;
        }
        catch { return null; }
    }

    public async Task SaveThemeNameAsync(string theme)
    {
        var config = await LoadAsync();
        config.SelectedTheme = theme;
        await SaveAsync(config);
    }
}
