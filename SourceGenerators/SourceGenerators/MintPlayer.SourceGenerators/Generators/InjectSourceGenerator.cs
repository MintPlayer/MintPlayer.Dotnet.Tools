using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;
using MintPlayer.SourceGenerators.Tools.Extensions;

namespace MintPlayer.SourceGenerators.Generators;

[Generator(LanguageNames.CSharp)]
public class InjectSourceGenerator : IncrementalGenerator
{
    //public override void RegisterComparers()
    //{
    //    NewtonsoftJsonComparers.Register();
    //}

    // Measure performance of the Analyzer
    // https://www.meziantou.net/measuring-performance-of-roslyn-source-generators.htm
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider, IncrementalValueProvider<ICompilationCache> cacheProvider)
    {
        var classesProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, ct) => node is ClassDeclarationSyntax classDeclaration &&
                    classDeclaration.Members.OfType<FieldDeclarationSyntax>()
                        .Any(f => f.AttributeLists.SelectMany(a => a.Attributes).Any()),
                static (context2, ct) =>
                {
                    if (context2.Node is ClassDeclarationSyntax classDeclaration)
                    {
                        var className = classDeclaration.Identifier.Text; // PASS ON

                        // Get dependencies for the current class
                        var injectFields = GetInjectFields(classDeclaration, context2.SemanticModel); // PASS ON

                        // Traverse the inheritance hierarchy to collect dependencies from base classes
                        var baseDependencies = new List<Models.InjectField>(); // PASS ON
                        var currentType = context2.SemanticModel.GetDeclaredSymbol(classDeclaration);

                        if (currentType is INamedTypeSymbol currentTypeSymbol)
                        {
                            while (currentTypeSymbol?.BaseType != null && currentTypeSymbol.BaseType.SpecialType != SpecialType.System_Object)
                            {
                                var baseTypeSyntax = currentTypeSymbol.BaseType.DeclaringSyntaxReferences
                                    .FirstOrDefault()?.GetSyntax();

                                if (baseTypeSyntax is ClassDeclarationSyntax baseClassDeclationSyntax)
                                    baseDependencies.AddRange(GetInjectFields(baseClassDeclationSyntax, context2.SemanticModel.Compilation.GetSemanticModel(baseTypeSyntax.SyntaxTree)));

                                currentTypeSymbol = currentTypeSymbol.BaseType;
                            }

                            if (context2.SemanticModel.GetDeclaredSymbol(classDeclaration) is INamedTypeSymbol classSymbol)
                            {
                                return new Models.ClassWithBaseDependenciesAndInjectFields
                                {
                                    FileName = classDeclaration.Identifier.Text,
                                    ClassName = className,
                                    ClassNamespace = classSymbol.ContainingNamespace.ToDisplayString(),
                                    BaseDependencies = baseDependencies,
                                    InjectFields = injectFields,
                                    PathSpec = classSymbol.GetPathSpec(),
                                };
                            }
                        }
                    }

                    return default;
                }
            )
            .Where(static c => c != null)
            .Select(static (c, ct) => c!)
            .WithComparer()
            .Collect()
            .SelectMany(static (classes, ct) => classes
                .GroupBy(c => c.ClassNamespace)
                .Select(cg => new Models.ClassesByNamespace { Namespace = cg.Key, Classes = cg.ToArray() }))
            .WithComparer()
            .Collect();

        var classesSourceProvider = classesProvider
            .Join(settingsProvider)
            .Select(static Producer (providers, ct) => new InjectProducer(providers.Item1.NotNull(), providers.Item2.RootNamespace!));

        // Combine all source providers
        context.ProduceCode(classesSourceProvider);
    }


    private static List<Models.InjectField> GetInjectFields(ClassDeclarationSyntax classDeclaration, SemanticModel semanticModel)
    {
        return classDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .Where(field => field.AttributeLists
                .SelectMany(attrs => attrs.Attributes)
                .Any(attr => semanticModel.GetTypeInfo(attr).Type?.Name == "InjectAttribute"))
            .Select(field =>
            {
                var fieldType = field.Declaration.Type;
                var fieldTypeSymbol = semanticModel.GetSymbolInfo(fieldType).Symbol as ITypeSymbol;

                var fqn = fieldTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)) ?? string.Empty;
                var name = field.Declaration.Variables.First().Identifier.Text;
                return new Models.InjectField { Type = fqn, Name = name };
            })
            .ToList();
    }
}
