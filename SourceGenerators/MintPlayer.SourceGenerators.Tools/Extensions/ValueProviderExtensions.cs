using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools.Extensions;

public static class ValueProviderExtensions
{
    /// <summary>
    /// Only emits the value if the provider is not disabled through the editorconfig.
    /// </summary>
    /// <param name="settingsProvider">Pass in the settingsProvider from the <see cref="IncrementalGenerator"/> here</param>
    public static IncrementalValuesProvider<T?> WhereEnabled<T>(this IncrementalValuesProvider<T?> provider, IncrementalValueProvider<Settings> settingsProvider)
    {
        return provider
            .Combine(settingsProvider)
            .Where(static (p) => !p.Right.Disable)
            .Select(static (p, ct) => p.Left);
    }
}
