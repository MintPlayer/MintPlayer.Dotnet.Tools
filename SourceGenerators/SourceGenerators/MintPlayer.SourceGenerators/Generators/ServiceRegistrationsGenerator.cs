using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public class ServiceRegistrationsGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider, IncrementalValueProvider<ICompilationCache> cacheProvider)
    {
        var classesWithRegisterAttributeProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, ct) => node is ClassDeclarationSyntax,
                static (context2, ct) =>
                {
                    var serviceLifetimeSymbol = context2.SemanticModel.Compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceLifetime");
                    var generatedAccessibilitySymbol = context2.SemanticModel.Compilation.GetTypeByMetadataName("MintPlayer.SourceGenerators.Attributes.EGeneratedAccessibility");

                    if (context2.Node is ClassDeclarationSyntax classDeclaration && serviceLifetimeSymbol is not null)
                    {
                        if (context2.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol namedTypeSymbol)
                        {
                            var attrs = namedTypeSymbol.GetAttributes().Where(a => a.AttributeClass?.Name == nameof(RegisterAttribute)).ToArray();
                            if (attrs.Length == 0) return default;

                            var factories = namedTypeSymbol.GetMembers()
                                .OfType<IMethodSymbol>()
                                .Where(m => m.IsStatic && m.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(RegisterFactoryAttribute)))
                                .Select(m => new { m.Name, ReturnType = m.ReturnType as INamedTypeSymbol })
                                .Where(m => m.ReturnType is not null)
                                .ToArray();

                            return attrs.Select(attr =>
                            {
                                var formalParamCount = attr.AttributeConstructor?.Parameters.Length ?? 0; // includes optional params
                                var args = attr.ConstructorArguments; // supplied values only

                                // New constructor forms have 3 (no interface) or 4 (with interface) formal params.
                                if (formalParamCount is 3 or 4)
                                {
                                    var firstFormal = attr.AttributeConstructor!.Parameters[0].Type;
                                    if (SymbolEqualityComparer.Default.Equals(firstFormal, serviceLifetimeSymbol))
                                    {
                                        // (ServiceLifetime lifetime, string methodNameHint = default, GeneratedAccessibility accessibility = default)
                                        if (args.Length < 1) return default;
                                        var lifetime = (ServiceLifetime)args[0].Value!;
                                        var methodNameHint = args.Length >= 2 ? args[1].Value as string : null;
                                        var accessibility = args.Length >= 3 && generatedAccessibilitySymbol is not null && SymbolEqualityComparer.Default.Equals(args[2].Type, generatedAccessibilitySymbol) && args[2].Value is int accInt
                                            ? (EGeneratedAccessibility)accInt
                                            : EGeneratedAccessibility.Unspecified;

                                        return new ServiceRegistration
                                        {
                                            ServiceTypeName = null,
                                            ImplementationTypeName = namedTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                            Lifetime = lifetime,
                                            MethodNameHint = methodNameHint,
                                            Accessibility = accessibility,
                                            FactoryNames = factories.Where(f => SymbolEqualityComparer.Default.Equals(f.ReturnType, namedTypeSymbol)).Select(f => f.Name).ToArray(),
                                        };
                                    }
                                    else if (args.ElementAtOrDefault(0).Value is INamedTypeSymbol interfaceTypeSymbol)
                                    {
                                        // (Type interfaceType, ServiceLifetime lifetime, string methodNameHint = default, GeneratedAccessibility accessibility = default)
                                        if (args.Length < 2) return default;
                                        var lifetimeArg = args[1];
                                        if (!SymbolEqualityComparer.Default.Equals(lifetimeArg.Type, serviceLifetimeSymbol)) return default;
                                        var lifetime = (ServiceLifetime)lifetimeArg.Value!;
                                        var methodNameHint = args.Length >= 3 ? args[2].Value as string : null;
                                        var accessibility = args.Length >= 4 && generatedAccessibilitySymbol is not null && SymbolEqualityComparer.Default.Equals(args[3].Type, generatedAccessibilitySymbol) && args[3].Value is int accInt2
                                            ? (EGeneratedAccessibility)accInt2
                                            : EGeneratedAccessibility.Unspecified;

                                        if (namedTypeSymbol.AllInterfaces.All(i => !SymbolEqualityComparer.Default.Equals(i, interfaceTypeSymbol))) return default;

                                        return new ServiceRegistration
                                        {
                                            ServiceTypeName = interfaceTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                            ImplementationTypeName = namedTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                            Lifetime = lifetime,
                                            MethodNameHint = methodNameHint,
                                            Accessibility = accessibility,
                                            FactoryNames = factories.Where(f => SymbolEqualityComparer.Default.Equals(f.ReturnType, interfaceTypeSymbol)).Select(f => f.Name).ToArray(),
                                        };
                                    }
                                }
                                return default;
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
            .Select(static Producer (providers, ct) => new RegistrationsProducer(providers.Item1.AsEnumerable(), providers.Item2.RootNamespace!));

        context.ProduceCode(registerAttributeSourceProvider);
    }
}
