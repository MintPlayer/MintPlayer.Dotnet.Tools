using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.Verz.Helpers;

internal sealed partial class ToolCatalog
{
    [Inject] private readonly VerzConfig verzConfig;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private ToolCatalogResult? cache;

    public async Task<ToolCatalogResult> GetToolsetAsync(CancellationToken cancellationToken)
    {
        if (cache is not null)
        {
            return cache;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (cache is null)
            {
                var (registries, sdks) = await Program.LoadToolsAsync(verzConfig.Tools ?? [], cancellationToken);
                cache = new ToolCatalogResult(registries, sdks);
            }
        }
        finally
        {
            initializationLock.Release();
        }

        return cache!;
    }
}