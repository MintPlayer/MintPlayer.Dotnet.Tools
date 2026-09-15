using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor BoxedEnumeratorRule = new DiagnosticDescriptor(
        id: "MPA0005",
        title: "foreach over an interface-typed indexable collection boxes its enumerator",
        messageFormat: "'{0}' is typed as the interface '{1}', so foreach boxes its enumerator — one heap allocation per call, on the passing path. Iterate a ReadOnlySpan<T> instead.",
        category: "MintPlayer.Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Iterating a variable whose static type is IReadOnlyList<T> or IList<T> goes through the " +
            "interface, which boxes the underlying struct enumerator. The source looks identical to " +
            "an allocation-free loop, so neither review nor reading the code will catch it. " +
            "\n\n" +
            "Prefer a ReadOnlySpan<T>: get one from an array directly or from a List<T> via " +
            "CollectionsMarshal.AsSpan, and iterate that. A span's enumerator is a ref struct the JIT " +
            "inlines, with no interface dispatch at all. " +
            "\n\n" +
            "Do NOT simply convert to an indexed loop over the interface. That fixes the allocation " +
            "and costs time: every indexer call and every Count read is an interface dispatch the JIT " +
            "cannot inline, and a naive 'for (var i = 0; i < x.Count; i++)' re-reads Count once per " +
            "element. Measured over 100k loops of 8 items, that ran ~1.4x SLOWER than the boxed " +
            "foreach it replaced, while the span ran ~2x faster for a List and ~7x faster for an " +
            "array. If a span is genuinely unavailable, hoist Count out of the loop at minimum. " +
            "\n\n" +
            "Only reported inside MintPlayer.Assertions itself, where assertions run on a hot path " +
            "and the library's stated boundary is that a passing assertion allocates nothing.");
}
