using Microsoft.CodeAnalysis;
using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

// Per-Compilation hub that every generator in this DLL can use.
public static class ComparerCacheHub
{
    private static readonly ConditionalWeakTable<Compilation, PerCompilationCache> _cwt = new();

    public static PerCompilationCache Get(Compilation compilation) =>
        _cwt.GetValue(compilation, static _ => new PerCompilationCache());
}

public sealed class PerCompilationCache
{
    internal PerCompilationCache() { }

    // Common: cache comparers keyed by ITypeSymbol
    private readonly ConcurrentDictionary<ITypeSymbol, Lazy<IEqualityComparer>> _byType = new(SymbolEqualityComparer.Default);

    // Optional: a general-purpose bucket if you also need other keys
    private readonly ConcurrentDictionary<(string Kind, string Key), Lazy<IEqualityComparer>> _misc = new();

    // Misc bucket for CLR keys (rarely needed)
    private readonly ConcurrentDictionary<string, Lazy<IEqualityComparer>> _byKey = new(StringComparer.Ordinal);

    public IEqualityComparer GetOrCreate(ITypeSymbol type, Func<ITypeSymbol, IEqualityComparer> factory)
        => _byType.GetOrAdd(type, t => new Lazy<IEqualityComparer>(
                () => factory(t),
                LazyThreadSafetyMode.ExecutionAndPublication))
             .Value;

