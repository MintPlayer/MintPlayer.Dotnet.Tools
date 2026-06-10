using MintPlayer.Verz.Configuration;

namespace MintPlayer.Verz.Hosting;

/// <summary>
/// Lazily resolves the plugin catalog for the current invocation. The first call
/// triggers download/load of every plugin listed in verz.json; subsequent calls
/// return the same instance.
/// </summary>
internal sealed class PluginCatalogProvider(PluginLoader loader, VerzConfigProvider configProvider)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PluginCatalog? _cache;

    public async Task<PluginCatalog> GetAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null) return _cache;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache is not null) return _cache;
            var config = configProvider.Get() ?? new VerzConfig();
            _cache = await loader.LoadAsync(config, cancellationToken);
            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }
}
