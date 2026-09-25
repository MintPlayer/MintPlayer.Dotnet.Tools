# MintPlayer.SourceGenerators.Tools
This package makes it easier to write your own source-generators. The library provides:
- An `IncrementalGenerator` class that implements `IIncrementalGenerator` and already reads several options (like the `RootNamespace` of the project) beforehand)
- A `Producer` class that hands you a ready-to-use `IndentedTextWriter`
- A `ProduceCode(...)` extension method where you can pass in your source-providers

## Example
Generator:

```csharp
namespace ExampleGenerators;

// Use the ready-made IncrementalGenerator

[Generator(LanguageNames.CSharp)]
public class ExampleGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var typesToMapProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ExampleGenerators.Attributes.ExampleAttribute",
                static (node, ct) => node is not null,
                static (ctx, ct) =>
                {
                    if (ctx.SemanticModel.GetDeclaredSymbol(ctx.TargetNode, ct) is INamedTypeSymbol typeSymbol &&
                        ctx.Attributes.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "ExampleGenerators.Attributes.ExampleAttribute") is { } attr &&
                        attr.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol mapType)
                    {
                        return new Models.TypeToMap
                        {
                            ...
                        };
                    }
                    return null;
                }
            )
            .Where(static m => m is not null);

        var typesToMapSourceProvider = typesToMapProvider
            .Collect()
            .Combine(settingsProvider)
            .Select(static Producer (p, ct) => new MapperProducer(p.Left, p.Right.RootNamespace!));

        // Pass your source-providers to the ready-made ProduceCode extension method 
        context.ProduceCode(typesToMapSourceProvider);
    }
}
```

`Initialize(context, settingsProvider)` is the method to override. `settingsProvider` carries the project's settings
(`RootNamespace`, `LanguageVersion`, target framework, ...) and is itself value-equal, so it stays cached until a
setting changes.

Producer:

```csharp
public sealed class MapperProducer : Producer
{
    private readonly IEnumerable<TypeToMap> typesToMap;
    public MapperProducer(IEnumerable<TypeToMap> typesToMap, string rootNamespace) : base(rootNamespace, "Mappers.g.cs")
    {
        this.typesToMap = typesToMap;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine($"namespace {RootNamespace}");
        writer.WriteLine("{");
        writer.Indent++;

        writer.WriteLine("public static class MapperExtensions");
        writer.WriteLine("{");
        writer.Indent++;

        // Generate more code based on the data in typesToMap

        writer.Indent--;
        writer.WriteLine("}");

        writer.Indent--;
        writer.WriteLine("}");
    }
}
```

