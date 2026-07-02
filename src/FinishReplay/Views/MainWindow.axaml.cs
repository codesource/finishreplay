using Avalonia.Controls;
using Avalonia.Interactivity;
using FinishReplay.ViewModels;

namespace FinishReplay.Views;

public partial class MainWindow : Window
{
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        new AboutWindow().ShowDialog(this);
    }

    // Settings opens as its own window (single instance), sharing the SettingsViewModel.
    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        if (DataContext is not MainViewModel main)
            return;

        // Settings are locked while a session is recording/finishing.
        if (main.Recording.IsBusy)
            return;

        _settingsWindow = new SettingsWindow { DataContext = main.Settings };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }
}
