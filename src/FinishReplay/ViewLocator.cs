using Avalonia.Controls;
using Avalonia.Controls.Templates;
using FinishReplay.ViewModels;

namespace FinishReplay;

/// <summary>
/// Maps a ViewModel instance to its matching View by naming convention,
/// e.g. <c>FinishReplay.ViewModels.ReplayViewModel</c> -> <c>FinishReplay.Views.ReplayView</c>.
/// Registered as an application-wide <see cref="IDataTemplate"/> in App.axaml.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!
            .Replace("ViewModels", "Views", System.StringComparison.Ordinal)
            .Replace("ViewModel", "View", System.StringComparison.Ordinal);

        var type = System.Type.GetType(name);
        return type is not null
            ? (Control)System.Activator.CreateInstance(type)!
            : new TextBlock { Text = "View not found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
