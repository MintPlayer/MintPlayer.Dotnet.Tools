namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Which kinds of member take part in an equivalency comparison.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="MemberTraits"/>: the accessor says what a member <em>is</em>, this
/// says what the comparison <em>wants</em>. Filtering is a bitwise test per member, so an option
/// nobody sets costs nothing.
/// </para>
/// <para>
/// <see cref="NonBrowsable"/> is in <see cref="Default"/>, which is a deliberate divergence from
/// FluentAssertions — it excludes <c>[EditorBrowsable(Never)]</c> members by default. An assertion
/// library silently comparing <em>less</em> than the caller wrote is the failure mode this library
/// takes most seriously (see the vacuity guard), and hiding a member from IntelliSense is a
/// statement about tooling, not about correctness. Call
/// <see cref="EquivalencyOptions{TExpectation}.ExcludingNonBrowsableMembers"/> to get FA's behaviour.
/// </para>
/// </remarks>
[Flags]
public enum MemberInclusion
{
    /// <summary>Compare nothing. Only useful as a starting point for building a set.</summary>
    None = 0,

    /// <summary>Compare properties.</summary>
    Properties = 1,

    /// <summary>Compare fields.</summary>
    Fields = 2,

    /// <summary>Compare <c>internal</c> and <c>protected internal</c> members as well as public ones.</summary>
    Internal = 4,

    /// <summary>Compare members marked <c>[EditorBrowsable(EditorBrowsableState.Never)]</c>.</summary>
    NonBrowsable = 8,

    /// <summary>Compare members that are only reachable through an explicitly implemented interface.</summary>
    ExplicitInterface = 16,

    /// <summary>Public properties and fields, browsable or not. What a comparison does with no options set.</summary>
    Default = Properties | Fields | NonBrowsable,
}
