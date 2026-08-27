# Value-comparer plug-ins

Index only — the documentation lives with the package.

Ready-made `ValueComparer<T>` implementations for third-party types, so incremental generators
carrying those types in their models still cache correctly.

| Project | Package | Purpose |
|---|---|---|
| [MintPlayer.ValueComparers.NewtonsoftJson](MintPlayer.ValueComparers.NewtonsoftJson/README.md) | `MintPlayer.ValueComparers.NewtonsoftJson` | Comparer for `JObject`, which otherwise compares by reference and defeats caching. |

To generate comparers for your own types, see
[ValueComparerGenerator](../ValueComparerGenerator/README.md).
