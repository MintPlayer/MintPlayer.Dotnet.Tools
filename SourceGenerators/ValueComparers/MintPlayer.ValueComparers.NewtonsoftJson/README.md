# MintPlayer.ValueComparers.NewtonsoftJson

An `IEqualityComparer<JObject>` for `Newtonsoft.Json.Linq.JObject`, for use in incremental source generators.

## Why this exists

An incremental generator only skips work when it can tell that its inputs are unchanged, and it
decides that with `EqualityComparer<T>.Default`. `JObject` compares by reference, so a model carrying one
looks different on every single run — the generator re-runs, and the caching that makes
incremental generators fast is silently lost.

`JObjectValueComparer` compares two `JObject`s by their compact serialized form (ordinal), so equal JSON
compares equal, and hashes that same form so equal objects hash equally. Property order is significant,
because the serialized form preserves it.

## Usage

Put `[UseEqualityComparer]` on the `JObject` property of an
[`[AutoValueComparer]`](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/SourceGenerators/ValueComparerGenerator/MintPlayer.ValueComparerGenerator/README.md)
model:

```csharp
using MintPlayer.ValueComparerGenerator.Attributes;
using MintPlayer.ValueComparers.NewtonsoftJson;
using Newtonsoft.Json.Linq;

[AutoValueComparer]
public partial class ConfigModel
{
    public string Name { get; set; } = string.Empty;

    [UseEqualityComparer(typeof(JObjectValueComparer))]
    public JObject? Settings { get; set; }
}
```

The generated `Equals`/`GetHashCode` call `JObjectValueComparer.Instance` for that property. Nothing needs
to be registered.

It is a plain `IEqualityComparer<JObject?>`, so it also works anywhere else a comparer is accepted:
`new HashSet<JObject?>(JObjectValueComparer.Instance)`, a dictionary, or as the element comparer of
`ValueEquality.List(a, b, JObjectValueComparer.Instance)`.

## Breaking changes in 12.0.0

- `JObjectValueComparer` no longer derives from `ValueComparer<JObject>` (which is deleted from
  MintPlayer.SourceGenerators.Tools). It is a sealed `IEqualityComparer<JObject?>` with a static `Instance`.
- `JObjectValueComparer.Register()` is gone, together with the registry it registered into. Replace the
  module initializer that called it with `[UseEqualityComparer(typeof(JObjectValueComparer))]` on each
  `JObject` property.
- The package no longer depends on MintPlayer.SourceGenerators.Tools.

## Related packages

- [MintPlayer.ValueComparerGenerator](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/SourceGenerators/ValueComparerGenerator/MintPlayer.ValueComparerGenerator/README.md) — generates value equality for your own model types
- [MintPlayer.SourceGenerators.Tools](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/SourceGenerators/MintPlayer.SourceGenerators.Tools/README.md) — `ValueEquality` and `EquatableArray<T>`
