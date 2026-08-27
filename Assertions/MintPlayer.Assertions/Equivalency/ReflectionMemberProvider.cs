using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Reflection-based member resolution: public readable non-indexer instance properties plus
/// public instance fields, cached per type. This is the only place in the equivalency engine that
/// touches reflection; it is the fallback behind the source-generated accessors in
/// <see cref="EquivalencyRegistry"/>, mirroring the strategy of
/// <see cref="Formatting.Formatter"/>.
/// </summary>
internal sealed class ReflectionMemberProvider : IMemberProvider
{
    private readonly ConcurrentDictionary<Type, IReadOnlyList<MemberAccessor>> cache = new();

    public IReadOnlyList<MemberAccessor> GetMembers(Type type)
        => cache.GetOrAdd(type, static t => BuildMembers(t));

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Fallback only: types that take part in equivalency comparisons are registered by the source generator in EquivalencyRegistry, which is trim-safe and consulted first. When reflection is reached under trimming, missing members merely reduce comparison coverage, matching the Formatter's best-effort approach.")]
    private static IReadOnlyList<MemberAccessor> BuildMembers(Type type)
    {
        try
        {
            var members = new List<MemberAccessor>();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
                members.Add(new(property.Name, property.PropertyType, property.GetValue, isProperty: true));
            }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                members.Add(new(field.Name, field.FieldType, field.GetValue, isProperty: false));
            }
            return members;
        }
        catch
        {
            return [];
        }
    }
}
