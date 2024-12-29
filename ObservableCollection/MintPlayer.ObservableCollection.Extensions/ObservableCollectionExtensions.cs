namespace MintPlayer.ObservableCollection.Extensions;

public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Ensure the collection have at most <paramref name="maxItemCount"/> items, exceeding items will be removed from the head of the collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="maxItemCount">The maximum number of items to keep in this <paramref name="collection"/>.</param>
    private static void RemoveExceedingAtHead<T>(this ObservableCollection<T> collection, int maxItemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItemCount);

        var exceed = collection.Count - maxItemCount;
        if (exceed > 0)
            collection.RemoveRange(0, exceed);
    }

    /// <summary>
    /// Ensure the collection have at most <paramref name="maxItemCount"/> items, exceeding items will be removed from the tail of the collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="maxItemCount">The maximum number of items to keep in this <paramref name="collection"/>.</param>
    private static void RemoveExceedingAtTail<T>(this ObservableCollection<T> collection, int maxItemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItemCount);

        var exceed = collection.Count - maxItemCount;
        if (exceed > 0)
            collection.RemoveRange(maxItemCount, exceed);
    }

    public static void AddRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items)
    {
        collection.AddRange(items.Cast<T>());
    }

    public static void AddRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items, int maxItemCount)
    {
        collection.AddRange(items.Cast<T>());
        collection.RemoveExceedingAtHead(maxItemCount);
    }

    public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items, int maxItemCount)
    {
        collection.AddRange(items);
        collection.RemoveExceedingAtHead(maxItemCount);
    }

    public static void Add<T>(this ObservableCollection<T> collection, T item, int maxItemCount)
    {
        collection.Add(item);
        collection.RemoveExceedingAtHead(maxItemCount);
    }

    public static void RemoveRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items)
    {
        collection.RemoveRange(items.Cast<T>());
    }

    public static void RemoveRange<T>(this ObservableCollection<T> collection, int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0) return;
        if (start >= collection.Count) throw new ArgumentOutOfRangeException(nameof(start), start, "The index is outside the range of this list.");

        collection.RemoveRange(collection.Skip(start).Take(count));
    }

    public static void Insert<T>(this ObservableCollection<T> collection, int index, T item, int maxItemCount)
    {
        collection.Insert(index, item);
        var halfCount = (collection.Count - 1) / 2;
        if (index >= halfCount) collection.RemoveExceedingAtHead(maxItemCount);
        else collection.RemoveExceedingAtTail(maxItemCount);
    }
}

