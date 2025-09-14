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
            .WithComparer();

        var typesToMapSourceProvider = typesToMapProvider
            .Select(static Producer (p, ct) => new MapperProducer(p));

        // Pass your source-providers to the ready-made ProduceCode extension method 
        context.ProduceCode(typesToMapSourceProvider);
    }

    public override void RegisterComparers()
    {
        // Use this if you're using libraries like Newtonsoft.Json
        // Make sure to use a Mutex.
        // An example is available in NewtonsoftJsonComparers.Register()
    }
}
```

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
If you install [this package](https://www.nuget.org/packages/MintPlayer.ValueComparerGenerator) too in your project, you can have your value-comparers automatically generated for you.

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

This generator will generate a value-comparer and a `.WithComparer()` and `.WithNullableComparer()` extension method for you.