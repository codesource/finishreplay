using FinishReplay.Models;

namespace FinishReplay.Services.Settings;

/// <summary>
/// Loads and persists <see cref="AppSettings"/>. A single mutable <see cref="Current"/> instance is
/// shared across view models; call <see cref="SaveAsync"/> after edits and listen to
/// <see cref="Changed"/> to react to saves.
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler? Changed;

    Task LoadAsync();
    Task SaveAsync();
}
