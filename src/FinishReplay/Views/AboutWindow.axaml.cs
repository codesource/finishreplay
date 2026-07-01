using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FinishReplay.Views;

public partial class AboutWindow : Window
{
    private const string RepositoryUrl = "https://github.com/codesource/finishreplay";
    private const string LicenseUrl = "https://www.gnu.org/licenses/gpl-3.0.html";
    private const string FfmpegUrl = "https://ffmpeg.org";

    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.1.0";
        // Trim the build-metadata suffix (e.g. "0.1.0+abc123") for display.
        var plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];

        VersionText.Text = $"Version {version}";
        CopyrightText.Text = "Copyright © 2026 Matthias Toscanelli / code-source.ch";
    }

    private void OnLicenseClick(object? sender, RoutedEventArgs e) => OpenUrl(LicenseUrl);
    private void OnSourceClick(object? sender, RoutedEventArgs e) => OpenUrl(RepositoryUrl);
    private void OnFfmpegClick(object? sender, RoutedEventArgs e) => OpenUrl(FfmpegUrl);
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // ignore — nothing actionable if no browser is available
        }
    }
}
