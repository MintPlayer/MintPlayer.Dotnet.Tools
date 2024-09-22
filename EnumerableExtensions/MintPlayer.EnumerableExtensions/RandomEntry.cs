namespace MintPlayer.EnumerableExtensions;

public static class RandomEntry
{
    private static readonly Random random = new Random();
    public static T RandomElement<T>(this IEnumerable<T> enumerable)
    {
        var list = enumerable.ToList();
        //var index = Random.Shared.Next(list.Count);
        var index = random.Next(list.Count);
        return list[index];
    }
}
