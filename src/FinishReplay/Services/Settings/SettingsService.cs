using System.Text.Json;
using FinishReplay.Models;

namespace FinishReplay.Services.Settings;

/// <summary>
/// JSON-file settings store at <c>%AppData%/FinishReplay/settings.json</c>
/// (platform config dir). Tolerates a missing/corrupt file by falling back to defaults.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly string _path;

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FinishReplay");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? Changed;

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_path))
            {
                await using var stream = File.OpenRead(_path);
                var loaded = await JsonSerializer
                    .DeserializeAsync<AppSettings>(stream, AppSettings.JsonOptions)
                    .ConfigureAwait(false);
                if (loaded is not null)
                    Current = loaded;
            }
        }
        catch
        {
            // TODO: surface load errors to a log/UI; defaults are used meanwhile.
            Current = new AppSettings();
        }

        if (string.IsNullOrWhiteSpace(Current.StorageDirectory))
            Current.StorageDirectory = AppSettings.DefaultStorageDirectory;
    }

    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(Current, AppSettings.JsonOptions);
        await File.WriteAllTextAsync(_path, json).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
