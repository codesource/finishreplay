using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FinishReplay.ViewModels;
using FinishReplay.Views;

namespace FinishReplay;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Simple manual composition root. If the service graph grows, swap this
            // for Microsoft.Extensions.DependencyInjection without touching the views.
            var main = new MainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = main };

            // Release cameras (and kill the worker processes holding a USB webcam) when the app exits —
            // otherwise the device stays active after the window closes.
            desktop.ShutdownRequested += (_, _) => main.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
