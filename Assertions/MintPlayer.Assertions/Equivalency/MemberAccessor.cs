namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// What kind of member an accessor stands for, so the equivalency options can filter on it without
/// the engine ever touching reflection.
/// </summary>
/// <remarks>
/// The scanner already knew every one of these at generation time and threw them away — it emitted
/// only public properties and fields, so "compare internal members too" was not expressible at all.
/// Carrying them as flags keeps the filtering a bitwise test on the passing path.
/// </remarks>
[Flags]
public enum MemberTraits
{
    /// <summary>A public, browsable, implicitly-implemented property. The common case.</summary>
    None = 0,

    /// <summary>A field rather than a property.</summary>
    Field = 1,

    /// <summary>
    /// Declared <c>internal</c> (or <c>protected internal</c>) rather than public.
    /// </summary>
    /// <remarks>
    /// Never <c>private</c> or <c>protected</c>: generated code cannot reach those, so including
    /// them would give a different answer for a source-generated type than for a reflected one. See
    /// <see cref="EquivalencyOptions{TExpectation}.IncludingInternalMembers"/>.
    /// </remarks>
    NonPublic = 2,

    /// <summary>Marked <c>[EditorBrowsable(EditorBrowsableState.Never)]</c>.</summary>
    NonBrowsable = 4,

    /// <summary>An explicit interface implementation, reachable only through the interface.</summary>
    ExplicitInterface = 8,
}

/// <summary>
/// A reflection-free description of one readable member of a type: its name, declared type, a typed
/// getter delegate and the traits the equivalency options filter on. Produced by the
/// MintPlayer.Assertions source generator (or, as a fallback, built from reflection) and consumed by
/// the equivalency engine and the formatter.
/// </summary>
public sealed class MemberAccessor
{
    /// <summary>Describes one readable member.</summary>
    public MemberAccessor(string name, Type type, Func<object, object?> getter, MemberTraits traits = MemberTraits.None)
    {
        Name = name;
        Type = type;
        Getter = getter;
        Traits = traits;
    }

    /// <summary>The member's name. For an explicit interface implementation, the interface member's name.</summary>
    public string Name { get; }

    /// <summary>The member's declared type.</summary>
    public Type Type { get; }

    /// <summary>Reads the member from an instance.</summary>
    public Func<object, object?> Getter { get; }

    /// <summary>What kind of member this is; see <see cref="MemberTraits"/>.</summary>
    public MemberTraits Traits { get; }

    /// <summary>True for a property, false for a field.</summary>
    public bool IsProperty => (Traits & MemberTraits.Field) == 0;
}
