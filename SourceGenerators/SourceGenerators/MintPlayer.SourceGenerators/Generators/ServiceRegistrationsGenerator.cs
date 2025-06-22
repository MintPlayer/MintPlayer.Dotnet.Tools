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

                            var serviceLifetimeSymbol = context2.SemanticModel.Compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceLifetime");
                            if (attr.AttributeConstructor?.Parameters.Length == 2)
                            {
                                if (!SymbolEqualityComparer.Default.Equals(attr.ConstructorArguments[0].Type, serviceLifetimeSymbol)) return default;
                                if (attr.ConstructorArguments[1].Value is not string methodNameHint) return default;

                                return new ServiceRegistration
                                {
                                    ServiceTypeName = null,
                                    ImplementationTypeName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    Lifetime = (ServiceLifetime)attr.ConstructorArguments[1].Value!,
                                    MethodNameHint = methodNameHint,
                                };
                            }
                            else if (attr.AttributeConstructor?.Parameters.Length == 3)
                            {
                                if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol interfaceTypeSymbol) return default;
                                if (!SymbolEqualityComparer.Default.Equals(attr.ConstructorArguments[1].Type, serviceLifetimeSymbol)) return default;
                                if (attr.ConstructorArguments[2].Value is not string methodNameHint) return default;

                                // Verify that the class implements the interface
                                if (namedTypeSymbol.AllInterfaces.All(i => !SymbolEqualityComparer.Default.Equals(i, interfaceTypeSymbol))) return default;
                                
                                return new ServiceRegistration
                                {
                                    ServiceTypeName = interfaceTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    ImplementationTypeName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    Lifetime = (ServiceLifetime)attr.ConstructorArguments[1].Value!,
                                    MethodNameHint = methodNameHint,
                                };
                            }
                        }
                    }

                    return default;
                }
            )
            .WithComparer()
            .Collect();

        var registerAttributeSourceProvider = classesWithRegisterAttributeProvider
            .Join(settingsProvider)
            .Select(static Producer (providers, ct) => new RegistrationsProducer(providers.Item1.NotNull(), providers.Item2.RootNamespace!));

        // Combine all source providers
        context.ProduceCode(registerAttributeSourceProvider);
    }
}
