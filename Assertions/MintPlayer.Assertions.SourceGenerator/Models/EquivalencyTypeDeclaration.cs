using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.Assertions.SourceGenerator.Models;

/// <summary>A type for which reflection-free equivalency accessors must be registered.</summary>
public sealed class EquivalencyTypeDeclaration : IEquatable<EquivalencyTypeDeclaration>
{
    public EquivalencyTypeDeclaration(string typeFullName, EquatableArray<MemberDeclaration> members, bool extendedMembersComplete = true)
    {
        TypeFullName = typeFullName;
        Members = members;
        ExtendedMembersComplete = extendedMembersComplete;
    }

    /// <summary>Fully qualified (global::) name of the type.</summary>
    public string TypeFullName { get; }

    public EquatableArray<MemberDeclaration> Members { get; }

    /// <summary>
    /// False when a member excluded by default was skipped because generated code cannot reference
    /// it — an <c>internal</c> member of a type in another assembly with no
    /// <c>InternalsVisibleTo</c>. The runtime then refuses the generated table for any option that
    /// wants those members and falls back to reflection, rather than silently comparing fewer of
    /// them than the same option would compare for a type in this assembly.
    /// </summary>
    public bool ExtendedMembersComplete { get; }

    public bool Equals(EquivalencyTypeDeclaration? other)
        => other is not null && TypeFullName == other.TypeFullName && Members.Equals(other.Members)
            && ExtendedMembersComplete == other.ExtendedMembersComplete;

    public override bool Equals(object? obj) => Equals(obj as EquivalencyTypeDeclaration);

    public override int GetHashCode()
    {
        unchecked { return (TypeFullName.GetHashCode() * 31 + Members.GetHashCode()) * 31 + (ExtendedMembersComplete ? 1 : 0); }
    }

    public override string ToString() => TypeFullName;
}
