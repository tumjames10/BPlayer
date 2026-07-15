using System.IO;
using System.Windows;
using BPlayer.Services;

namespace BPlayer;

public partial class App : System.Windows.Application
{
    private void ReportStartupError(string context, string message)
    {
        try { Logger.Error($"{context}: {message}"); } catch { }
        MessageBox.Show($"{context}:\n{message}\n\nThe application may not work correctly.",
            "Startup Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void SafeLoggerInit()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPlayer");
            Directory.CreateDirectory(logDir);
        }
        catch { }
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        SafeLoggerInit();

        DispatcherUnhandledException += (_, args) =>
        {
            try { Logger.Error($"Unhandled: {args.Exception.Message}"); } catch { }
            MessageBox.Show($"An unexpected error occurred:\n{args.Exception.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var msg = ex?.Message ?? "Unknown fatal error";
            try { Logger.Error($"Fatal: {msg}"); } catch { }
            MessageBox.Show($"A fatal error occurred:\n{msg}",
                "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            ReportStartupError("Theme initialization", ex.Message);
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
            ReportStartupError("Directory setup", ex.Message);
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
            ReportStartupError("VLC initialization", ex.Message);
        }

        base.OnStartup(e);
    }
}
