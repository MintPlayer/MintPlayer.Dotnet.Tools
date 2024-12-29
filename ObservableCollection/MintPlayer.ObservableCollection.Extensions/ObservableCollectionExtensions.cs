using System;

namespace MintPlayer.ObservableCollection.Extensions;

public static class ObservableCollectionExtensions
{
    public static void RemoveRange<T>(this ObservableCollection<T> collection, int start, int count)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), start, "Index can not be negative.");
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Count can not be negative.");
        if (count == 0) return;
        if (start >= collection.Count) throw new ArgumentOutOfRangeException(nameof(start), start, "The index is outside the range of this list.");

        collection.RemoveRange(collection.Skip(start).Take(count));
    }
}
