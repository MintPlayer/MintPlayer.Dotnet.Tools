using MintPlayer.ObservableCollection.Extensions.Enums;

namespace MintPlayer.ObservableCollection.Extensions;

public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Ensure the collection have at most <paramref name="maxItemCount"/> items, exceeding items will be removed from the head/tail of the collection.
    /// </summary>
    /// <typeparam name="T">Collection type</typeparam>
    /// <param name="collection">Collection to restrict the number of items for</param>
    /// <param name="maxItemCount">The maximum number of items to keep in this <paramref name="collection"/>.</param>
    private static void RemoveExceedingAt<T>(this ObservableCollection<T> collection, int maxItemCount, ECollectionSide side)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItemCount);

        var exceed = collection.Count - maxItemCount;
        if (exceed > 0)
        {
            switch (side)
            {
                case ECollectionSide.Head:
                    collection.RemoveRange(0, exceed);
                    break;
                case ECollectionSide.Tail:
                    collection.RemoveRange(maxItemCount, exceed);
                    break;
            }
        }
    }

    public static void AddRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items)
    {
        collection.AddRange(items.Cast<T>());
    }

    public static void AddRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items, int maxItemCount)
    {
        collection.AddRange(items.Cast<T>());
        collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
    }

    public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items, int maxItemCount)
    {
        collection.AddRange(items);
        collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
    }

    public static void Add<T>(this ObservableCollection<T> collection, T item, int maxItemCount)
    {
        collection.Add(item);
        collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
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

    /// <summary>
    /// Insert an item at the specified index, and ensure the collection have at most <paramref name="maxItemCount"/> items.
    /// </summary>
    /// <remarks>
    /// If the <paramref name="index"/> is greater than or equal to the half of the collection count, exceeding items will be removed from the head of the collection, otherwise at the tail.
    /// </remarks>
    /// <typeparam name="T">Collection type</typeparam>
    /// <param name="collection">Collection to restrict the number of items for</param>
    /// <param name="index">Position at where the item should be inserted</param>
    /// <param name="item">Item to insert</param>
    /// <param name="maxItemCount">The maximum number of items to keep in this <paramref name="collection"/>.</param>
    public static void Insert<T>(this ObservableCollection<T> collection, int index, T item, int maxItemCount)
    {
        collection.Insert(index, item);
        var halfCount = (collection.Count - 1) / 2;
        if (index >= halfCount) collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
        else collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Tail);
    }

    /// <summary>
    /// Insert an item at the specified index, and ensure the collection have at most <paramref name="maxItemCount"/> items.
    /// </summary>
    /// <typeparam name="T">Collection type</typeparam>
    /// <param name="collection">Collection to restrict the number of items for</param>
    /// <param name="index">Position at where the item should be inserted</param>
    /// <param name="item">Item to insert</param>
    /// <param name="maxItemCount">The maximum number of items to keep in this <paramref name="collection"/>.</param>
    /// <param name="removeItemsAt">Whether to remove excessive items at the start/end of the collection</param>
    public static void Insert<T>(this ObservableCollection<T> collection, int index, T item, int maxItemCount, ECollectionSide removeItemsAt)
    {
        collection.Insert(index, item);
        collection.RemoveExceedingAt(maxItemCount, removeItemsAt);
    }
}

