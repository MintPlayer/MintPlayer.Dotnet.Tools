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
    /// Converts a sequence of value types to a sequence of nullable value types of the same underlying type.
    /// </summary>
    /// <remarks>This method enables LINQ queries and other operations that require nullable value types when
    /// working with sequences of non-nullable value types. The order and number of elements in the returned sequence
    /// are the same as in the source sequence.</remarks>
    /// <typeparam name="T">The value type of the elements in the source sequence.</typeparam>
    /// <param name="source">The sequence of value type elements to convert. Cannot be null.</param>
    /// <returns>An IEnumerable<T?> containing the elements of the source sequence, each cast to a nullable value type.</returns>
    public static IEnumerable<T?> AsNullable<T>(this IEnumerable<T> source) where T : struct
    {
        return source.Cast<T?>();
    }

    /// <summary>
    /// Converts a reference type value to its nullable equivalent.
    /// </summary>
    /// <typeparam name="T">The reference type to convert to a nullable type.</typeparam>
    /// <param name="source">The reference type value to convert. Can be null.</param>
    /// <returns>A nullable value of type T that represents the original value, or null if the source is null.</returns>
    public static T? AsNullable<T>(this T source) where T : class
    {
        return (T?)source;
    }
}
