using CommunityToolkit.Mvvm.ComponentModel;
using FinishReplay.Models;

namespace FinishReplay.ViewModels;

/// <summary>A timing marker shown both as a clickable tick on the timeline and in the marker list.</summary>
public partial class ReplayMarkerViewModel : ObservableObject
{
    public ReplayMarkerViewModel(TimingTrigger trigger, double fraction)
    {
        Trigger = trigger;
        _fraction = fraction;
    }

    public TimingTrigger Trigger { get; }

    public TimingTriggerType Type => Trigger.Type;
    public TimeSpan VideoTime => Trigger.VideoTime;
    public string Label => $"{Trigger.Type}  {Trigger.VideoTime:mm\\:ss\\.fff}";

    /// <summary>Position along the timeline, 0..1.</summary>
    [ObservableProperty] private double _fraction;
}
