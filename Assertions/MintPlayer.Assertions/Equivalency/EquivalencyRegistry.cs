using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Holds the source-generated member accessors the equivalency engine and formatter use instead
/// of reflection. The generator emits a [ModuleInitializer] into each consuming assembly that
/// calls <see cref="RegisterAccessors(Type, MemberAccessor[])"/> for every type observed in
/// BeEquivalentTo call sites or marked with <see cref="AssertEquivalencyAttribute"/>. Registration
/// is idempotent; the last registration for a type wins.
/// </summary>
public static class EquivalencyRegistry
{
    // MemberAccessor[] throughout, never IReadOnlyList<MemberAccessor>. The array flows straight
    // from here into the walker's foreach, and the interface form boxes a struct enumerator on
    // every member lookup — 5.46 KB per comparison, 27% of the walk. See IMemberProvider.GetMembers
    // for the measurement and for why an indexed loop over the interface is not an alternative.
    private static readonly ConcurrentDictionary<Type, MemberAccessor[]> accessors = new();

    /// <summary>
    /// Members that are NOT compared by default — non-public, non-browsable — kept apart from the
    /// default table rather than filtered out of it.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The split is the optimisation; do not merge these back into one table.</b> The default
    /// walk is handed the same array it was handed before traits existed, so it does no filtering,
    /// its member count is unchanged, and <c>FindByName</c> — which is O(members²) per node — does
    /// not grow because a type happens to have internal members nobody asked about. Merging the
    /// tables and masking in <c>CompareMembers</c> would put that cost on every comparison in every
    /// suite to serve options that are off by default.
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, ExtendedAccessors> extended = new();

    /// <summary>Combinations already materialised, so the concatenation happens once per (type, traits).</summary>
    private static readonly ConcurrentDictionary<(Type Type, MemberTraits Wanted), MemberAccessor[]> combinations = new();

    private sealed record ExtendedAccessors(MemberAccessor[] Members, bool IsComplete);

    /// <summary>Registers the generated accessors for <paramref name="type"/>. Called from a [ModuleInitializer].</summary>
    /// <remarks>The parameter is an array on purpose; see the note on the field above.</remarks>
    public static void RegisterAccessors(Type type, MemberAccessor[] members)
        => accessors[type] = members;

    /// <summary>
    /// Registers the members of <paramref name="type"/> that are excluded from comparison by
    /// default, each tagged with the <see cref="MemberTraits"/> that excludes it.
    /// </summary>
    /// <param name="type">The type these accessors belong to.</param>
    /// <param name="members">The non-default members the generator could emit an accessor for.</param>
    /// <param name="isComplete">
    /// False when the generator had to skip a member it could not reference from generated code —
    /// an <c>internal</c> member of a type in another assembly, with no <c>InternalsVisibleTo</c>.
    /// </param>
    /// <remarks>
    /// ⚠️ <paramref name="isComplete"/> is not bookkeeping. A partial table would make an option
    /// like <c>IncludingInternalMembers</c> compare a DIFFERENT set of members depending on which
    /// assembly the type came from — a discrepancy that shows up only in someone else's build. When
    /// it is false, <see cref="TryGetAccessors(Type, MemberTraits, out MemberAccessor[])"/> refuses
    /// the type for any request that wants those traits, and the caller falls back to reflection,
    /// which can always see them. Slower, and right.
    /// </remarks>
    public static void RegisterExtendedAccessors(Type type, MemberAccessor[] members, bool isComplete)
        => extended[type] = new(members, isComplete);

    /// <summary>The generated accessors for <paramref name="type"/>, if the generator emitted any.</summary>
    /// <remarks>
    /// A false return is the silent slow path: the caller falls back to reflection, gets correct
    /// results roughly 15× slower, and nothing reports it. <c>GeneratedAccessorInvariantTests</c>
    /// and the benchmark's own fairness check exist to notice when that happens.
    /// </remarks>
    public static bool TryGetAccessors(Type type, [NotNullWhen(true)] out MemberAccessor[]? members)
        => accessors.TryGetValue(type, out members);

    /// <summary>
    /// The generated accessors for <paramref name="type"/> including any member whose traits are
    /// covered by <paramref name="wanted"/>. Returns false when the generated table cannot serve
    /// the request, in which case the caller must use reflection rather than compare fewer members.
    /// </summary>
    public static bool TryGetAccessors(Type type, MemberTraits wanted, [NotNullWhen(true)] out MemberAccessor[]? members)
    {
        if (wanted == MemberTraits.None) return TryGetAccessors(type, out members);

        if (!accessors.TryGetValue(type, out var defaults))
        {
            members = null;
            return false;
        }

        // Nothing was registered as excluded, so there is nothing extra to hand back and the
        // default table is already the complete answer.
        if (!extended.TryGetValue(type, out var extras))
        {
            members = defaults;
            return true;
        }

        if (!extras.IsComplete)
        {
            members = null;
            return false;
        }

        // ⚠️ The factory is STATIC and takes its inputs through the state argument. It must stay that
        // way, and this is not style.
        //
        // A lambda that captures a local makes the compiler allocate a display class for the whole
        // enclosing method scope — at method ENTRY, before any early return. This method returns on
        // its first line for the default case, and it is called twice per structural node, so a
        // capturing lambda down here cost 2,760 B/op on the benchmark graph: 24 bytes for a closure
        // that was never used, on every member lookup in the library. Measured 13,976 → 16,744 B/op,
        // and the walker's node and lookup counts were identical before and after, which is exactly
        // why an operation-count gate could not see it and the allocation gate could.
        //
        // The same trap applies to any helper added below the fast path in a hot method: put the
        // closure in its own method, or pass state to a `static` lambda as here.
        members = combinations.GetOrAdd((type, wanted), static (key, state) => Combine(key.Wanted, state.Defaults, state.Extras),
            (Defaults: defaults, Extras: extras.Members));
        return true;
    }

    private static MemberAccessor[] Combine(MemberTraits wanted, MemberAccessor[] defaults, MemberAccessor[] extras)
    {
        var matched = 0;
        foreach (var extra in extras)
        {
            if (IsWanted(extra.Traits, wanted)) matched++;
        }

        if (matched == 0) return defaults;

        var combined = new MemberAccessor[defaults.Length + matched];
        Array.Copy(defaults, combined, defaults.Length);
        var next = defaults.Length;
        foreach (var extra in extras)
        {
            if (IsWanted(extra.Traits, wanted)) combined[next++] = extra;
        }
        return combined;
    }

    /// <summary>
    /// True when every reason this member is excluded by default has been asked for. A member that
    /// is both non-public AND non-browsable needs both bits, not either.
    /// </summary>
    private static bool IsWanted(MemberTraits traits, MemberTraits wanted)
        => (traits & ExcludedByDefault & ~wanted) == MemberTraits.None;

    /// <summary>The traits that keep a member out of the default table.</summary>
    internal const MemberTraits ExcludedByDefault =
        MemberTraits.NonPublic | MemberTraits.NonBrowsable | MemberTraits.ExplicitInterface;
}
