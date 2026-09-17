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

    /// <summary>Generated accessors when the generator saw this type, reflection otherwise.</summary>
    /// <remarks>
    /// <para>
    /// The array return type is deliberate and measured — see <see cref="IMemberProvider.GetMembers"/>.
    /// </para>
    /// <para>
    /// ⚠️ The <c>false</c> branch is the 15× slow path and it is <b>silent</b>: the comparison is
    /// still correct, just reflective. Nothing logs it, nothing counts it. That is why the scanner's
    /// skip list matters so much — an expectation erased to <c>object</c>, a type containing a type
    /// parameter, a file-local or private nested type, or a call shape the generator does not
    /// recognise all land here with no signal.
    /// </para>
    /// </remarks>
    public MemberAccessor[] GetMembers(Type type, MemberTraits wanted)
        => EquivalencyRegistry.TryGetAccessors(type, wanted, out var accessors)
            ? accessors
            : fallback.GetMembers(type, wanted);
}