    public T GetOrCreate<T>(string kind, string key, Func<T> factory) where T : class
        => (T)_misc.GetOrAdd((kind, key), _ => new Lazy<IEqualityComparer>(
                () => (IEqualityComparer)factory(),
                LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    public T GetOrCreate<T>(string key, Func<T> factory) where T : class
        => (T)_byKey.GetOrAdd(key, _ => new Lazy<IEqualityComparer>(
                () => (IEqualityComparer)factory(),
                LazyThreadSafetyMode.ExecutionAndPublication))
           .Value;
}

/// <summary>Tuple key that compares two symbols with SymbolEqualityComparer.Default.</summary>
internal readonly struct SymbolPair : IEquatable<SymbolPair>
{
    public readonly ITypeSymbol Source;
    public readonly ITypeSymbol Destination;
    public SymbolPair(ITypeSymbol source, ITypeSymbol destination)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Destination = destination ?? throw new ArgumentNullException(nameof(destination));
    }

    public bool Equals(SymbolPair other) =>
        SymbolEqualityComparer.Default.Equals(Source, other.Source) &&
        SymbolEqualityComparer.Default.Equals(Destination, other.Destination);

    public override bool Equals(object? obj) => obj is SymbolPair sp && Equals(sp);

    public override int GetHashCode()
    {
        unchecked
        {
            var h1 = SymbolEqualityComparer.Default.GetHashCode(Source);
            var h2 = SymbolEqualityComparer.Default.GetHashCode(Destination);
            return (h1 * 397) ^ h2;
        }
    }
}

//internal static class ValueComparerCache
//{
//    // Hide the cache logic from the surface


//    //private static class Cache<TValue>
//    //{
//    //    private static readonly Lazy<IEqualityComparer<TValue?>> lazyComparer = new(() =>
//    //    {
//    //        // Note: Add Comparer when it is not possible via ValueCompareAttribute
//    //        // Note: ImmutableArrays cannot be casted to IEqualityComparer<TValue?>?, therefore use AsEnumerable() to cast it to IEnumerable<TValue?>?.
//    //        // Note: ImmutableDictionary does not contain order, therefore we cannot use it.

//    //        var underlyingType = Nullable.GetUnderlyingType(typeof(TValue));

//    //        var type = underlyingType ?? typeof(TValue);

//    //        var arg1 = type.IsGenericType ? type.GetGenericArguments().ElementAtOrDefault(0) : null;
//    //        var arg2 = type.IsGenericType ? type.GetGenericArguments().ElementAtOrDefault(1) : null;
//    //        var arg3 = type.IsGenericType ? type.GetGenericArguments().ElementAtOrDefault(2) : null;
//    //        var arg4 = type.IsGenericType ? type.GetGenericArguments().ElementAtOrDefault(3) : null;
//    //        var arg5 = type.IsGenericType ? type.GetGenericArguments().ElementAtOrDefault(4) : null;
//    //        var arg6 = type.IsGenericType ? type.GetGenericArguments().ElementAtOrDefault(5) : null;

//    //        var commonComparer = type switch
//    //        {
//    //            ISymbol symbol => SymbolEqualityComparer.Default,
//    //            _ => null,
//    //        };

//    //        if (commonComparer is IEqualityComparer<TValue?> comp)
//    //            return comp;

//    //        //if (commonComparer is not null && typeof(TValue).IsAssignableFrom(typeof(ISymbol)))
//    //        //{
//    //        //    // If the type is ISymbol, use the SymbolEqualityComparer
//    //        //    return (IEqualityComparer<TValue?>)(object)commonComparer;
//    //        //}

//    //        var comparerType = type switch
//    //        {
//    //            // Collection Comparers
//    //            { IsArray: true } => typeof(ArrayValueComparer<>).MakeGenericType(type.GetElementType()),
//    //            { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(List<>) => typeof(ListValueComparer<>).MakeGenericType(arg1),
//    //            { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(IEnumerable<>) => typeof(IEnumerableValueComparer<>).MakeGenericType(arg1),
//    //            { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>) => typeof(IReadOnlyCollectionValueComparer<>).MakeGenericType(arg1),
//    //            { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(Dictionary<,>) => typeof(DictionaryValueComparer<,>).MakeGenericType(arg1, arg2),

//    //            // Value Pairs Comparers
//    //            { IsGenericType: true } when underlyingType is null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,>) => typeof(ValueTupleValueComparer<,>).MakeGenericType(arg1, arg2),
//    //            { IsGenericType: true } when underlyingType is null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,>) => typeof(ValueTupleValueComparer<,,>).MakeGenericType(arg1, arg2, arg3),
//    //            { IsGenericType: true } when underlyingType is null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,>) => typeof(ValueTupleValueComparer<,,,>).MakeGenericType(arg1, arg2, arg3, arg4),
//    //            { IsGenericType: true } when underlyingType is null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,>) => typeof(ValueTupleValueComparer<,,,,,>).MakeGenericType(arg1, arg2, arg3, arg4, arg5),
//    //            { IsGenericType: true } when underlyingType is null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,,>) => typeof(ValueTupleValueComparer<,,,,,>).MakeGenericType(arg1, arg2, arg3, arg4, arg5, arg6),
//    //            { IsGenericType: true } when underlyingType is null && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) => typeof(KeyValuePairValueComparer<,>).MakeGenericType(arg1, arg2),
//    //            { IsGenericType: true } when underlyingType is not null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,>) => typeof(NullableValueTupleValueComparer<,>).MakeGenericType(arg1, arg2),
//    //            { IsGenericType: true } when underlyingType is not null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,>) => typeof(NullableValueTupleValueComparer<,,>).MakeGenericType(arg1, arg2, arg3),
//    //            { IsGenericType: true } when underlyingType is not null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,>) => typeof(NullableValueTupleValueComparer<,,,>).MakeGenericType(arg1, arg2, arg3, arg4),
//    //            { IsGenericType: true } when underlyingType is not null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,>) => typeof(NullableValueTupleValueComparer<,,,,>).MakeGenericType(arg1, arg2, arg3, arg4, arg5),
//    //            { IsGenericType: true } when underlyingType is not null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,,>) => typeof(NullableValueTupleValueComparer<,,,,,>).MakeGenericType(arg1, arg2, arg3, arg4, arg5, arg6),
//    //            { IsGenericType: true } when underlyingType is not null && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) => typeof(NullableKeyValuePairValueComparer<,>).MakeGenericType(arg1, arg2),

//    //            // Object Comparers
//    //            { } locationType when locationType == typeof(LocationKey) => typeof(LocationKeyValueComparer),
//    //            //{ } syntaxType when syntaxType == typeof(SyntaxNode) => typeof(SyntaxValueComparer),
//    //            { } symbolType when typeof(ISymbol).IsAssignableFrom(symbolType) => typeof(SymbolValueComparer),
//    //            //{ } sourceTextType when sourceTextType == typeof(SourceText) => typeof(SourceTextValueComparer),

//    //            // Default Value Comparers
//    //            { } stringType when stringType == typeof(string) => typeof(DefaultValueComparer<>).MakeGenericType(type),
//    //            { IsPrimitive: true } => typeof(DefaultValueComparer<>).MakeGenericType(type),
//    //            { IsEnum: true } => typeof(DefaultValueComparer<>).MakeGenericType(type),
//    //            { IsValueType: true } => typeof(DefaultValueComparer<>).MakeGenericType(typeof(TValue)),

//    //            // Attribute Value Comparer
//    //            _ => GetOtherComparerType(type),
//    //        };

//    //        return (IEqualityComparer<TValue?>?)Activator.CreateInstance(comparerType) ?? throw new NotImplementedException();
//    //    });

//    //    public static IEqualityComparer<TValue?> Comparer => lazyComparer.Value;

//    //    private static Type? GetOtherComparerType(Type? type)
//    //    {
//    //        // Check if the type has a ValueComparerAttribute
//    //        var comparerTypeFromAttribute = type.GetCustomAttribute<ValueComparerAttribute>()?.ComparerType;
//    //        if (comparerTypeFromAttribute is { })
//    //            return comparerTypeFromAttribute;

//    //        // TODO: instead of this CustomComparers list, call a class where you register your custom comparers, if it exists
//    //        // Optionally a class decorated with an attribute

//    //        // customComparers must be filled before Cache<TValue> is called
//    //        return customComparers.FirstOrDefault()?.ComparerType;
//    //    }

//    //    private static List<CustomComparer<TValue>> customComparers = [];
//    //    internal static void AddCustomComparer<TComparer>() where TComparer : ValueComparer<TValue>
//    //    {
//    //        customComparers.Add(new CustomComparer<TValue>() { ComparerType = typeof(TComparer) });
//    //    }
//    //}

//    //public static IEqualityComparer<TValue?> GetComparer<TValue>() => Cache<TValue>.Comparer;
//    //internal static void AddCustomComparer<TValue, TComparer>() where TComparer : ValueComparer<TValue>
//    //    => Cache<TValue>.AddCustomComparer<TComparer>();
//}

////internal class CustomComparer<TValue>
////{
////    public Type? ComparerType { get; set; }
////}