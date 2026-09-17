using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Holds the source-generated member accessors the equivalency engine and formatter use instead
/// of reflection. The generator emits a [ModuleInitializer] into each consuming assembly that
/// calls <see cref="RegisterAccessors"/> for every type observed in BeEquivalentTo call sites or
/// marked with <see cref="AssertEquivalencyAttribute"/>. Registration is idempotent; the last
/// registration for a type wins.
/// </summary>
public static class EquivalencyRegistry
{
    // MemberAccessor[] throughout, never IReadOnlyList<MemberAccessor>. The array flows straight
    // from here into the walker's foreach, and the interface form boxes a struct enumerator on
    // every member lookup — 5.46 KB per comparison, 27% of the walk. See IMemberProvider.GetMembers
    // for the measurement and for why an indexed loop over the interface is not an alternative.
    private static readonly ConcurrentDictionary<Type, MemberAccessor[]> accessors = new();

    /// <summary>Registers the generated accessors for <paramref name="type"/>. Called from a [ModuleInitializer].</summary>
    /// <remarks>The parameter is an array on purpose; see the note on the field above.</remarks>
    public static void RegisterAccessors(Type type, MemberAccessor[] members)
        => accessors[type] = members;

    /// <summary>The generated accessors for <paramref name="type"/>, if the generator emitted any.</summary>
    /// <remarks>
    /// A false return is the silent slow path: the caller falls back to reflection, gets correct
    /// results roughly 15× slower, and nothing reports it. <c>GeneratedAccessorInvariantTests</c>
    /// and the benchmark's own fairness check exist to notice when that happens.
    /// </remarks>
    public static bool TryGetAccessors(Type type, [NotNullWhen(true)] out MemberAccessor[]? members)
        => accessors.TryGetValue(type, out members);
}
