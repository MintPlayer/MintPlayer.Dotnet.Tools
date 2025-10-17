using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
//using MintPlayer.ValueComparers.NewtonsoftJson;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public class ServiceRegistrationsGenerator : IncrementalGenerator
{
    //public override void RegisterComparers()
    //{
    //    NewtonsoftJsonComparers.Register();
    //}

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var classesWithRegisterAttributeProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, ct) => node is ClassDeclarationSyntax,
                static (context2, ct) =>
                {
                    var serviceLifetimeSymbol = context2.SemanticModel.Compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceLifetime");

                    if (context2.Node is ClassDeclarationSyntax classDeclaration)
                    {
                        var classSymbol = context2.SemanticModel.GetDeclaredSymbol(classDeclaration, ct);
                        if (classSymbol is INamedTypeSymbol namedTypeSymbol)
                        {
                            var attrs = classSymbol.GetAttributes()
                                .Where(a => a.AttributeClass?.Name == nameof(RegisterAttribute))
                                .ToArray();

                            if (attrs.Length is 0) return default;

                            var factories = namedTypeSymbol.GetMembers()
                                .OfType<IMethodSymbol>()
                                .Where(m => m.IsStatic && m.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(RegisterFactoryAttribute)))
                                .Select(m => new
                                {
                                    m.Name,
                                    ReturnType = m.ReturnType as INamedTypeSymbol,
                                })
                                .Where(m => m.ReturnType is not null)
                                .ToArray();

                            return attrs.Select(attr =>
                            {
                                if (attr.AttributeConstructor?.Parameters.Length == 2)
                                {
                                    if (!SymbolEqualityComparer.Default.Equals(attr.ConstructorArguments[0].Type, serviceLifetimeSymbol)) return default;
                                    if (attr.ConstructorArguments[1].Value is not string methodNameHint) return default;

                                    return new ServiceRegistration
                                    {
                                        ServiceTypeName = null,
                                        ImplementationTypeName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                        Lifetime = (ServiceLifetime)attr.ConstructorArguments[0].Value!,
                                        MethodNameHint = methodNameHint,
                                        FactoryNames = factories
                                            .Where(f => SymbolEqualityComparer.Default.Equals(f.ReturnType, namedTypeSymbol))
                                            .Select(f => f.Name)
                                            .ToArray(),
                                    };
                                }
                                else if (attr.AttributeConstructor?.Parameters.Length is 3 or 4)
                                {
                                    if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol interfaceTypeSymbol) return default;
                                    if (!SymbolEqualityComparer.Default.Equals(attr.ConstructorArguments[1].Type, serviceLifetimeSymbol)) return default;
                                    var methodNameHint = attr.ConstructorArguments[2].Value as string;

                                    // Verify that the class implements the interface
                                    if (namedTypeSymbol.AllInterfaces.All(i => !SymbolEqualityComparer.Default.Equals(i, interfaceTypeSymbol))) return default;

                                    return new ServiceRegistration
                                    {
                                        ServiceTypeName = interfaceTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                        ImplementationTypeName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                        Lifetime = (ServiceLifetime)attr.ConstructorArguments[1].Value!,
                                        MethodNameHint = methodNameHint,
                                        FactoryNames = factories
                                            .Where(f => SymbolEqualityComparer.Default.Equals(f.ReturnType, interfaceTypeSymbol))
                                            .Select(f => f.Name)
                                            .ToArray(),
                                    };
                                }
                                else
                                {
                                    return default;
                                }
                            })
                            .NotNull()
                            .ToArray();
                        }
                    }

                    return default;
                }
            )
            .SelectMany((x, ct) => x)
            .Collect();

        var registerAttributeSourceProvider = classesWithRegisterAttributeProvider
            .Join(settingsProvider)
            .Select(static Producer (providers, ct) => new RegistrationsProducer(providers.Item1.NotNull(), providers.Item2.RootNamespace!));

        // Combine all source providers
        context.ProduceCode(registerAttributeSourceProvider);
    }
}
