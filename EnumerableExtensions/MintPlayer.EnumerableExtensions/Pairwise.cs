namespace MintPlayer.EnumerableExtensions;

public static class PairwiseExtension
{
    /// <summary>Returns pairs as in (1,2) (3,4) (5,6) ...</summary>
    /// <typeparam name="T">Type of elements contained in the enumerable.</typeparam>
    /// <param name="enumerable">Enumerable</param>
    public static IEnumerable<Tuple<T, T?>> Pairwise<T>(this IEnumerable<T> enumerable)
    {
        if (enumerable is null) throw new ArgumentNullException(nameof(enumerable));

        // Materialize once. The previous implementation called Count() and then
        // ElementAt(index + 1) per pair, walking the source O(n) extra times and
        // producing wrong results for a one-shot sequence (an iterator, a reader)
        // that cannot be enumerated twice.
        var items = enumerable as IList<T> ?? enumerable.ToList();

        for (var index = 0; index < items.Count; index += 2)
        {
            yield return new Tuple<T, T?>(
                items[index],
                index + 1 >= items.Count ? default : items[index + 1]);
        }
    }
}
