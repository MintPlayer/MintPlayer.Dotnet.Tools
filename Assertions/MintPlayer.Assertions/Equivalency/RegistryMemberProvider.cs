namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// The default member provider: prefers the source-generated accessors registered in
/// <see cref="EquivalencyRegistry"/> (reflection-free, AOT-safe) and falls back to
/// <see cref="ReflectionMemberProvider"/> for unregistered types.
/// </summary>
internal sealed class RegistryMemberProvider : IMemberProvider
{
    /// <summary>The shared instance used by the equivalency engine.</summary>
    public static RegistryMemberProvider Instance { get; } = new();

    private readonly ReflectionMemberProvider fallback = new();

    public IReadOnlyList<MemberAccessor> GetMembers(Type type)
        => EquivalencyRegistry.TryGetAccessors(type, out var accessors)
            ? accessors
            : fallback.GetMembers(type);
}
