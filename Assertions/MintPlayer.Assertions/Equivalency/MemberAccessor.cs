namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// A reflection-free description of one readable member of a type: its name, declared type and a
/// typed getter delegate. Produced by the MintPlayer.Assertions source generator (or, as a
/// fallback, built from reflection) and consumed by the equivalency engine and the formatter.
/// </summary>
public sealed class MemberAccessor
{
    public MemberAccessor(string name, Type type, Func<object, object?> getter, bool isProperty = true)
    {
        Name = name;
        Type = type;
        Getter = getter;
        IsProperty = isProperty;
    }

    public string Name { get; }
    public Type Type { get; }
    public Func<object, object?> Getter { get; }

    /// <summary>True for a property, false for a field.</summary>
    public bool IsProperty { get; }
}
