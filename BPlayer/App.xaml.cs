using System.IO;
using System.Windows;
using BPlayer.Services;

namespace BPlayer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        try { SafeLoggerInit(); } catch { }

        DispatcherUnhandledException += (_, args) =>
        {
            try { Logger.Error($"Unhandled: {args.Exception.Message}"); } catch { }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                var ex = args.ExceptionObject as Exception;
                try { Logger.Error($"Fatal: {ex?.Message}"); } catch { }
            }
            catch { }
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
            try { Logger.Error($"Theme init failed: {ex.Message}"); } catch { }
            try { ThemeService.ApplyTheme("Dark"); } catch { }
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

            Logger.Info($"AppData folder: {appData}");
            Logger.Info($"Cache folder: {cacheDir}");
        }
        catch (Exception ex)
        {
            try { Logger.Error($"Failed to create app data directories: {ex.Message}"); } catch { }
        }

        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var libvlcPath = Path.Combine(baseDir, "libvlc", "win-x64");
            if (Directory.Exists(libvlcPath))
                LibVLCSharp.Shared.Core.Initialize(libvlcPath);
            else
                LibVLCSharp.Shared.Core.Initialize();
        }
        catch (Exception ex)
        {
            try { Logger.Error($"VLC init failed: {ex.Message}"); } catch { }
        }

        base.OnStartup(e);
    }

    private static void SafeLoggerInit()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPlayer");
            Directory.CreateDirectory(logDir);
        }
        catch { }
    }
}
