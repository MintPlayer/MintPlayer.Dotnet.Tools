using MintPlayer.SourceGenerators.Tools.Models;
using System.Runtime.CompilerServices;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

internal static class ValueComparer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Register built-in comparers
        ComparerRegistry.TryRegister(typeof(AnalyzerInfo), new AnalyzerInfoComparer());
    }
}
