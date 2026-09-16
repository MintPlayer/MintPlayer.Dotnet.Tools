using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Equivalency;

/// <summary>How enum members are compared.</summary>
public enum EnumComparison
{
    /// <summary>By underlying value, via <see cref="object.Equals(object?, object?)"/>. The default.</summary>
    ByValue,

    /// <summary>By name, which lets two unrelated enum types with matching names compare equal.</summary>
    ByName,
}

/// <summary>How <c>record</c> types are compared.</summary>
public enum RecordComparison
{
    /// <summary>Member by member, like any other structural node. The default.</summary>
    ByMembers,

    /// <summary>Via the compiler-generated <see cref="object.Equals(object?)"/>, as a value.</summary>
    ByValue,
}

/// <summary>What happens when the walk meets a reference pair it is already comparing higher up.</summary>
public enum CyclicReferenceHandling
{
    /// <summary>Treat the pair as equal and stop descending. The default, and what terminates a cyclic graph.</summary>
    Ignore,

    /// <summary>Report the cycle as a difference, for callers who expect an acyclic graph.</summary>
    ThrowException,
}

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

    /// <summary>Member names excluded wherever they appear, regardless of declaring type or depth.</summary>
    IReadOnlyCollection<string> ExcludedMemberNames { get; }

    /// <summary>Declared member types excluded wherever they appear (e.g. every <c>DateTime</c> member).</summary>
    IReadOnlyCollection<Type> ExcludedMemberTypes { get; }

    /// <summary>
    /// When non-empty, only members whose path matches one of these patterns are compared. A pattern
    /// may name a nested path, and a prefix of a pattern stays included so the walk can reach it.
    /// </summary>
    IReadOnlyCollection<string> IncludedPaths { get; }

    /// <summary>Expectation member name to subject member name, for members that were renamed.</summary>
    IReadOnlyDictionary<string, string> MemberNameMappings { get; }

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

    /// <summary>Which kinds of member take part in the comparison.</summary>
    MemberInclusion Inclusion { get; }

    /// <summary>True to compare collections pairwise in order; false (default) matches items unordered.</summary>
    bool UseStrictOrdering { get; }

    /// <summary>Wildcard paths whose collections are compared in order even when the default is unordered.</summary>
    IReadOnlyCollection<string> StrictOrderingPaths { get; }

    /// <summary>Wildcard paths whose collections are matched unordered even when the default is strict.</summary>
    IReadOnlyCollection<string> LooseOrderingPaths { get; }

    /// <summary>Maximum object-graph descent depth; exceeding it is reported as a difference.</summary>
    int MaxDepth { get; }

    /// <summary>True to resolve expectation members from runtime types instead of declared types.</summary>
    bool UseRuntimeTypes { get; }

    /// <summary>
    /// True to pass silently when the expectation has a member the subject does not, instead of
    /// reporting it as a difference.
    /// </summary>
    bool IgnoreMissingMembers { get; }

    /// <summary>How enum members are compared.</summary>
    EnumComparison EnumComparison { get; }

    /// <summary>How <c>record</c> types are compared.</summary>
    RecordComparison RecordComparison { get; }

    /// <summary>Casing/whitespace/newline handling for string comparisons anywhere in the graph.</summary>
    StringMatchOptions StringOptions { get; }

    /// <summary>True to treat a null string and an empty string as equal.</summary>
    bool TreatNullAsEmptyString { get; }

    /// <summary>What happens when the walk meets a reference pair it is already comparing.</summary>
    CyclicReferenceHandling CyclicReferenceHandling { get; }

    /// <summary>
    /// True to permit a comparison in which some node compares no members at all — an assertion
    /// that cannot fail. Rejected by default, because it is nearly always a mistake.
    /// </summary>
    bool AllowVacuousComparison { get; }
}
