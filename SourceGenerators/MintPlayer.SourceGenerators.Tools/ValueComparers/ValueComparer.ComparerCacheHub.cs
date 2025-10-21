using Microsoft.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

// Per-Compilation hub that every generator in this DLL can use.
internal static class ComparerCacheHub
{
    private static readonly ConditionalWeakTable<Compilation, PerCompilationCache> _cwt = new();

    public static ICompilationCache Get(Compilation compilation) =>
        _cwt.GetValue(compilation, static _ => new PerCompilationCache());
}