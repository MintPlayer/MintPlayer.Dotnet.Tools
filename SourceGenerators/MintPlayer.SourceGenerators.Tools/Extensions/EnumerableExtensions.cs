namespace MintPlayer.SourceGenerators.Tools;

public static class EnumerableExtensions
{
    /// <summary>
    /// Filters out all null values from the source collection
    /// </summary>
    /// <typeparam name="T">Enumerable type</typeparam>
    public static IEnumerable<T> NotNull<T>(this IEnumerable<T?> source) where T : class
    {
        return source.Where(item => item is not null).Cast<T>();
    }

    /// <summary>
    /// Returns distinct elements from a sequence according to a specified key selector function.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements of source.</typeparam>
    /// <typeparam name="TKey">The type of key to distinguish elements by.</typeparam>
    public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
    {
        var seenKeys = new HashSet<TKey>();
        foreach (var element in source)
        {
            if (seenKeys.Add(keySelector(element)))
            {
                yield return element;
            }
        }
    }
}
