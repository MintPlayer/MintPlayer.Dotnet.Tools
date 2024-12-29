namespace MintPlayer.ObservableCollection.Extensions;

public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Only call this method on Add (when adding items at the end of the collection)
    /// </summary>
    private static void RemoveExceeding<T>(this ObservableCollection<T> collection, int maxItemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItemCount);

        var exceed = collection.Count - maxItemCount;
        if (exceed > 0)
            collection.RemoveRange(0, exceed);
    }

    public static void AddRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items)
    {
        collection.AddRange(items.Cast<T>());
    }

    public static void AddRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items, int maxItemCount)
    {
        collection.AddRange(items.Cast<T>());
        collection.RemoveExceeding(maxItemCount);
    }

    public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items, int maxItemCount)
    {
        collection.AddRange(items);
        collection.RemoveExceeding(maxItemCount);
    }

    public static void Add<T>(this ObservableCollection<T> collection, T item, int maxItemCount)
    {
        collection.Add(item);
        collection.RemoveExceeding(maxItemCount);
    }

    public static void RemoveRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items)
    {
        collection.RemoveRange(items.Cast<T>());
    }

    public static void RemoveRange<T>(this ObservableCollection<T> collection, int start, int count)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), start, "Index can not be negative.");
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Count can not be negative.");
        if (count == 0) return;
        if (start >= collection.Count) throw new ArgumentOutOfRangeException(nameof(start), start, "The index is outside the range of this list.");

        collection.RemoveRange(collection.Skip(start).Take(count));
    }
}
