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
setting changes. `RootNamespace` is already a valid namespace name: a project named `My-App` gives `My_App`
(`StringExtensions.ToSafeNamespace`), and a `Producer` given none uses `GeneratedCode`.

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

[GenerateEquality]
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

## Discovery, attributes and re-declaring a type (12.2.0)
Helpers so that a generator handles every type kind (class, record, struct, record struct, interface) without
hand-written keyword logic. All are additive.

- **Finding attributes:** `symbol.FindAttribute("N.MyAttribute")`, `FindAttributes(...)`, `HasAttribute(...)` and
  `attributeClass.IsAttribute("N.MyAttribute")` compare the full metadata name (`+` for nested types, `` `1 `` for
  generic ones), so a same-named attribute in another namespace never matches.
- **Reading arguments:** `attribute.GetArgument("count", fallback)` and `TryGetArgument<T>(name, out value)` look the
  name up as a named argument or a constructor parameter (ignoring case) and read the compiler's constant, so `const`
  references, arithmetic, casts and optional-parameter defaults all work. `T` can be a primitive, `string`, an enum,
  `INamedTypeSymbol`/`ITypeSymbol` (`typeof`), `ImmutableArray<TypedConstant>` or `U[]`. An unbound attribute or
  `typeof`, a missing argument or a value that does not convert gives the fallback.
- **Discovery:** `context.SyntaxProvider.ForAttribute<TModel>(metadataName, predicate, transform)` wraps
  `ForAttributeWithMetadataName` with a transform that returns `Discovered<TModel>(model, diagnostics)`. Split it with
  `.Models(trackingName)` and `.Diagnostics()`, then `context.ReportDiagnostics(discovered.Diagnostics())`.
  `DiagnosticInfo.Create(rule, location, args...)` holds a diagnostic by value (rule id, `LocationKey`, string
  arguments; no Roslyn object, the descriptor is looked up by id at report time). Give every descriptor its own id:
  creating a `DiagnosticInfo` for a second, different descriptor under an id already seen throws. Reporting never
  throws: a stale location is reported without one, and a diagnostic that cannot be reported becomes `MPSG002`.
- **Re-declaring a type:** `GetPathSpec()` now also describes the type itself (`Target`), interface parents, and why a
  type cannot be re-declared (`UnsupportedReason`: not partial, `file`, or an unsupported kind).
  `writer.OpenPathSpec(spec, includeTarget: true)` opens every parent and the type with the right keyword, type
  parameters and variance, and never repeats accessibility, modifiers or constraints. `UniqueTypeParameterName` and
  `UniqueTypeParameterPrefix` avoid shadowing the type parameters in scope.
- **File preamble:** override `WritePreamble => true` in a `Producer` to have `Header` and `#nullable enable`
  (`NullableEnable`) written for you; `writer.OpenNamespace(ns)` opens a namespace block, or nothing for the global
  namespace.

```csharp
var discovered = context.SyntaxProvider.ForAttribute(
    "N.MyAttribute",
    static (node, ct) => node is TypeDeclarationSyntax,
    static (ctx, ct) =>
    {
        var type = (INamedTypeSymbol)ctx.TargetSymbol;
        var spec = type.GetPathSpec(ct)!;
        return spec.IsSupported
            ? new Discovered<MyModel>(new MyModel(spec))
            : Discovered<MyModel>.Fail(DiagnosticInfo.Create(Rules.NotPartial, type.Locations.FirstOrDefault(), type.Name));
    });
// One file for all models: a hint name derived from a type name can outgrow the path limit.
context.ProduceCode(discovered.Models().Collect().Select(static Producer (m, ct) => new MyProducer(m.ToEquatableArray())));
context.ReportDiagnostics(discovered.Diagnostics());
```

## Staying incremental
A generator that emits the same output on every keystroke is still doing the work on every keystroke. The pieces above are built so that an edit your generator does not care about stops at an equal model:

- `ProduceCode(...)` gives each producer its own output step and never touches the `Compilation`, so a file is regenerated only when its own producer's input changed. Since 11.0.0; before that every producer was combined with the `Compilation` and re-ran on every edit. It takes any number of single providers (`IncrementalValueProvider<Producer>`, one file each) or, since 12.0.0, any number of multi-value providers (`IncrementalValuesProvider<Producer>`, one file per producer).
- Models compare by value under the default comparer: generated by `[GenerateEquality]`, or written by hand with `ValueEquality`. A step that builds a new collection returns `EquatableArray<T>`.
- `ReportDiagnostics(...)` needs the `Compilation` to rebuild in-tree locations, which `#pragma` and `.editorconfig` severity depend on. Implement `IConditionalDiagnosticReporter` and a reporter with nothing to report runs no step at all.

Rules of thumb for your models: no `ISymbol`, `SyntaxNode`, `SyntaxTokenList`, `Location`, `Compilation` or `DiagnosticDescriptor` (use strings, `LocationKey` and `DiagnosticInfo`; `MINT001` enforces it on `[GenerateEquality]` models); return `EquatableArray<T>` from every step that builds a collection; and carry a `LocationKey` only on a model that will actually report a diagnostic, so that typing above a declaration does not invalidate it.

To prove it, `MintPlayer.SourceGenerators.TestHarness` runs a generator twice and reports the run reason of every output step.

## Breaking changes in 12.0.0
The full list, with a migration guide, is in the [changelog](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/SourceGenerators/CHANGELOG.md).

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