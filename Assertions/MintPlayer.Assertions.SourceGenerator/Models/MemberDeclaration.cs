namespace MintPlayer.Assertions.SourceGenerator.Models;

/// <summary>
/// What kind of member an accessor stands for. Mirrors
/// <c>MintPlayer.Assertions.Equivalency.MemberTraits</c> value for value; the emitter writes the
/// runtime enum, so the two must not drift.
/// </summary>
[Flags]
public enum MemberTraitsValue
{
    None = 0,
    Field = 1,
    NonPublic = 2,
    NonBrowsable = 4,
    ExplicitInterface = 8,
}

/// <summary>One readable member of a type, reduced to the strings the emitter needs.</summary>
public sealed class MemberDeclaration : IEquatable<MemberDeclaration>
{
    public MemberDeclaration(string name, string typeFullName, MemberTraitsValue traits, string? declaringInterfaceFullName = null)
    {
        Name = name;
        TypeFullName = typeFullName;
        Traits = traits;
        DeclaringInterfaceFullName = declaringInterfaceFullName;
    }

    public string Name { get; }

    /// <summary>Fully qualified (global::) declared type of the member.</summary>
    public string TypeFullName { get; }

    public MemberTraitsValue Traits { get; }

    /// <summary>
    /// For an explicit interface implementation, the interface the getter must cast through; null
    /// otherwise. Without it the emitted accessor would not compile — an explicit implementation is
    /// unreachable through the concrete type by design.
    /// </summary>
    public string? DeclaringInterfaceFullName { get; }

    public bool IsProperty => (Traits & MemberTraitsValue.Field) == 0;

    public bool Equals(MemberDeclaration? other)
        => other is not null && Name == other.Name && TypeFullName == other.TypeFullName
            && Traits == other.Traits && DeclaringInterfaceFullName == other.DeclaringInterfaceFullName;

    public override bool Equals(object? obj) => Equals(obj as MemberDeclaration);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Name.GetHashCode();
            hash = hash * 31 + TypeFullName.GetHashCode();
            hash = hash * 31 + (int)Traits;
            hash = hash * 31 + (DeclaringInterfaceFullName?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public override string ToString() => $"{TypeFullName} {Name}";
}
