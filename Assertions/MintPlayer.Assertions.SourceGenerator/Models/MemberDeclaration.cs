namespace MintPlayer.Assertions.SourceGenerator.Models;

/// <summary>One readable member of a type, reduced to the strings the emitter needs.</summary>
public sealed class MemberDeclaration : IEquatable<MemberDeclaration>
{
    public MemberDeclaration(string name, string typeFullName, bool isProperty)
    {
        Name = name;
        TypeFullName = typeFullName;
        IsProperty = isProperty;
    }

    public string Name { get; }

    /// <summary>Fully qualified (global::) declared type of the member.</summary>
    public string TypeFullName { get; }

    public bool IsProperty { get; }

    public bool Equals(MemberDeclaration? other)
        => other is not null && Name == other.Name && TypeFullName == other.TypeFullName && IsProperty == other.IsProperty;

    public override bool Equals(object? obj) => Equals(obj as MemberDeclaration);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Name.GetHashCode();
            hash = hash * 31 + TypeFullName.GetHashCode();
            hash = hash * 31 + (IsProperty ? 1 : 0);
            return hash;
        }
    }

    public override string ToString() => $"{TypeFullName} {Name}";
}
