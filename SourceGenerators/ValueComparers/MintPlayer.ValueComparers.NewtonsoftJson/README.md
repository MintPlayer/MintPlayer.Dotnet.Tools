# MintPlayer.ValueComparers.NewtonsoftJson

A value-comparer for `Newtonsoft.Json.Linq.JObject`, for use in incremental source generators.

## Why this exists

An incremental generator only skips work when it can tell that its inputs are unchanged, and it
decides that with `IEqualityComparer<T>`. `JObject` compares by reference, so a model carrying one
looks different on every single run — the generator re-runs, and the caching that makes
incremental generators fast is silently lost.

This package registers a comparer that compares two `JObject`s by their compact serialized form,
so equal JSON compares equal.

## Usage

Register once, from a module initializer in your generator:

```csharp
using MintPlayer.ValueComparers.NewtonsoftJson;

internal static class Comparers
{
    [ModuleInitializer]
    internal static void Register() => JObjectValueComparer.Register();
}
```

From then on, anything that resolves comparers through
`MintPlayer.SourceGenerators.Tools`' `ComparerRegistry` — including the comparers written by
[MintPlayer.ValueComparerGenerator](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/SourceGenerators/ValueComparerGenerator/MintPlayer.ValueComparerGenerator/README.md)
— will use it for `JObject` members.

`Register()` uses `TryRegister`, so it will not overwrite a comparer you registered yourself, and
calling it more than once is harmless.

## Related packages

- [MintPlayer.SourceGenerators.Tools](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/SourceGenerators/MintPlayer.SourceGenerators.Tools/README.md) — the base `ValueComparer<T>` and the registry this plugs into
- [MintPlayer.ValueComparerGenerator](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/SourceGenerators/ValueComparerGenerator/MintPlayer.ValueComparerGenerator/README.md) — generates value-comparers for your own model types
