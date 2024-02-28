namespace MintPlayer.EnumerableExtensions;

public static class RandomEntry
{
    public static T RandomElement<T>(this IEnumerable<T> enumerable)
    {
        var list = enumerable.ToList();
        var index = Random.Shared.Next(list.Count);
        return list[index];
    }
}
