using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FinishReplay.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        new AboutWindow().ShowDialog(this);
    }
}
