using System.IO;
using System.Windows;
using BPlayer.Services;

namespace BPlayer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Error($"Unhandled: {args.Exception.Message}");
            MessageBox.Show($"Unhandled error: {args.Exception.Message}\n{args.Exception.StackTrace}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Logger.Error($"Fatal: {ex?.Message}");
            MessageBox.Show($"Fatal error: {ex?.Message}", "Fatal",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        };

        try
        {
            Logger.Debug("OnStartup: initializing themes synchronously");
            ThemeService.Initialize();
            var savedTheme = ThemeService.LoadSavedTheme();
            ThemeService.ApplyTheme(savedTheme);
            Logger.Info($"App started with theme: {savedTheme}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Theme init failed: {ex.Message}");
            ThemeService.ApplyTheme("Dark");
        }

        try
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPlayer");
            Directory.CreateDirectory(appData);

            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BPlayer", "thumbcache");
            Directory.CreateDirectory(cacheDir);
            ThumbnailService.EnsureCacheDir();

            Logger.Info($"AppData folder: {appData}");
            Logger.Info($"Cache folder: {cacheDir}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to create app data directories: {ex.Message}");
        }

        try
        {
            LibVLCSharp.Shared.Core.Initialize();
        }
        catch (Exception ex)
        {
            Logger.Error($"VLC init failed: {ex.Message}");
        }

        base.OnStartup(e);
    }
}
