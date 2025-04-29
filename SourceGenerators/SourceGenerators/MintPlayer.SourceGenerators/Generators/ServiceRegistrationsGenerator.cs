using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparers.NewtonsoftJson;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public class ServiceRegistrationsGenerator : IncrementalGenerator
{
    public override void RegisterComparers()
    {
        NewtonsoftJsonComparers.Register();
    }

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var classesWithRegisterAttributeProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, ct) => node is ClassDeclarationSyntax,
                static (context2, ct) =>
                {
                    if (context2.Node is ClassDeclarationSyntax classDeclaration)
                    {
                        var classSymbol = context2.SemanticModel.GetDeclaredSymbol(classDeclaration, ct);
                        if (classSymbol is INamedTypeSymbol namedTypeSymbol)
                        {
                            var attr = classSymbol.GetAttributes()
                                .FirstOrDefault(a => a.AttributeClass?.Name == nameof(RegisterAttribute));

                            if (attr is null) return default;

                            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol interfaceSymbol) return default;

                            // Verify that the class implements the interface
                            if (namedTypeSymbol.AllInterfaces.All(i => !SymbolEqualityComparer.Default.Equals(i, interfaceSymbol))) return default;

                            return new ServiceRegistration
                            {
                                ServiceTypeName = interfaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                ImplementationTypeName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                Lifetime = (ServiceLifetime)attr.ConstructorArguments[1].Value!,
                                MethodNameHint = (string?)attr.ConstructorArguments[2].Value ?? string.Empty,
                            };
                        }
                    }

                    return default;
                }
            )
            .WithComparer()
            .Collect();

        var registerAttributeSourceProvider = classesWithRegisterAttributeProvider
            .Combine(settingsProvider)
            .Select(static Producer (providers, ct) => new Producers.RegistrationsProducer(providers.Left.NotNull(), providers.Right.RootNamespace!));

        // Combine all source providers
        context.ProduceCode(registerAttributeSourceProvider);
    }
}
