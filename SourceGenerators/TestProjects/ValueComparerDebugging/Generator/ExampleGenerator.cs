using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.ValueComparerGenerator.Attributes;

namespace ValueComparerDebugging.Analyzers;

internal class ExampleGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, ct) => true,
                transform: static (ctx, ct) =>
                {
                    if (ctx.Node is ClassDeclarationSyntax classDeclaration &&
                        ctx.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol classSymbol)
                    {
                        return new ClassDeclaration
                        {
                            Name = classSymbol.Name,
                            Namespace = classSymbol.ContainingNamespace.ToDisplayString()
                        };
                    };
                    return default;
                })
                .WithNullableComparer()
                //.WithComparer(ComparerRegistry.For<ClassDeclaration>())
                .Collect();

            
        context.RegisterSourceOutput(
            classProvider.Select((classes, ct) => classes.Select(c => $"partial class {c.Name}")),
            (context, items) => string.Join(Environment.NewLine, items)
        );
    }
}

[AutoValueComparer]
public partial class ClassDeclaration
{
    public string? Name { get; set; }
    public string? Namespace { get; set; }
    //public Location Location { get; set; }
    //public INamedTypeSymbol? Symbol { get; set; }
}
