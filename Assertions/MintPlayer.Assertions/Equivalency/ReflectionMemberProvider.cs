using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Reflection-based member resolution: readable non-indexer instance properties plus instance
/// fields, cached per type. This is the only place in the equivalency engine that touches
/// reflection; it is the fallback behind the source-generated accessors in
/// <see cref="EquivalencyRegistry"/>, mirroring the strategy of <see cref="Formatting.Formatter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Public and internal members are both collected, and explicit interface implementations too, each
/// tagged with its <see cref="MemberTraits"/>. Whether any of them takes part in a comparison is the
/// options' decision, not this provider's — collecting them unconditionally is what makes the
/// options work identically for a reflected type and a source-generated one.
/// </para>
/// <para>
/// <c>private</c> and <c>protected</c> members are deliberately NOT collected, even though
/// reflection could reach them. The source generator cannot emit an accessor for one, so including
/// them here would make the same option mean two different things depending on whether a type
/// happened to be scanned.
/// </para>
/// </remarks>
internal sealed class ReflectionMemberProvider : IMemberProvider
{
    private readonly ConcurrentDictionary<Type, IReadOnlyList<MemberAccessor>> cache = new();

    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public IReadOnlyList<MemberAccessor> GetMembers(Type type)
        => cache.GetOrAdd(type, static t => BuildMembers(t));

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Fallback only: types that take part in equivalency comparisons are registered by the source generator in EquivalencyRegistry, which is trim-safe and consulted first. When reflection is reached under trimming, missing members merely reduce comparison coverage, matching the Formatter's best-effort approach.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Same fallback path; see above.")]
    private static IReadOnlyList<MemberAccessor> BuildMembers(Type type)
    {
        try
        {
            var members = new List<MemberAccessor>();
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in type.GetProperties(Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
                var getter = property.GetMethod;
                if (getter is null) continue;
                if (!TryVisibility(getter.IsPublic, getter.IsAssembly || getter.IsFamilyOrAssembly, out var traits)) continue;

                if (!names.Add(property.Name)) continue;
                members.Add(new(property.Name, property.PropertyType, property.GetValue, traits | Browsability(property)));
            }

            foreach (var field in type.GetFields(Instance))
            {
                if (field.IsStatic) continue;
                // Compiler-generated backing fields would duplicate the property they back, under a
                // name nobody wrote. They are never part of what "equivalent" means.
                if (field.Name.Contains('<')) continue;
                if (!TryVisibility(field.IsPublic, field.IsAssembly || field.IsFamilyOrAssembly, out var traits)) continue;

                if (!names.Add(field.Name)) continue;
                members.Add(new(field.Name, field.FieldType, field.GetValue, traits | MemberTraits.Field | Browsability(field)));
            }

            AddExplicitInterfaceMembers(type, members, names);

            return members;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Maps a member's visibility onto traits, rejecting the ones the source generator could never
    /// emit so the two providers agree.
    /// </summary>
    private static bool TryVisibility(bool isPublic, bool isAssemblyVisible, out MemberTraits traits)
    {
        if (isPublic) { traits = MemberTraits.None; return true; }
        if (isAssemblyVisible) { traits = MemberTraits.NonPublic; return true; }
        traits = MemberTraits.None;
        return false;
    }

    private static MemberTraits Browsability(MemberInfo member)
        => member.GetCustomAttribute<EditorBrowsableAttribute>() is { State: EditorBrowsableState.Never }
            ? MemberTraits.NonBrowsable
            : MemberTraits.None;

    /// <summary>
    /// Adds properties that are only reachable through an interface.
    /// </summary>
    /// <remarks>
    /// Read through the interface's own <see cref="PropertyInfo"/> rather than through
    /// <c>GetInterfaceMap</c>: the interface getter dispatches to the explicit implementation
    /// anyway, and the map is both slower and one more thing for a trimmer to lose. A name already
    /// claimed by a public or internal member wins, which is what makes this "explicit only" — an
    /// implicitly-implemented interface property has already been collected above.
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Reflection fallback; see BuildMembers.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Reflection fallback; see BuildMembers. An interface whose properties were trimmed simply contributes none, and explicit interface members are excluded from comparison by default anyway.")]
    private static void AddExplicitInterfaceMembers(Type type, List<MemberAccessor> members, HashSet<string> names)
    {
        foreach (var contract in type.GetInterfaces())
        {
            foreach (var property in contract.GetProperties())
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
                if (!names.Add(property.Name)) continue;
                members.Add(new(property.Name, property.PropertyType, property.GetValue,
                    MemberTraits.ExplicitInterface | Browsability(property)));
            }
        }
    }
}
