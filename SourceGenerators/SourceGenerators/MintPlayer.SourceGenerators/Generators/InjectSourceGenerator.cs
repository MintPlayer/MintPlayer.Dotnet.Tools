using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

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
                    (
                        classDeclaration.Members.OfType<FieldDeclarationSyntax>()
                            .Any(f => f.AttributeLists.SelectMany(a => a.Attributes).Any()) ||
                        classDeclaration.Members.OfType<PropertyDeclarationSyntax>()
                            .Any(p => p.AttributeLists.SelectMany(a => a.Attributes).Any())
                    ),
                static (context2, ct) =>
                {
                    if (context2.Node is ClassDeclarationSyntax classDeclaration)
                    {
                        var className = classDeclaration.Identifier.Text; // PASS ON

                        // Get dependencies for the current class (fields + get-only properties)
                        var injectFields = GetInjectMembers(classDeclaration, context2.SemanticModel); // PASS ON

                        // Traverse the inheritance hierarchy to collect dependencies from base classes
                        var baseDependencies = new List<Models.InjectField>(); // PASS ON
                        var currentType = context2.SemanticModel.GetDeclaredSymbol(classDeclaration);

                        if (currentType is INamedTypeSymbol currentTypeSymbol)
                        {
                            var pathSpec = currentTypeSymbol.GetPathSpec(ct);

                            while (currentTypeSymbol?.BaseType != null && currentTypeSymbol.BaseType.SpecialType != SpecialType.System_Object)
                            {
                                var baseTypeSyntax = currentTypeSymbol.BaseType.DeclaringSyntaxReferences
                                    .FirstOrDefault()?.GetSyntax();

                                if (baseTypeSyntax is ClassDeclarationSyntax baseClassDeclationSyntax)
                                    baseDependencies.AddRange(GetInjectMembers(baseClassDeclationSyntax, context2.SemanticModel.Compilation.GetSemanticModel(baseTypeSyntax.SyntaxTree)));

                                currentTypeSymbol = currentTypeSymbol.BaseType;
                            }

                            return new Models.ClassWithBaseDependenciesAndInjectFields
                            {
                                FileName = classDeclaration.Identifier.Text,
                                ClassName = className,
                                ClassNamespace = pathSpec?.ContainingNamespace,
                                PathSpec = pathSpec,
                                BaseDependencies = baseDependencies,
                                InjectFields = injectFields,
                            };
                        }
                    }

                    return default;
                }
            )
            .Collect();

        var classesSourceProvider = classesProvider
            .Join(settingsProvider)
            .Select(static Producer (providers, ct) => new InjectProducer(providers.Item1.NotNull(), providers.Item2.RootNamespace!));

        // Combine all source providers
        context.ProduceCode(classesSourceProvider);
    }


    private static List<Models.InjectField> GetInjectMembers(ClassDeclarationSyntax classDeclaration, SemanticModel semanticModel)
    {
        var result = new List<Models.InjectField>();

        // Fields
        foreach (var field in classDeclaration.Members.OfType<FieldDeclarationSyntax>())
        {
            if (field.AttributeLists.SelectMany(attrs => attrs.Attributes)
                .Any(attr => semanticModel.GetTypeInfo(attr).Type?.Name == "InjectAttribute"))
            {
                var fieldType = field.Declaration.Type;
                var fieldTypeSymbol = semanticModel.GetSymbolInfo(fieldType).Symbol as ITypeSymbol;
                var fqn = fieldTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)) ?? string.Empty;
                var name = field.Declaration.Variables.First().Identifier.Text;
                result.Add(new Models.InjectField { Type = fqn, Name = name });
            }
        }

        // Get-only properties
        foreach (var prop in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (prop.AttributeLists.SelectMany(attrs => attrs.Attributes)
                .Any(attr => semanticModel.GetTypeInfo(attr).Type?.Name == "InjectAttribute"))
            {
                // Ensure no set accessor (get-only, including expression-bodied)
                var hasSetter = prop.AccessorList?.Accessors.Any(a => a.Kind() == SyntaxKind.SetAccessorDeclaration) == true;
                if (hasSetter) continue;

                var propTypeSymbol = semanticModel.GetSymbolInfo(prop.Type).Symbol as ITypeSymbol;
                var fqn = propTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)) ?? string.Empty;
                var name = prop.Identifier.Text;
                result.Add(new Models.InjectField { Type = fqn, Name = name });
            }
        }

        return result;
    }
}
