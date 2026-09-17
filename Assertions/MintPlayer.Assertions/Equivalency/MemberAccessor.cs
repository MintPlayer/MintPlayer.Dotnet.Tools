namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// A reflection-free description of one readable member of a type: its name, declared type, a
/// typed getter delegate and the compile-time <see cref="MemberTraits"/> the options are answered
/// from. Produced by the MintPlayer.Assertions source generator (or, as a fallback, built from
/// reflection) and consumed by the equivalency engine and the formatter.
/// </summary>
public sealed class MemberAccessor
{
    /// <summary>
    /// Creates an accessor for a public, browsable member. Kept for source compatibility with
    /// generated code emitted before <see cref="MemberTraits"/> existed.
    /// </summary>
    public MemberAccessor(string name, Type type, Func<object, object?> getter, bool isProperty = true)
        : this(name, type, getter, isProperty ? MemberTraits.Property : MemberTraits.Field)
    {
    }

    /// <summary>Creates an accessor with explicit traits.</summary>
    public MemberAccessor(string name, Type type, Func<object, object?> getter, MemberTraits traits)
    {
        Name = name;
        Type = type;
        Getter = getter;
        Traits = traits;
    }

    /// <summary>The member's name.</summary>
    public string Name { get; }

    /// <summary>The member's declared type.</summary>
    public Type Type { get; }

    /// <summary>Reads the member from an instance of the declaring type.</summary>
    public Func<object, object?> Getter { get; }

    /// <summary>What the generator (or reflection) knows about this member at registration time.</summary>
    public MemberTraits Traits { get; }

    /// <summary>True for a property, false for a field.</summary>
    public bool IsProperty => (Traits & MemberTraits.Property) != 0;
}
