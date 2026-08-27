using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Holds the source-generated member accessors the equivalency engine and formatter use instead
/// of reflection. The generator emits a [ModuleInitializer] into each consuming assembly that
/// calls <see cref="RegisterAccessors"/> for every type observed in BeEquivalentTo call sites or
/// marked with <see cref="AssertEquivalencyAttribute"/>. Registration is idempotent; the last
/// registration for a type wins.
/// </summary>
public static class EquivalencyRegistry
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<MemberAccessor>> accessors = new();

    public static void RegisterAccessors(Type type, IReadOnlyList<MemberAccessor> members)
        => accessors[type] = members;

    public static bool TryGetAccessors(Type type, [NotNullWhen(true)] out IReadOnlyList<MemberAccessor>? members)
        => accessors.TryGetValue(type, out members);
}