## Use the ValueComparerGenerator
Roslyn compares every step's output with `EqualityComparer<T>.Default`, so your models need value equality. If you
install [MintPlayer.ValueComparerGenerator](https://www.nuget.org/packages/MintPlayer.ValueComparerGenerator) too,
it generates it for you.

TypeToMap.cs

```csharp
namespace MintPlayer.Mapper.Models;

[AutoValueComparer]
public partial class TypeToMap
{
    public string DeclaredType { get; set; }
    public string DeclaredTypeName { get; set; }
    public PropertyDeclaration[] DeclaredProperties { get; set; } = [];
    public string MappingType { get; set; }
    public string MappingTypeName { get; set; }
    public PropertyDeclaration[] MappingProperties { get; set; } = [];
    public string DestinationNamespace { get; set; }
}
```

The generator writes `IEquatable<T>`, `Equals(object)` and `GetHashCode()` on `TypeToMap` itself, so the model is
value-equal in every step, in a `Collect()`, and inside a `Combine` tuple, with no comparer passed anywhere.

## Value equality helpers
Two public types support value-equal models. The generated code uses them, and so can a model you write by hand.

- **`ValueEquality`** is a static, reflection-free helper. It has `Array`, `ImmutableArray`, `List`, `Sequence` and
  `Dictionary` comparisons with matching `*Hash` methods, `Combine` for folding a hash, and a sealed comparer with a
  static `Instance` for each shape (`ArrayComparer<T>`, `ImmutableArrayComparer<T>`, `ListComparer<T>`,
  `SequenceComparer<T>`, `DictionaryComparer<K,V>`, plus `DelegateComparer<T>`). Pass one as the element comparer to
  compose nested collections. Sequences compare element-wise and in order, by the declared shape rather than the
  runtime type. Dictionaries compare order-insensitively. A default `ImmutableArray` equals only another default one.
- **`EquatableArray<T>`** is a readonly struct over `T[]` that implements `IEquatable`. Return it from a step that
  builds a new collection, such as a `Select` after `Collect()` that filters or projects, or a transform returning an
  array. A plain array or `ImmutableArray<T>` compares by reference, so that step would report `Modified` on every run.
  A default `EquatableArray<T>` behaves as empty. `.ToEquatableArray()` converts any sequence, and arrays and
  `ImmutableArray<T>` convert implicitly.

```csharp
var registrations = classesProvider
    .Combine(assemblyLevelProvider)
    .Select(static (p, ct) => p.Left.Concat(p.Right).ToEquatableArray());
```

`LocationKey`, `PathSpec`, `PathSpecElement` and `Settings` implement `IEquatable<T>`, so a model can hold them.

## Staying incremental
A generator that emits the same output on every keystroke is still doing the work on every keystroke. The pieces above are built so that an edit your generator does not care about stops at an equal model:

- `ProduceCode(...)` gives each producer its own output step and never touches the `Compilation`, so a file is regenerated only when its own producer's input changed. Since 11.0.0; before that every producer was combined with the `Compilation` and re-ran on every edit.
- Models compare by value under the default comparer: generated by `[AutoValueComparer]`, or written by hand with `ValueEquality`. A step that builds a new collection returns `EquatableArray<T>`.
- `ReportDiagnostics(...)` needs the `Compilation` to rebuild in-tree locations, which `#pragma` and `.editorconfig` severity depend on. Implement `IConditionalDiagnosticReporter` and a reporter with nothing to report runs no step at all.

Rules of thumb for your models: no `ISymbol`, `SyntaxNode`, `SyntaxTokenList`, `Location` or `Compilation` (use strings and `LocationKey`; `MINT001` enforces it on `[AutoValueComparer]` models); return `EquatableArray<T>` from every step that builds a collection; and carry a `LocationKey` only on a model that will actually report a diagnostic, so that typing above a declaration does not invalidate it.

To prove it, `MintPlayer.SourceGenerators.Testing` runs a generator twice and reports the run reason of every output step.

## Breaking changes in 12.0.0
The value-comparer runtime is removed, with no backward compatibility. Models are value-equal by themselves now.

- **Removed:** `ValueComparer<T>`, `ComparerRegistry`, `[ValueComparer]`, the whole
  `MintPlayer.SourceGenerators.Tools.ValueComparers` namespace (every structural, tuple, dictionary, Symbol, Syntax
  and SourceText comparer, and `ReferenceEqualityComparer<T>`), `ICompilationCache` and its cache types,
  `HashCodeCompat`, the `ModuleInitializerAttribute` polyfill, and the public `SettingsValueComparer`.
- **`IncrementalGenerator.Initialize`** loses its third parameter. Override
  `Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)`
  and drop `using MintPlayer.SourceGenerators.Tools.ValueComparers;`.
- **`.WithComparer()` / `.WithNullableComparer()`** are no longer generated. Delete the calls. A step that builds a
  new collection returns `EquatableArray<T>` instead of an array.
- **Registering a comparer** (`ComparerRegistry.Register`, `JObjectValueComparer.Register()`) is replaced by
  `[UseEqualityComparer(typeof(...))]` on the property.
- **Added:** `ValueEquality` and `EquatableArray<T>`; `LocationKey`, `PathSpec`, `PathSpecElement` and `Settings`
  implement `IEquatable<T>` with consistent hashes. `LocationKey` is sealed, and `PathSpecElement` equality now
  includes `GenericTypeParameters`.