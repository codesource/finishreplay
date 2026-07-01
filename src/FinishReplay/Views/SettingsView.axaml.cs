using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FinishReplay.ViewModels;

namespace FinishReplay.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    // Folder picker for the storage directory. Lives in code-behind because it needs the TopLevel.
    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where recordings are stored",
            AllowMultiple = false,
        });

        var picked = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(picked))
            vm.StorageDirectory = picked;
    }

    // Opens the per-camera configuration dialog (needs the owner window, so it lives in code-behind).
    private async void OnConfigureCameraClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: CameraSettingRowViewModel row })
            return;
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return;

        var dialog = new CameraConfigWindow(row.Profile);
        await dialog.ShowDialog(owner);

        if (dialog.Applied && DataContext is SettingsViewModel vm)
            vm.CameraSettings.ConfigureApplied(row);
    }
}
