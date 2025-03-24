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
}
