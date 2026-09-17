namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Which members a comparison wants: the normally-excluded traits it asked for, and the member
/// kinds it asked to leave out.
/// </summary>
/// <remarks>
/// <para>
/// The two halves pull in opposite directions and are kept apart for that reason.
/// <see cref="Wanted"/> <b>adds</b> members that are off by default (non-public, non-browsable);
/// <see cref="ExcludedKinds"/> <b>removes</b> members that are on by default
/// (<c>ExcludingFields</c>, <c>ExcludingProperties</c>). Folding them into one mask makes every
/// call site guess which direction a bit means.
/// </para>
/// <para>
/// ⚠️ <b>A dictionary key, so it implements <see cref="IEquatable{T}"/> by hand — and that is not
/// ceremony.</b> A struct without it falls back to <c>ObjectEqualityComparer</c>, which BOXES on
/// every lookup: measured as <b>96 B/op</b> on a comparison using <c>ExcludingFields</c>, on what is
/// supposed to be a cached fast path. The compiler's structural equality is correct but not free,
/// and nothing warns about the difference. If a field is ever added here, add it to
/// <see cref="Equals(MemberSelection)"/> and <see cref="GetHashCode"/> too.
/// </para>
/// <para>
/// ⚠️ <b><see cref="Default"/> must remain the zero value.</b> Both providers short-circuit on
/// <see cref="IsDefault"/> to hand back the exact array they held before selections existed — no
/// filtering, no copy, no extra members for <c>FindByName</c> to scan past. That fast path is the
/// only reason options like these cost a passing suite nothing, and a non-zero default would route
/// every comparison in the library through the slow branch.
/// </para>
/// </remarks>
internal readonly struct MemberSelection(MemberTraits wanted, MemberTraits excludedKinds)
    : IEquatable<MemberSelection>
{
    /// <summary>Everything on by default, nothing more: what essentially every comparison uses.</summary>
    public static MemberSelection Default => default;

    /// <summary>Normally-excluded traits this comparison asked to include.</summary>
    public MemberTraits Wanted { get; } = wanted;

    /// <summary>
    /// <see cref="MemberTraits.Property"/> and/or <see cref="MemberTraits.Field"/> to leave out.
    /// </summary>
    public MemberTraits ExcludedKinds { get; } = excludedKinds;

    public bool IsDefault => Wanted == MemberTraits.None && ExcludedKinds == MemberTraits.None;

    /// <summary>Whether a member with these traits takes part in the comparison.</summary>
    /// <remarks>
    /// A member excluded by default is admitted only when <em>every</em> reason it is excluded has
    /// been asked for — one that is both non-public and non-browsable needs both bits, not either.
    /// </remarks>
    public bool Admits(MemberTraits traits)
    {
        if ((traits & ExcludedKinds) != MemberTraits.None) return false;
        return (traits & EquivalencyRegistry.ExcludedByDefault & ~Wanted) == MemberTraits.None;
    }

    public bool Equals(MemberSelection other) => Wanted == other.Wanted && ExcludedKinds == other.ExcludedKinds;

    public override bool Equals(object? obj) => obj is MemberSelection other && Equals(other);

    public override int GetHashCode() => ((int)Wanted * 397) ^ (int)ExcludedKinds;
}
