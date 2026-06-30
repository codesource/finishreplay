using FinishReplay.Models;

namespace FinishReplay.Services.Camera;

/// <summary>
/// Default <see cref="ICameraManager"/>: delegates discovery/opening to the provider registry
/// and remembers the active selection.
/// </summary>
public sealed class CameraManager : ICameraManager
{
    private readonly CameraProviderRegistry _registry;

    public CameraManager(CameraProviderRegistry registry)
    {
        _registry = registry;
    }

    public CameraDevice? ActiveCamera { get; private set; }

    public IReadOnlyList<ICameraProvider> Providers => _registry.Providers;

    public Task<IReadOnlyList<CameraDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
        => _registry.DiscoverAllAsync(cancellationToken);

    public void SelectCamera(CameraDevice? camera) => ActiveCamera = camera;
}
