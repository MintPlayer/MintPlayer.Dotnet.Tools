using static System.Net.Mime.MediaTypeNames;

namespace MintPlayer.EnumerableExtensions;

public static class PairwiseExtension
{
    /// <summary>Returns pairs as in (1,2) (3,4) (5,6) ...</summary>
    /// <typeparam name="T">Type of elements contained in the enumerable.</typeparam>
    /// <param name="enumerable">Enumerable</param>
    public static IEnumerable<Tuple<T, T?>> Pairwise<T>(this IEnumerable<T> enumerable)
    {
        var count = enumerable.Count();
        return enumerable
            .Select((item, index) =>
            {
                if (index % 2 == 0)
                {
                    return new Tuple<T, T?>(
                        item,
                        index + 1 >= count
                            ? default(T)
                            : enumerable.ElementAt(index + 1)
                    );
                }
                else
                {
                    return null;
                }
            })
            .Where(item => item != null)
            .Cast<Tuple<T, T?>>();
    }
}
