using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace BPlayer.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var verStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        AppTitle.Text = "BPlayer";
        VersionText.Text = $"v{verStr}";
    }

    private void OnLinkClick(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex) { BPlayer.Services.Logger.Warn($"AboutWindow: failed to open link: {ex.Message}"); }
        e.Handled = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
