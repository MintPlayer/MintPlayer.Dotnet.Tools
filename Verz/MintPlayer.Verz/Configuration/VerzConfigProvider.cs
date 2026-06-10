namespace MintPlayer.Verz.Configuration;

/// <summary>
/// Lazily loads <c>verz.json</c> from the current working directory and
/// caches it for the duration of the invocation. Commands that need both
/// the registries list and the loaded plugins share this single read.
/// </summary>
internal sealed class VerzConfigProvider
{
    private readonly Lazy<VerzConfig?> _config = new(LoadCwd);

    public VerzConfig? Get() => _config.Value;

    public VerzConfig Require() =>
        Get() ?? throw new InvalidOperationException(
            $"verz.json not found in {Directory.GetCurrentDirectory()}. Run `verz init` first.");

    private static VerzConfig? LoadCwd()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "verz.json");
        return File.Exists(path) ? VerzConfigSerializer.Load(path) : null;
    }
}
