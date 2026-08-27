namespace MintPlayer.Assertions.SourceGenerator.Models;

/// <summary>A type for which reflection-free equivalency accessors must be registered.</summary>
public sealed class EquivalencyTypeDeclaration : IEquatable<EquivalencyTypeDeclaration>
{
    public EquivalencyTypeDeclaration(string typeFullName, EquatableArray<MemberDeclaration> members)
    {
        TypeFullName = typeFullName;
        Members = members;
    }

    /// <summary>Fully qualified (global::) name of the type.</summary>
    public string TypeFullName { get; }

    public EquatableArray<MemberDeclaration> Members { get; }

    public bool Equals(EquivalencyTypeDeclaration? other)
        => other is not null && TypeFullName == other.TypeFullName && Members.Equals(other.Members);

    public override bool Equals(object? obj) => Equals(obj as EquivalencyTypeDeclaration);

    public override int GetHashCode()
    {
        unchecked { return TypeFullName.GetHashCode() * 31 + Members.GetHashCode(); }
    }

    public override string ToString() => TypeFullName;
}
