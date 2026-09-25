// Vendored verbatim from master:SourceGenerators/MintPlayer.SourceGenerators.Tools/ValueComparers/ValueComparer.Registry.cs
// for benchmark B1 (the "Legacy" variant). Only the namespace is changed. Do not fix or optimise it:
// the point is to measure what master ships.
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;

namespace MintPlayer.SourceGenerators.Tools.Benchmarks.Legacy;

public static class ComparerRegistry
{
    private static readonly ConcurrentDictionary<Type, object> _byType = new();

    /// <summary>Types whose entry was resolved implicitly (structural, or a <see cref="ValueComparerAttribute"/>), not registered explicitly.</summary>
    private static readonly ConcurrentDictionary<Type, bool> _implicit = new();

    /// <summary>Types already inspected for an implicit comparer (structural or attribute) and found to have none.</summary>
    private static readonly ConcurrentDictionary<Type, bool> _noImplicitComparer = new();

    public static void Register(Type type, object comparer)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (comparer is null) throw new ArgumentNullException(nameof(comparer));

        var expected = typeof(IEqualityComparer<>).MakeGenericType(type);
        if (!expected.IsInstanceOfType(comparer))
            throw new ArgumentException($"Comparer must implement {expected}.");

        _byType[type] = comparer;
        _implicit.TryRemove(type, out _);
    }

    /// <returns>
    /// False if <paramref name="type"/> already has an explicit registration. A comparer that was only
    /// picked up from the type's <see cref="ValueComparerAttribute"/> is replaced: an explicit
    /// registration always wins, whichever happened first.
    /// </returns>
    public static bool TryRegister(Type type, object comparer)
    {
        if (type is null) return false;
        if (comparer is null) return false;

        var expected = typeof(IEqualityComparer<>).MakeGenericType(type);
        if (!expected.IsInstanceOfType(comparer))
            throw new ArgumentException($"Comparer must implement {expected}.");

        if (_byType.TryAdd(type, comparer))
            return true;

        if (!_implicit.TryRemove(type, out _))
            return false; // already explicitly registered

        _byType[type] = comparer;
        return true;
    }

    /// <remarks>
    /// Falls back to the comparer named by <typeparamref name="T"/>'s <see cref="ValueComparerAttribute"/>.
    /// The ValueComparerGenerator puts that attribute on every <c>[AutoValueComparer]</c> type, and the
    /// hand-written models carry it too, but until this fallback existed nothing ever read it: nothing
    /// was registered, <see cref="For{T}"/> returned <see cref="EqualityComparer{T}.Default"/>, and every
    /// model class in every generator pipeline compared by reference. Each syntax transform allocates new
    /// models, so no pipeline step past it could ever be served from cache.
    /// </remarks>
    public static bool TryGet<T>(out IEqualityComparer<T> comparer)
    {
        if (_byType.TryGetValue(typeof(T), out var obj) || TryResolveImplicit(typeof(T), out obj))
        {
            comparer = (IEqualityComparer<T>)obj;
            return true;
        }
        comparer = default!;
        return false;
    }

    public static IEqualityComparer<T> For<T>()
        => TryGet<T>(out var comparer) ? comparer : EqualityComparer<T>.Default;

    private static bool TryResolveImplicit(Type type, out object comparer)
    {
        comparer = default!;
        if (_noImplicitComparer.ContainsKey(type)) return false;

        var created = CreateImplicit(type);
        if (created is null)
        {
            _noImplicitComparer.TryAdd(type, true);
            return false;
        }

        if (_byType.TryAdd(type, created))
            _implicit.TryAdd(type, true);

        // Another thread, or an explicit registration, may have won the race; use whatever is there.
        comparer = _byType[type];
        return true;
    }

    private static object? CreateImplicit(Type type)
        => CreateStructural(type) ?? CreateFromValueComparerAttribute(type);

    /// <summary>Value tuples, by generic definition, and the comparer that compares them item by item.</summary>
    private static readonly Dictionary<Type, Type> ValueTupleComparers = new()
    {
        [typeof(ValueTuple<,>)] = typeof(ValueTupleValueComparer<,>),
        [typeof(ValueTuple<,,>)] = typeof(ValueTupleValueComparer<,,>),
        [typeof(ValueTuple<,,,>)] = typeof(ValueTupleValueComparer<,,,>),
        [typeof(ValueTuple<,,,,>)] = typeof(ValueTupleValueComparer<,,,,>),
        [typeof(ValueTuple<,,,,,>)] = typeof(ValueTupleValueComparer<,,,,,>),
    };

    /// <summary>
    /// Arrays, lists and <see cref="ImmutableArray{T}"/> compare element-wise, each element through this
    /// registry. Their default equality is by reference, which a pipeline step never wants: the generated
    /// <c>WithComparer()</c> for a collected <c>ImmutableArray&lt;T&gt;</c> resolves through here, and so do
    /// array-valued steps.
    /// </summary>
    /// <remarks>
    /// Value tuples compare item by item for the same reason. Their own <c>Equals</c> is structural, but
    /// through each item's default comparer — by reference for a model class — so an array of
    /// <c>(Model, Model)</c> pairs never matched its previous run.
    /// </remarks>
    private static object? CreateStructural(Type type)
    {
        Type? comparerType = null;
        if (type.IsArray && type.GetArrayRank() == 1)
            comparerType = typeof(ArrayValueComparer<>).MakeGenericType(type.GetElementType()!);
        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
            comparerType = typeof(ImmutableArrayValueComparer<>).MakeGenericType(type.GetGenericArguments());
        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            comparerType = typeof(ListValueComparer<>).MakeGenericType(type.GetGenericArguments());
        else if (type.IsGenericType && ValueTupleComparers.TryGetValue(type.GetGenericTypeDefinition(), out var tupleComparer))
            comparerType = tupleComparer.MakeGenericType(type.GetGenericArguments());

        return comparerType is null ? null : Activator.CreateInstance(comparerType, nonPublic: true);
    }

    private static object? CreateFromValueComparerAttribute(Type type)
    {
        var attribute = type.GetCustomAttribute<ValueComparerAttribute>(inherit: true);
        var comparerType = attribute?.ComparerType;
        if (comparerType is null || comparerType.ContainsGenericParameters)
            return null;

        // The generated comparers expose a static Instance; the hand-written ones mostly do not, and
        // several are internal, so fall back to a non-public-capable constructor.
        var instance = comparerType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            ?? Activator.CreateInstance(comparerType, nonPublic: true);

        // IEqualityComparer<in T> is contravariant, so a base type's comparer serves a derived type
        // that inherited the attribute. Anything else is a misconfiguration; ignore it rather than
        // throwing inside a generator pipeline.
        var expected = typeof(IEqualityComparer<>).MakeGenericType(type);
        return expected.IsInstanceOfType(instance) ? instance : null;
    }
}
