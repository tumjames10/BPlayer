using System.IO;
using System.Windows;
using BPlayer.Services;

namespace BPlayer;

public partial class App : System.Windows.Application
{
    private void ReportStartupError(string context, string message)
    {
        ReportStartupError(context, message, "Warning");
    }

    private void ReportStartupError(string context, string message, string caption)
    {
        var full = $"{context}:\n{message}\n\nThe application may not work correctly.";
        try { Logger.Error(full); } catch { }
        try { MessageBox.Show(full, caption, MessageBoxButton.OK, MessageBoxImage.Warning); } catch { }
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

    private string GetSafeAppDataPath()
    {
        try
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPlayer");
        }
        catch
        {
            var fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            try { Directory.CreateDirectory(fallback); } catch { }
            return fallback;
        }
    }

    private void WriteCrashMarker(string message)
    {
        try
        {
            var dir = GetSafeAppDataPath();
            File.WriteAllText(Path.Combine(dir, "crash_marker.txt"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }
        catch { }
    }

    private bool EnsureThemeApplied()
    {
        try
        {
            Logger.Debug("OnStartup: initializing themes synchronously");
            ThemeService.Initialize();
            var savedTheme = ThemeService.LoadSavedTheme();
            ThemeService.ApplyTheme(savedTheme);
            Logger.Info($"App started with theme: {savedTheme}");
            return true;
        }
        catch (Exception ex)
        {
            ReportStartupError("Theme initialization", ex.Message);
            try { ThemeService.ApplyTheme("Dark"); return true; } catch { }
        }
        return false;
    }

    private void EnsureDirectoriesExist()
    {
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
    }

    private void InitializeVlc()
    {
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
    }

    private void CreateMainWindowSafely()
    {
        try
        {
            Logger.Debug("Creating MainWindow");
            var mainWindow = new MainWindow();
            mainWindow.Show();
            Logger.Info("MainWindow created successfully");
        }
        catch (Exception ex)
        {
            WriteCrashMarker($"Window creation failed: {ex.GetType().Name}: {ex.Message}");
            ReportStartupError("Window creation", ex.ToString(), "Fatal Error");
            Shutdown(-1);
        }
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        SafeLoggerInit();
        WriteCrashMarker("Starting up");

        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashMarker($"Unhandled dispatcher: {args.Exception.GetType().Name}: {args.Exception.Message}");
            try { Logger.Error($"Unhandled: {args.Exception}"); } catch { }
            try
            {
                MessageBox.Show($"An unexpected error occurred:\n{args.Exception.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var msg = ex?.ToString() ?? "Unknown fatal error";
            WriteCrashMarker($"Fatal: {msg}");
            try { Logger.Error($"Fatal: {msg}"); } catch { }
            try
            {
                MessageBox.Show($"A fatal error occurred:\n{ex?.Message ?? "Unknown"}",
                    "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        };

        Logger.Debug($"OnStartup: BaseDir={AppDomain.CurrentDomain.BaseDirectory}");
        Logger.Debug($"OnStartup: OSVersion={Environment.OSVersion}");

        EnsureThemeApplied();
        EnsureDirectoriesExist();
        InitializeVlc();

        Logger.Debug("OnStartup: creating window");
        CreateMainWindowSafely();

        base.OnStartup(e);
    }
}
