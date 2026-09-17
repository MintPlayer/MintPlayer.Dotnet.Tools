using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace MintPlayer.Assertions.Equivalency;

/// <summary>
/// Reflection-based member resolution: readable non-indexer instance properties plus instance
/// fields, cached per type and per requested <see cref="MemberTraits"/>. This is the only place in
/// the equivalency engine that touches reflection; it is the fallback behind the source-generated
/// accessors in <see cref="EquivalencyRegistry"/>, mirroring the strategy of
/// <see cref="Formatting.Formatter"/>.
/// </summary>
/// <remarks>
/// ⚠️ <b>This type must agree with <c>EquivalencyScanner</c> in the generator about which members
/// exist and what traits they carry.</b> They are two implementations of one rule, in two projects,
/// with no compiler check between them — a member one returns and the other does not makes the same
/// option behave differently depending on whether a type happened to be scanned, which surfaces only
/// in a consumer's build. <c>MemberTraitParityTests</c> compares them on purpose-built types; extend
/// it when this changes.
/// <para>
/// Specifically mirrored here: <c>private</c> and <c>private protected</c> members are NOT returned
/// (the generator physically cannot emit an accessor for one), explicit interface implementations
/// are NOT returned (neither side emits them yet — see <see cref="MemberTraits.ExplicitInterface"/>),
/// and <c>[EditorBrowsable(Never)]</c> is a trait rather than an exclusion.
/// </para>
/// </remarks>
internal sealed class ReflectionMemberProvider : IMemberProvider
{
    private readonly ConcurrentDictionary<(Type Type, MemberSelection Selection), MemberAccessor[]> cache = new();

    public MemberAccessor[] GetMembers(Type type, in MemberSelection selection)
        => cache.GetOrAdd((type, selection), static key => BuildMembers(key.Type, key.Selection));

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Fallback only: types that take part in equivalency comparisons are registered by the source generator in EquivalencyRegistry, which is trim-safe and consulted first. When reflection is reached under trimming, missing members merely reduce comparison coverage, matching the Formatter's best-effort approach.")]
    private static MemberAccessor[] BuildMembers(Type type, MemberSelection selection)
    {
        try
        {
            // Public-only is the overwhelmingly common request, and asking for NonPublic makes the
            // runtime hand back compiler-generated backing fields and a great deal else, so the
            // cheap binding is used unless something actually wants more.
            var binding = BindingFlags.Public | BindingFlags.Instance;
            if ((selection.Wanted & MemberTraits.NonPublic) != 0) binding |= BindingFlags.NonPublic;

            var members = new List<MemberAccessor>();
            foreach (var property in type.GetProperties(binding))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;

                var getter = property.GetMethod;
                if (getter is null) continue;

                // An explicit implementation's name is "Namespace.IFace.Member" and it is private.
                // Neither provider returns these yet; excluding it here rather than tagging it keeps
                // the two sides identical.
                if (property.Name.Contains('.')) continue;

                var traits = TraitsOf(getter.IsPublic, getter.IsPrivate || getter.IsFamilyAndAssembly, MemberTraits.Property, property);
                if (traits is null || !selection.Admits(traits.Value)) continue;

                members.Add(new(property.Name, property.PropertyType, property.GetValue, traits.Value));
            }

            foreach (var field in type.GetFields(binding))
            {
                // Backing fields and other compiler-generated state are not members anyone wrote,
                // and the generator never sees them — it works from syntax.
                if (field.IsSpecialName || field.Name.Contains('<')) continue;

                var traits = TraitsOf(field.IsPublic, field.IsPrivate || field.IsFamilyAndAssembly, MemberTraits.Field, field);
                if (traits is null || !selection.Admits(traits.Value)) continue;

                members.Add(new(field.Name, field.FieldType, field.GetValue, traits.Value));
            }

            // Materialised to an ARRAY, not returned as the List. The List would satisfy an
            // IReadOnlyList<T> return type and reintroduce the boxed enumerator this whole chain
            // exists to avoid — and it would do so invisibly, because the call sites would not
            // change. Cached per type, so the copy happens once. See IMemberProvider.GetMembers.
            return [.. members];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Null when the member is one neither provider represents.</summary>
    private static MemberTraits? TraitsOf(bool isPublic, bool isPrivate, MemberTraits kind, MemberInfo member)
    {
        if (isPrivate) return null;

        var traits = kind;
        if (!isPublic) traits |= MemberTraits.NonPublic;
        if (IsNonBrowsable(member)) traits |= MemberTraits.NonBrowsable;
        return traits;
    }

    private static bool IsNonBrowsable(MemberInfo member)
        => member.GetCustomAttribute<EditorBrowsableAttribute>() is { State: EditorBrowsableState.Never };

}
