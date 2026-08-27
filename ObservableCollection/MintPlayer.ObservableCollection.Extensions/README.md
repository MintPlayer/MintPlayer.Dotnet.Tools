# MintPlayer.ObservableCollection.Extensions

Extension methods for the built-in `System.Collections.ObjectModel.ObservableCollection<T>`.

These work on the **standard** `ObservableCollection<T>`, so you can use them without adopting
[MintPlayer.ObservableCollection](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/ObservableCollection/MintPlayer.ObservableCollection/README.md).

## Installation

```shell
dotnet add package MintPlayer.ObservableCollection.Extensions
```

## Bulk operations

```csharp
using MintPlayer.ObservableCollection.Extensions;

collection.AddRange(items);
collection.RemoveRange(items);
collection.RemoveRange(start: 2, count: 3);
```

## Bounded collections

Keep at most N items — useful for a "recent items" list that must not grow without limit. Excess
items are dropped from the head by default, or from the tail with `ECollectionSide`:

```csharp
recentSearches.Add(term, maxItemCount: 10);
recentSearches.Insert(0, term, maxItemCount: 10, removeItemsAt: ECollectionSide.Tail);
```

## Distinct adds

Add only if not already present; the `bool` says whether the item was added, and the range
overloads return the items that were actually added:

```csharp
var added = tags.AddDistinct(tag);
var newTags = tags.AddDistinctRange(incoming);

// With your own notion of equality, and a cap:
tags.AddDistinct(tag, maxItemCount: 20, comparer: StringComparer.OrdinalIgnoreCase);
```

Every distinct/bounded overload composes: `maxItemCount`, an `IEqualityComparer<T>`, and the side
to trim from can be combined.

## Related packages

- [MintPlayer.ObservableCollection](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/ObservableCollection/MintPlayer.ObservableCollection/README.md) — a replacement `ObservableCollection<T>` that raises `CollectionChanged` once for a range, monitors item properties, and can be updated from any thread
