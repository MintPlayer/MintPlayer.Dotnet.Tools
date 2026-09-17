namespace MintPlayer.Assertions.SourceGenerator.Models;

/// <summary>
/// The compile-time facts about a member that the emitter turns into a
/// <c>MintPlayer.Assertions.Equivalency.MemberTraits</c> value.
/// </summary>
/// <remarks>
/// ⚠️ <b>This mirrors the runtime enum by value, and nothing checks that it still does.</b> The
/// generator targets netstandard2.0 and cannot reference the assertions assembly, so the bits are
/// repeated here rather than shared. Changing either side means changing both;
/// <c>MemberTraitParityTests</c> in the assertions test project is what actually catches a drift,
/// because a mismatch produces perfectly valid code that compares the wrong members.
/// </remarks>
[Flags]
public enum MemberTraitFlags
{
    None = 0,
    Property = 1 << 0,
    Field = 1 << 1,
    NonPublic = 1 << 2,
    NonBrowsable = 1 << 3,
    ExplicitInterface = 1 << 4,
}

/// <summary>One readable member of a type, reduced to the strings the emitter needs.</summary>
public sealed class MemberDeclaration : IEquatable<MemberDeclaration>
{
    public MemberDeclaration(string name, string typeFullName, MemberTraitFlags traits)
    {
        Name = name;
        TypeFullName = typeFullName;
        Traits = traits;
    }

    public string Name { get; }

    /// <summary>Fully qualified (global::) declared type of the member.</summary>
    public string TypeFullName { get; }

    public MemberTraitFlags Traits { get; }

    public bool IsProperty => (Traits & MemberTraitFlags.Property) != 0;

    /// <summary>True when this member is excluded from comparison unless an option asks for it.</summary>
    public bool IsExcludedByDefault
        => (Traits & (MemberTraitFlags.NonPublic | MemberTraitFlags.NonBrowsable | MemberTraitFlags.ExplicitInterface)) != 0;

    public bool Equals(MemberDeclaration? other)
        => other is not null && Name == other.Name && TypeFullName == other.TypeFullName && Traits == other.Traits;

    public override bool Equals(object? obj) => Equals(obj as MemberDeclaration);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Name.GetHashCode();
            hash = hash * 31 + TypeFullName.GetHashCode();
            hash = hash * 31 + (int)Traits;
            return hash;
        }
    }

    public override string ToString() => $"{TypeFullName} {Name}";
}
