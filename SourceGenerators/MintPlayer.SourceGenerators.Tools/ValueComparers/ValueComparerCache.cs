using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

internal static class ValueComparerCache
{
    // Hide the cache logic from the surface

    private static class Cache<TValue>
    {
        public static IEqualityComparer<TValue?> Comparer { get; }

        static Cache()
        {
            // Note: Add Comparer when it is not possible via ValueCompareAttribute
            // Note: ImmutableArrays cannot be casted to IEqualityComparer<TValue?>?, therefore use AsEnumerable() to cast it to IEnumerable<TValue?>?.
            // Note: ImmutableDictionary does not contain order, therefore we cannot use it.

            var underlyingType = Nullable.GetUnderlyingType(typeof(TValue));

            var type = underlyingType ?? typeof(TValue);

            var arg1 = type.IsGenericType ? type.GetGenericArguments().ElementAtOrDefault(0) : null;
            var arg2 = type.IsGenericType ? type.GetGenericArguments().ElementAtOrDefault(1) : null;
            var arg3 = type.IsGenericType ? type.GetGenericArguments().ElementAtOrDefault(2) : null;
            var arg4 = type.IsGenericType ? type.GetGenericArguments().ElementAtOrDefault(3) : null;
            var arg5 = type.IsGenericType ? type.GetGenericArguments().ElementAtOrDefault(4) : null;
            var arg6 = type.IsGenericType ? type.GetGenericArguments().ElementAtOrDefault(5) : null;

            var comparerType = type switch
            {
                // Collection Comparers
                { IsArray: true } => typeof(ArrayValueComparer<>).MakeGenericType(type.GetElementType()),
                { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(List<>) => typeof(ListValueComparer<>).MakeGenericType(arg1),
                { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(IEnumerable<>) => typeof(IEnumerableValueComparer<>).MakeGenericType(arg1),
                { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>) => typeof(IReadOnlyCollectionValueComparer<>).MakeGenericType(arg1),
                { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(Dictionary<,>) => typeof(DictionaryValueComparer<,>).MakeGenericType(arg1, arg2),

                // Value Pairs Comparers
                { IsGenericType: true } when underlyingType is null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,>) => typeof(ValueTupleValueComparer<,>).MakeGenericType(arg1, arg2),
                { IsGenericType: true } when underlyingType is null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,>) => typeof(ValueTupleValueComparer<,,>).MakeGenericType(arg1, arg2, arg3),
                { IsGenericType: true } when underlyingType is null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,>) => typeof(ValueTupleValueComparer<,,,>).MakeGenericType(arg1, arg2, arg3, arg4),
                { IsGenericType: true } when underlyingType is null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,>) => typeof(ValueTupleValueComparer<,,,,,>).MakeGenericType(arg1, arg2, arg3, arg4, arg5),
                { IsGenericType: true } when underlyingType is null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,,>) => typeof(ValueTupleValueComparer<,,,,,>).MakeGenericType(arg1, arg2, arg3, arg4, arg5, arg6),
                { IsGenericType: true } when underlyingType is null && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) => typeof(KeyValuePairValueComparer<,>).MakeGenericType(arg1, arg2),
                { IsGenericType: true } when underlyingType is not null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,>) => typeof(NullableValueTupleValueComparer<,>).MakeGenericType(arg1, arg2),
                { IsGenericType: true } when underlyingType is not null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,>) => typeof(NullableValueTupleValueComparer<,,>).MakeGenericType(arg1, arg2, arg3),
                { IsGenericType: true } when underlyingType is not null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,>) => typeof(NullableValueTupleValueComparer<,,,>).MakeGenericType(arg1, arg2, arg3, arg4),
                { IsGenericType: true } when underlyingType is not null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,>) => typeof(NullableValueTupleValueComparer<,,,,>).MakeGenericType(arg1, arg2, arg3, arg4, arg5),
                { IsGenericType: true } when underlyingType is not null && type.GetGenericTypeDefinition() == typeof(ValueTuple<,,,,,>) => typeof(NullableValueTupleValueComparer<,,,,,>).MakeGenericType(arg1, arg2, arg3, arg4, arg5, arg6),
                { IsGenericType: true } when underlyingType is not null && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) => typeof(NullableKeyValuePairValueComparer<,>).MakeGenericType(arg1, arg2),

                // Object Comparers
                { } locationType when locationType == typeof(Location) => typeof(LocationValueComparer),
                { } syntaxType when syntaxType == typeof(SyntaxNode) => typeof(SyntaxValueComparer),
                { } symbolType when typeof(ISymbol).IsAssignableFrom(symbolType) => typeof(SymbolValueComparer),
                { } sourceTextType when sourceTextType == typeof(SourceText) => typeof(SourceTextValueComparer),

                // Default Value Comparers
                { } stringType when stringType == typeof(string) => typeof(DefaultValueComparer<>).MakeGenericType(type),
                { IsPrimitive: true } => typeof(DefaultValueComparer<>).MakeGenericType(type),
                { IsEnum: true } => typeof(DefaultValueComparer<>).MakeGenericType(type),
                { IsValueType: true } => typeof(DefaultValueComparer<>).MakeGenericType(typeof(TValue)),

                // Attribute Value Comparer
                _ => GetOtherComparerType(type),
            };

            Comparer = (IEqualityComparer<TValue?>?)Activator.CreateInstance(comparerType) ?? throw new NotImplementedException();
        }

        private static Type? GetOtherComparerType(Type? type)
        {
            // Check if the type has a ValueComparerAttribute
            var comparerTypeFromAttribute = type.GetCustomAttribute<ValueComparerAttribute>()?.ComparerType;
            if (comparerTypeFromAttribute is { })
                return comparerTypeFromAttribute;

            // customComparers must be filled before Cache<TValue> is called
            return customComparers.FirstOrDefault()?.ComparerType;
        }

        private static List<CustomComparer<TValue>> customComparers = [];
        internal static void AddCustomComparer<TComparer>() where TComparer : ValueComparer<TValue>
        {
            var x = typeof(TComparer);
            var y = new CustomComparer<TValue>() { ComparerType = x };
            customComparers.Add(y);
        }
    }

    public static IEqualityComparer<TValue?> GetComparer<TValue>() => Cache<TValue>.Comparer;
    internal static void AddCustomComparer<TValue, TComparer>() where TComparer : ValueComparer<TValue>
        => Cache<TValue>.AddCustomComparer<TComparer>();
}

internal class CustomComparer<TValue>
{
    public Type? ComparerType { get; set; }
}