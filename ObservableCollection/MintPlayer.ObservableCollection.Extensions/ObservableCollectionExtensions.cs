using MintPlayer.ObservableCollection.Extensions.Enums;

namespace MintPlayer.ObservableCollection.Extensions;

public static class ObservableCollectionExtensions
{
    #region Remove Exceeding

    /// <summary>
    /// Ensure the collection have at most <paramref name="maxItemCount"/> items, exceeding items will be removed from the head/tail of the collection.
    /// </summary>
    /// <typeparam name="T">Collection type</typeparam>
    /// <param name="collection">Collection to restrict the number of items for</param>
    /// <param name="maxItemCount">The maximum number of items to keep in this <paramref name="collection"/>.</param>
    /// <param name="side"></param>
    private static void RemoveExceedingAt<T>(this ObservableCollection<T> collection, int maxItemCount, ECollectionSide side)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItemCount);

        var exceed = collection.Count - maxItemCount;
        if (exceed <= 0) return;

        switch (side)
        {
            case ECollectionSide.Head:
                collection.RemoveRange(0, exceed);
                break;
            case ECollectionSide.Tail:
                collection.RemoveRange(maxItemCount, exceed);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, null);
        }
    }
    #endregion

    /// <summary>
    /// Add an item to the collection, and ensure the collection have at most <paramref name="maxItemCount"/> items.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="item"></param>
    /// <param name="maxItemCount"></param>
    public static void Add<T>(this ObservableCollection<T> collection, T item, int maxItemCount)
    {
        collection.Add(item);
        collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
    }

    /// <summary>
    /// Add an item to the collection if it does not already exist in the collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="item"></param>
    /// <returns></returns>
    public static bool AddDistinct<T>(this ObservableCollection<T> collection, T item)
    {
        if (collection.Contains(item)) return false;
        collection.Add(item);
        return true;
    }

    /// <summary>
    /// Add an item to the collection if it does not already exist in the collection, and ensure the collection have at most <paramref name="maxItemCount"/> items.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="item"></param>
    /// <param name="maxItemCount"></param>
    /// <returns></returns>
    public static bool AddDistinct<T>(this ObservableCollection<T> collection, T item, int maxItemCount)
    {
        if (collection.Contains(item)) return false;
        collection.Add(item);
        collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
        return true;
    }

    /// <summary>
    /// Add an item to the collection if it does not already exist in the collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="item"></param>
    /// <param name="comparer"></param>
    /// <returns></returns>
    public static bool AddDistinct<T>(this ObservableCollection<T> collection, T item, IEqualityComparer<T> comparer)
    {
        if (collection.Contains(item, comparer)) return false;
        collection.Add(item);
        return true;
    }

    /// <summary>
    /// Add an item to the collection if it does not already exist in the collection, and ensure the collection have at most <paramref name="maxItemCount"/> items.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="item"></param>
    /// <param name="maxItemCount"></param>
    /// <param name="comparer"></param>
    /// <returns></returns>
    public static bool AddDistinct<T>(this ObservableCollection<T> collection, T item, int maxItemCount, IEqualityComparer<T> comparer)
    {
        if (collection.Contains(item, comparer)) return false;
        collection.Add(item);
        collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
        return true;
    }


    /// <summary>
    /// Add a range of items to the collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="items"></param>
    public static void AddRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items)
    {
        collection.AddRange(items.Cast<T>());
    }

    /// <summary>
    /// Add a range of items to the collection, and ensure the collection have at most <paramref name="maxItemCount"/> items.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="items"></param>
    /// <param name="maxItemCount"></param>
    public static void AddRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items, int maxItemCount)
    {
        collection.AddRange(items.Cast<T>());
        collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
    }

    /// <summary>
    /// Add a range of items to the collection, and ensure the collection have at most <paramref name="maxItemCount"/> items.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="items"></param>
    /// <param name="maxItemCount"></param>
    public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items, int maxItemCount)
    {
        collection.AddRange(items);
        collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
    }

    /// <summary>
    /// Add a range of items to the collection if they do not already exist in the collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="items"></param>
    /// <returns></returns>
    public static IEnumerable<T> AddDistinctRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items)
    {
        var distinctItems = items.Cast<T>().Distinct().Where(item => !collection.Contains(item));
        collection.AddRange(distinctItems);
        return distinctItems;
    }

    /// <summary>
    /// Add a range of items to the collection if they do not already exist in the collection, and ensure the collection have at most <paramref name="maxItemCount"/> items.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="items"></param>
    /// <param name="maxItemCount"></param>
    /// <returns></returns>
    public static IEnumerable<T> AddDistinctRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items, int maxItemCount)
    {
        var distinctItems = items.Cast<T>().Distinct().Where(item => !collection.Contains(item));
        collection.AddRange(distinctItems);
        collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
        return distinctItems;
    }

    /// <summary>
    /// Add a range of items to the collection if they do not already exist in the collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="items"></param>
    /// <returns></returns>
    public static IEnumerable<T> AddDistinctRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        var distinctItems = items.Distinct().Where(item => !collection.Contains(item));
        collection.AddRange(distinctItems);
        return distinctItems;
    }

    /// <summary>
    /// Add a range of items to the collection if they do not already exist in the collection, and ensure the collection have at most <paramref name="maxItemCount"/> items.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="items"></param>
    /// <param name="maxItemCount"></param>
    /// <returns></returns>
    public static IEnumerable<T> AddDistinctRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items, int maxItemCount)
    {
        var distinctItems = items.Distinct().Where(item => !collection.Contains(item));
        collection.AddRange(distinctItems);
        collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
        return distinctItems;
    }

    /// <summary>
    /// Add a range of items to the collection if they do not already exist in the collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="items"></param>
    /// <param name="comparer"></param>
    /// <returns></returns>
    public static IEnumerable<T> AddDistinctRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items, IEqualityComparer<T> comparer)
    {
        var distinctItems = items.Cast<T>().Distinct(comparer).Where(item => !collection.Contains(item, comparer));
        collection.AddRange(distinctItems);
        return distinctItems;
    }

    /// <summary>
    /// Add a range of items to the collection if they do not already exist in the collection, and ensure the collection have at most <paramref name="maxItemCount"/> items.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="items"></param>
    /// <param name="maxItemCount"></param>
    /// <param name="comparer"></param>
    /// <returns></returns>
    public static IEnumerable<T> AddDistinctRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items, int maxItemCount, IEqualityComparer<T> comparer)
    {
        var distinctItems = items.Cast<T>().Distinct(comparer).Where(item => !collection.Contains(item, comparer));
        collection.AddRange(distinctItems);
        collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
        return distinctItems;
    }

    /// <summary>
    /// Add a range of items to the collection if they do not already exist in the collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="items"></param>
    /// <param name="comparer"></param>
    /// <returns></returns>
    public static IEnumerable<T> AddDistinctRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items, IEqualityComparer<T> comparer)
    {
        var distinctItems = items.Distinct(comparer).Where(item => !collection.Contains(item, comparer));
        collection.AddRange(distinctItems);
        return distinctItems;
    }

    /// <summary>
    /// Add a range of items to the collection if they do not already exist in the collection, and ensure the collection have at most <paramref name="maxItemCount"/> items.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="items"></param>
    /// <param name="maxItemCount"></param>
    /// <param name="comparer"></param>
    /// <returns></returns>
    public static IEnumerable<T> AddDistinctRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items, int maxItemCount, IEqualityComparer<T> comparer)
    {
        var distinctItems = items.Distinct(comparer).Where(item => !collection.Contains(item, comparer));
        collection.AddRange(distinctItems);
        collection.RemoveExceedingAt(maxItemCount, ECollectionSide.Head);
        return distinctItems;
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
        var halfCount = collection.Count / 2;
        collection.Insert(index, item);
        collection.RemoveExceedingAt(maxItemCount, index >= halfCount ? ECollectionSide.Head : ECollectionSide.Tail);
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

    /// <summary>
    /// Insert an item at the specified index if it does not already exist in the collection.
    /// </summary>
    /// <remarks>
    /// If the <paramref name="index"/> is greater than or equal to the half of the collection count, exceeding items will be removed from the head of the collection, otherwise at the tail.
    /// </remarks>
    /// <typeparam name="T">Collection type</typeparam>
    /// <param name="collection">Collection to restrict the number of items for</param>
    /// <param name="index">Position at where the item should be inserted</param>
    /// <param name="item">Item to insert</param>
    public static bool InsertDistinct<T>(this ObservableCollection<T> collection, int index, T item)
    {
        if (collection.Contains(item)) return false;
        collection.Insert(index, item);
        return true;
    }

    /// <summary>
    /// Insert an item at the specified index if it does not already exist in the collection, and ensure the collection have at most <paramref name="maxItemCount"/> items.
    /// </summary>
    /// <remarks>
    /// If the <paramref name="index"/> is greater than or equal to the half of the collection count, exceeding items will be removed from the head of the collection, otherwise at the tail.
    /// </remarks>
    /// <typeparam name="T">Collection type</typeparam>
    /// <param name="collection">Collection to restrict the number of items for</param>
    /// <param name="index">Position at where the item should be inserted</param>
    /// <param name="item">Item to insert</param>
    /// <param name="maxItemCount">The maximum number of items to keep in this <paramref name="collection"/>.</param>
    public static bool InsertDistinct<T>(this ObservableCollection<T> collection, int index, T item, int maxItemCount)
    {
        if (collection.Contains(item)) return false;
        var halfCount = collection.Count / 2;
        collection.Insert(index, item);
        collection.RemoveExceedingAt(maxItemCount, index >= halfCount ? ECollectionSide.Head : ECollectionSide.Tail);
        return true;
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
    public static bool InsertDistinct<T>(this ObservableCollection<T> collection, int index, T item, int maxItemCount, ECollectionSide removeItemsAt)
    {
        if (collection.Contains(item)) return false;
        collection.Insert(index, item);
        collection.RemoveExceedingAt(maxItemCount, removeItemsAt);
        return true;
    }

    /// <summary>
    /// Remove a collection of items from the collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="items"></param>
    public static void RemoveRange<T>(this ObservableCollection<T> collection, System.Collections.IEnumerable items)
    {
        collection.RemoveRange(items.Cast<T>());
    }

    /// <summary>
    /// Remove a collection of items from the collection.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="collection"></param>
    /// <param name="start"></param>
    /// <param name="count"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static void RemoveRange<T>(this ObservableCollection<T> collection, int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0) return;
        if (start >= collection.Count) throw new ArgumentOutOfRangeException(nameof(start), start, "The index is outside the range of this list.");

        collection.RemoveRange(collection.Skip(start).Take(count));
    }
}

