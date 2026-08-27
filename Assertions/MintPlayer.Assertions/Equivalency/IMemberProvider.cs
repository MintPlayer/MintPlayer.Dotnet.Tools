namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Resolves the comparable members of a type for the equivalency engine. Implementations decide
/// where the accessors come from (source-generated registry, reflection, ...).
/// </summary>
internal interface IMemberProvider
{
    /// <summary>The readable members of <paramref name="type"/>, in declaration order when known.</summary>
    IReadOnlyList<MemberAccessor> GetMembers(Type type);
}
