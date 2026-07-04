using Avalonia.Controls;
using Avalonia.Input;
using FinishReplay.ViewModels;

namespace FinishReplay.Views;

public partial class ReplayView : UserControl
{
    public ReplayView()
    {
        InitializeComponent();
    }

    // Click or drag anywhere on the timeline to seek to that frame.
    private void OnTimelinePressed(object? sender, PointerPressedEventArgs e) => SeekFromPointer(sender, e);

    private void OnTimelineMoved(object? sender, PointerEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.IsLeftButtonPressed)
            SeekFromPointer(sender, e);
    }

    private void SeekFromPointer(object? sender, PointerEventArgs e)
    {
        if (sender is not Control track || DataContext is not ReplayViewModel vm)
            return;

        var width = track.Bounds.Width;
        if (width > 0)
            vm.SeekToFraction(e.GetPosition(track).X / width);
    }
}
