using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor BoxedEnumeratorRule = new DiagnosticDescriptor(
        id: "MPA0005",
        title: "foreach over an interface-typed indexable collection boxes its enumerator",
        messageFormat: "'{0}' is typed as the interface '{1}', so foreach boxes its enumerator — one heap allocation per call, on the passing path. Index it instead: for (var i = 0; i < {0}.Count; i++).",
        category: "MintPlayer.Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Iterating a variable whose static type is IReadOnlyList<T> or IList<T> goes through the " +
            "interface, which boxes the underlying struct enumerator. The source looks identical to " +
            "an allocation-free loop, so neither review nor reading the code will catch it. Because " +
            "the collection is indexable, an indexed loop is a free fix. " +
            "Only reported inside MintPlayer.Assertions itself, where assertions run on a hot path " +
            "and the library's stated boundary is that a passing assertion allocates nothing.");
}
