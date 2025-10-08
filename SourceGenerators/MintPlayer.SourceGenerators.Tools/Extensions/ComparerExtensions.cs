using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Tools;

public static class ComparerExtensions
{
    /// <summary>
    /// Only use this method on simple types, like bool
    /// </summary>
    public static IncrementalValueProvider<T> WithDefaultComparer<T>(this IncrementalValueProvider<T> provider) where T : struct
    {
        return provider.WithComparer(DefaultValueComparer<T>.Instance);
    }
}
