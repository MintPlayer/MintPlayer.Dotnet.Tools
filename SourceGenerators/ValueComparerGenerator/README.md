# Value-comparer generator
This library contains source-generators to simplify writing your own source-generators.

## Getting started
You need to install both [`MintPlayer.ValueComparerGenerator`](https://nuget.org/packages/MintPlayer.ValueComparerGenerator) and [`MintPlayer.ValueComparerGenerator`](https://nuget.org/packages/MintPlayer.ValueComparerGenerator.Attributes) packages in your project.

## Example
Note that the class (and parent classes) must be partial.

```csharp
using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Models;

public partial class Context
{
    public partial class Models
    {
        [AutoValueComparer]
        public partial class Person
        {
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public List<Address> Addresses { get; set; } = [];
        }

        [AutoValueComparer]
        public partial class Address
        {
            public string Street { get; set; } = string.Empty;
            public int Number { get; set; }
            public string City { get; set; } = string.Empty;
            public int PostalCode { get; set; }

            [ComparerIgnore]
            public string Text { get; set; }
        }
    }
}
```

## WithComparer
The source generator also generates a `.WithComparer()` and `.WithNullableComparer()` extension method that doesn't require parameters. This allows you to minimize your source-generator code like this:

```csharp
[Generator(LanguageNames.CSharp)]
public class MapperGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var typesToMapProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "MintPlayer.Mapper.Attributes.GenerateMapperAttribute",
                static (node, ct) => node is not null,
                static (ctx, ct) =>
                {
                    if (ctx.SemanticModel.GetDeclaredSymbol(ctx.TargetNode, ct) is INamedTypeSymbol typeSymbol &&
                        ctx.Attributes.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.GenerateMapperAttribute") is { ConstructorArguments.Length: > 0 } attr &&
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
            .WithComparer(); // <-- No need to pass in the reference to the value-comparer

        var typesToMapSourceProvider = typesToMapProvider
            .Select(static Producer (p, ct) => new MapperProducer(p));

        context.ProduceCode(typesToMapSourceProvider);
    }

    public override void RegisterComparers()
    {
    }
}
```