using System.Globalization;
using Avalonia.Data.Converters;

namespace FinishReplay.Converters;

/// <summary>
/// Multi-value converter: [fraction (0..1), width] -> fraction * width. Used to place timeline
/// marker ticks, the progress fill and the position indicator at a pixel offset along the bar.
/// </summary>
public sealed class FractionWidthConverter : IMultiValueConverter
{
    public static FractionWidthConverter Instance { get; } = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return 0d;
        var fraction = values[0] is double f ? f : 0d;
        var width = values[1] is double w ? w : 0d;
        if (double.IsNaN(fraction) || double.IsNaN(width)) return 0d;
        return Math.Clamp(fraction, 0, 1) * width;
    }
}
