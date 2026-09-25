# Value-comparer generator
Generates value equality for the models of your incremental source generators, so that every pipeline step caches.

Roslyn compares the output of every incremental step with `EqualityComparer<T>.Default`. A plain class compares by
reference, so a model that the transform builds anew on every run always looks changed, and everything downstream
re-runs on every keystroke. Put `[AutoValueComparer]` on the model and the generator writes `IEquatable<T>`,
`Equals(object)` and `GetHashCode()` on the type itself: the model is value-equal everywhere, in any step, inside a
`Combine` tuple, as a property of another model, in a `HashSet` and in `Assert.Equal`. No comparer is passed anywhere.

## Getting started
Install both [`MintPlayer.ValueComparerGenerator`](https://nuget.org/packages/MintPlayer.ValueComparerGenerator) and [`MintPlayer.ValueComparerGenerator.Attributes`](https://nuget.org/packages/MintPlayer.ValueComparerGenerator.Attributes) in your project. The generated code calls `MintPlayer.SourceGenerators.Tools.ValueEquality`, so the project also references [`MintPlayer.SourceGenerators.Tools`](https://nuget.org/packages/MintPlayer.SourceGenerators.Tools), which every generator built on its `IncrementalGenerator` already does.

## Example
The type, and every type containing it, must be `partial`.

```csharp
using MintPlayer.ValueComparerGenerator.Attributes;

namespace Demo;

[AutoValueComparer]
public sealed partial class CliOption
{
    public string Name { get; set; } = "";
    public IReadOnlyList<string> Aliases { get; set; } = new List<string>();
    public ImmutableArray<Child> Children { get; set; }

    [ComparerIgnore]
    public string CachedText { get; set; } = "";
}

[AutoValueComparer]
public sealed partial class Child
{
    public string Value { get; set; } = "";
}
```

generates (one `<Type>.Equality.g.cs` per model):

```csharp
partial class CliOption : global::System.IEquatable<global::Demo.CliOption>
{
    public bool Equals(global::Demo.CliOption? other)
    {
        if (other is null) return false;
        if (global::System.Object.ReferenceEquals(this, other)) return true;
        // CachedText: [ComparerIgnore]
        return global::System.String.Equals(Name, other.Name, global::System.StringComparison.Ordinal)
            && global::MintPlayer.SourceGenerators.Tools.ValueEquality.List<string>(Aliases, other.Aliases)
            && global::MintPlayer.SourceGenerators.Tools.ValueEquality.ImmutableArray<global::Demo.Child>(Children, other.Children);
    }

    public override bool Equals(object? obj) => Equals(obj as global::Demo.CliOption);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + (Name is null ? 0 : global::System.StringComparer.Ordinal.GetHashCode(Name));
            h = h * 31 + global::MintPlayer.SourceGenerators.Tools.ValueEquality.ListHash<string>(Aliases);
            h = h * 31 + global::MintPlayer.SourceGenerators.Tools.ValueEquality.ImmutableArrayHash<global::Demo.Child>(Children);
            return h;
        }
    }
}
```

`GetHashCode()` hashes exactly the properties that `Equals` compares, so equal models always hash equally.

## How each property is compared
The comparison is chosen from the declared property type at generation time; nothing is looked up at run time.

| Declared property type | Equality |
| --- | --- |
| `string` | ordinal |
| primitive, enum | `==` |
| `T[]` | element-wise, in order |
| `ImmutableArray<T>` | element-wise; `default` equals only `default`, never an empty array |
| `List<T>`, `IList<T>`, `IReadOnlyList<T>` | element-wise, in order, whatever the runtime type |
| `IEnumerable<T>`, `IReadOnlyCollection<T>`, other sequences | element-wise, in order |
| `IDictionary<K,V>`, `IReadOnlyDictionary<K,V>` | same keys with equal values, in any order |
| `ValueTuple<...>` | item by item, applying this table to each item |
| another model, or any other type | `EqualityComparer<T>.Default` |

Nested shapes compose: an `ImmutableArray<ImmutableArray<Model>>`, a `List<(string, List<Model>)>` or a tuple holding a
list all compare structurally.

## Supported shapes
Sealed and non-sealed classes, abstract hierarchies, records (sealed or derived), structs, record structs, generic
types, and types nested in any of these.

- **Hierarchies.** Every type deriving from an `[AutoValueComparer]` type gets its own members too, whether or not it
  carries the attribute, so it must be `partial` as well. An instance is never equal to an instance of another
  runtime type.
- **Equality you wrote yourself.** A member the type already declares (`Equals(T)`, `Equals(object)`,
  `GetHashCode()`) is not generated (`MINT002`).

## Attributes
- `[AutoValueComparer]` on a class, record or struct: generate the members.
- `[ComparerIgnore]` on a property: leave it out of `Equals` and `GetHashCode`.
- `[UseEqualityComparer(typeof(C))]` on a property: compare it with `C`, an `IEqualityComparer<TProperty>` with a
  static `Instance` or a public parameterless constructor. For example
  `[UseEqualityComparer(typeof(JObjectValueComparer))]` from
  [MintPlayer.ValueComparers.NewtonsoftJson](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/SourceGenerators/ValueComparers/MintPlayer.ValueComparers.NewtonsoftJson/README.md).

## Diagnostics

| Id | Severity | Meaning |
| --- | --- | --- |
| `MINT001` | Error | A model property holds a Roslyn type (`ISymbol`, `SyntaxNode`, `Location`, `Compilation`, `SemanticModel`, ...), directly or through a collection, tuple or nested class. It pins the compilation between runs. Project it to a string, a `LocationKey` or a `PathSpec`. |
| `MINT002` | Info (Warning when only one of `Equals(object)`/`GetHashCode()` is declared) | The type declares an equality member itself, so it was not generated. |
| `MINT003` | Error | A model, or a type deriving from one, is not `partial`. |
| `MINT004` | Error | The `[UseEqualityComparer]` type is not an `IEqualityComparer<T>` of the property type, or cannot be created. |
| `MINT005` | Warning | A property type has only reference equality (a class that neither overrides `Equals` nor implements `IEquatable<T>`, and is not a model or a supported collection). Make it equatable, mark it `[AutoValueComparer]`, or use `[UseEqualityComparer]`. |
| `MINT006` | Error | A type containing a model is not `partial`. |

## Using the models in a pipeline
No comparer is needed on any step. A model coming out of a transform, a `Collect()` of models, or a `Combine` tuple
holding models all compare by value under the default comparer.

The one exception is a step that **builds a new collection** from its input, such as a `Select` after `Collect()`
that filters or projects, or a transform returning an array. An array or `ImmutableArray<T>` compares by reference, so
that step would report `Modified` on every run. Return an `EquatableArray<T>` (from `MintPlayer.SourceGenerators.Tools`)
instead:

```csharp
var registrationsProvider = classesProvider
    .Combine(assemblyLevelProvider)
    .Select(static (p, ct) => p.Left.Concat(p.Right).ToEquatableArray());
```

## Joining providers
The MintPlayer.SourceGenerators.Tools package already contains 4 extension methods that help you join providers.

```csharp
var sourceProvider = typeProvider
    .Join(typeTreeProvider)
    .Join(childrenWithoutDerived)
    .Join(settingsProvider)
    .Join(hasCodeAnalysisReference)
    .Select(static Producer (p, ct) => new TreeProducer(p.Item1, p.Item2, p.Item3, p.Item4.RootNamespace!, p.Item5));
```

By using this extension method, you can avoid the nested tuples that would require you to write `p.Left.Left.Left.Left.Right` to access a value.
If you want to join more than 5 providers, you can apply the `[assembly: GenerateJoinMethods(n)]` attribute on a namespace.
The ValueComparerGenerator package contains a source-generator that will generate the necessary extension methods for you.

## Breaking changes in 12.0.0
The full list, with a migration guide, is in the [changelog](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/SourceGenerators/CHANGELOG.md).

- The generator no longer emits an `XValueComparer` class, a `[ValueComparer]` tag, or the `.WithComparer()` /
  `.WithNullableComparer()` extension methods. The members are generated on the model itself. Delete every
  `.WithComparer()`/`.WithNullableComparer()` call; where a step builds a new collection, return `EquatableArray<T>`.
- Records, structs, record structs and generic models are now supported.
- A derived type is now covered even without its own attribute, and must be `partial` (`MINT003`).
- `ComparerRegistry.Register` is replaced by `[UseEqualityComparer]` on the property.
- `MINT001` inspects model properties instead of `.WithComparer()` calls.
- Dictionaries compare order-insensitively.
