using Avalonia.Controls;
using Avalonia.Interactivity;
using FinishReplay.Models;
using FinishReplay.Services.Camera.Providers.Usb;
using FinishReplay.ViewModels;

namespace FinishReplay.Views;

public partial class CameraConfigWindow : Window
{
    private readonly CameraConfigViewModel _viewModel;

    // Parameterless ctor for the XAML previewer only.
    public CameraConfigWindow() : this(new CameraProfile { DisplayName = "Camera", SourceType = "MJPEG" })
    {
    }

    public CameraConfigWindow(CameraProfile profile)
    {
        _viewModel = new CameraConfigViewModel(profile);
        DataContext = _viewModel;
        InitializeComponent();
    }

    /// <summary>True if the user applied changes.</summary>
    public bool Applied { get; private set; }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.Apply();
        Applied = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private void OnDriverPropertiesClick(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        DirectShowCameraProperties.ShowPropertyPages(_viewModel.DeviceId, hwnd);
    }
}
