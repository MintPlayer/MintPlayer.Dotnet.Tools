namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// The untyped view of <see cref="EquivalencyOptions{TExpectation}"/> the equivalency engine
/// consumes. The generic options class is the fluent authoring surface; this interface is the
/// engine's read-only contract, so the engine never depends on the expectation type.
/// </summary>
public interface IEquivalencyOptions
{
    /// <summary>Exact member paths (dot-separated, e.g. "Address.City") that are skipped entirely.</summary>
    IReadOnlyCollection<string> ExcludedPaths { get; }

    /// <summary>
    /// Member names excluded on any node whose type matches the key (assignability included),
    /// no matter where that node sits in the graph — including inside collections.
    /// </summary>
    IReadOnlyDictionary<Type, IReadOnlyCollection<string>> NestedExclusions { get; }

    /// <summary>Wildcard patterns ('*'/'?') matched against the full difference path of each node.</summary>
    IReadOnlyCollection<string> ExcludedWildcardPaths { get; }

    /// <summary>When non-empty, only these top-level members of the expectation are compared.</summary>
    IReadOnlyCollection<string> IncludedMembers { get; }

    /// <summary>
    /// Custom comparisons keyed by the member's declared type. The action receives
    /// (subject, expectation); an <see cref="AssertionFailedException"/> it throws becomes the
    /// difference text for that path.
    /// </summary>
    IReadOnlyDictionary<Type, Action<object?, object?>> CustomComparers { get; }

    /// <summary>Types forced to compare via <see cref="object.Equals(object?, object?)"/>.</summary>
    IReadOnlyCollection<Type> ComparedByValue { get; }

    /// <summary>Types forced to compare member-by-member even when value-like by default.</summary>
    IReadOnlyCollection<Type> ComparedByMembers { get; }

    /// <summary>True to compare collections pairwise in order; false (default) matches items unordered.</summary>
    bool UseStrictOrdering { get; }

    /// <summary>Maximum object-graph descent depth; nodes deeper than this are treated as equal.</summary>
    int MaxDepth { get; }

    /// <summary>True to resolve expectation members from runtime types instead of declared types.</summary>
    bool UseRuntimeTypes { get; }
}
