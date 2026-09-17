namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Compile-time facts about a member, emitted by the source generator onto every
/// <see cref="MemberAccessor"/> so the equivalency options can be answered with a bitwise test
/// instead of reflection.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This enum is the contract between the generator and the reflection fallback, and the two
/// must agree exactly.</b> A trait the generator emits and <see cref="ReflectionMemberProvider"/>
/// does not — or the reverse — means the same option silently compares different members depending
/// on whether a type happened to be scanned. That is worse than not having the option: it is a
/// difference that appears only in someone else's assembly. Any change here is a change to both
/// providers, and <c>MemberTraitParityTests</c> is what holds them together.
/// </para>
/// <para>
/// ⚠️ <b>Traits are not a filter applied per node.</b> Members whose traits are off by default live
/// in a second table that is only ever handed out when an option asks for them, so the default walk
/// sees an array of exactly the members it saw before and does no filtering at all. Do not "simplify"
/// this into one table plus a mask test in <c>CompareMembers</c>: that puts a test on the hottest
/// loop in the library to serve options almost nobody enables. See
/// <see cref="EquivalencyRegistry.TryGetAccessors(Type, MemberTraits, out MemberAccessor[])"/>.
/// </para>
/// </remarks>
[Flags]
public enum MemberTraits
{
    /// <summary>No traits: a public, browsable, directly-declared member.</summary>
    None = 0,

    /// <summary>The member is a property. Mutually exclusive with <see cref="Field"/>.</summary>
    Property = 1 << 0,

    /// <summary>The member is a field. Mutually exclusive with <see cref="Property"/>.</summary>
    Field = 1 << 1,

    /// <summary>
    /// The member is <c>internal</c>, <c>protected</c> or <c>protected internal</c>.
    /// </summary>
    /// <remarks>
    /// <c>private</c> members are not represented at all — neither provider returns them, matching
    /// what other assertion libraries compare. Do not widen this to mean "not public" without
    /// changing both providers together: the generator physically cannot emit an accessor for a
    /// private member, so the reflection fallback would start comparing members the generated path
    /// never could.
    /// </remarks>
    NonPublic = 1 << 2,

    /// <summary>The member carries <c>[EditorBrowsable(EditorBrowsableState.Never)]</c>.</summary>
    NonBrowsable = 1 << 3,

    /// <summary>
    /// Reserved for explicit interface implementations. <b>Nothing emits this yet</b>, and that is
    /// deliberate: an explicit implementation is reachable only through a cast to the interface, so
    /// the generator would have to emit a different accessor shape for it. The bit is defined now so
    /// the layout is fixed, and so the two providers stay in agreement by both returning nothing.
    /// </summary>
    ExplicitInterface = 1 << 4,
}
