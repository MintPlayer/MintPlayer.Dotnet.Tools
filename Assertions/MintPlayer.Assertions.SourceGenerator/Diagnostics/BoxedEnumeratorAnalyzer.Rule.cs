using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor BoxedEnumeratorRule = new DiagnosticDescriptor(
        id: "MPA0005",
        title: "foreach over an interface-typed collection boxes its enumerator",
        messageFormat: "'{0}' is typed as '{1}', so this foreach boxes the enumerator — one heap allocation per call. Iterate a ReadOnlySpan<T>, or change the declared type to an array.",
        category: "MintPlayer.Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "foreach over a variable whose STATIC type is a collection interface calls " +
            "IEnumerable<T>.GetEnumerator(), which returns the interface — so the underlying struct " +
            "enumerator is boxed onto the heap, once per loop. The same loop over an array or a " +
            "ReadOnlySpan<T> allocates nothing.\n\n" +
            "This matters here because the code is IDENTICAL either way. Only the declared type " +
            "decides, so neither review nor a code search finds it; it was measured at 5.46 KB per " +
            "equivalency comparison, 27% of the entire walk, hiding behind a foreach that looked " +
            "perfectly ordinary.\n\n" +
            "THE FIX IS A SPAN, NOT AN INDEXED LOOP. Rewriting `foreach (var x in list)` as " +
            "`for (var i = 0; i < list.Count; i++)` over the same interface removes the allocation " +
            "and is SLOWER than the boxed loop it replaces — roughly 1.4x — because every indexer " +
            "call and every Count read is an interface dispatch the JIT cannot inline. Measured " +
            "over 100k loops of 8 items: boxed foreach 22.1ms, indexed-over-interface 30.2ms, " +
            "span 10.6ms.\n\n" +
            "Prefer, in order: change the declared type to T[] (foreach over an array emits no " +
            "enumerator at all); or take a ReadOnlySpan<T> — from the array directly, or " +
            "CollectionsMarshal.AsSpan(list) for a List<T>. When type-testing a sequence to reach a " +
            "span, test the ARRAY case before the List case: an array is not a List<T>, so testing " +
            "only for the list silently drops every T[] onto the copying path, and arrays are what " +
            "test code writes.");
}
