using FinishReplay.Models;

namespace FinishReplay.Services.Camera;

/// <summary>
/// Aggregates all registered <see cref="ICameraProvider"/>s. Discovery fans out across every
/// provider; opening a device is routed to the provider that matches its source type.
/// New transports (ONVIF, …) are added by registering another provider here.
/// </summary>
public sealed class CameraProviderRegistry
{
    private readonly IReadOnlyList<ICameraProvider> _providers;

    public CameraProviderRegistry(IEnumerable<ICameraProvider> providers)
    {
        _providers = providers.ToList();
    }

    public IReadOnlyList<ICameraProvider> Providers => _providers;

    public ICameraProvider? FindProvider(string providerName) =>
        _providers.FirstOrDefault(p => string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Discover devices across all providers; a failing provider is skipped, not fatal.</summary>
    public async Task<IReadOnlyList<CameraDevice>> DiscoverAllAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<CameraDevice>();
        foreach (var provider in _providers)
        {
            try
            {
                all.AddRange(await provider.DiscoverAsync(cancellationToken).ConfigureAwait(false));
            }
            catch
            {
                // TODO: surface provider discovery failures to the UI/log instead of swallowing.
            }
        }
        return all;
    }

    public Task<ICameraStream> OpenAsync(CameraDevice device, CameraSettings settings, CancellationToken cancellationToken = default)
    {
        var provider = FindProvider(device.ProviderName)
            ?? throw new InvalidOperationException($"No camera provider registered for '{device.ProviderName}'.");
        return provider.OpenAsync(device, settings, cancellationToken);
    }
}
