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
                        // Classes with attributes on fields, properties, or methods (existing behavior)
                        classDeclaration.Members.OfType<FieldDeclarationSyntax>()
                            .Any(f => f.AttributeLists.SelectMany(a => a.Attributes).Any()) ||
                        classDeclaration.Members.OfType<PropertyDeclarationSyntax>()
                            .Any(p => p.AttributeLists.SelectMany(a => a.Attributes).Any()) ||
                        classDeclaration.Members.OfType<MethodDeclarationSyntax>()
                            .Any(m => m.AttributeLists.SelectMany(a => a.Attributes).Any()) ||
                        // Partial classes that extend another class (may need base dependency forwarding)
                        (classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)) &&
                         classDeclaration.BaseList != null)
                    ),
                static (context2, ct) =>
                {
                    if (context2.Node is ClassDeclarationSyntax classDeclaration)
                    {
                        var className = classDeclaration.Identifier.Text;

                        // Get dependencies for the current class (fields + get-only properties)
                        var injectFields = GetInjectMembers(classDeclaration, context2.SemanticModel);

                        // Traverse the inheritance hierarchy to collect dependencies from base classes
                        // (do this before PostConstruct check so we know if base dependencies exist)
                        var baseDependencies = new List<Models.InjectField>();
                        var currentType = context2.SemanticModel.GetDeclaredSymbol(classDeclaration);

                        if (currentType is INamedTypeSymbol currentTypeSymbol)
                        {
                            var pathSpec = currentTypeSymbol.GetPathSpec(ct);

                            while (currentTypeSymbol?.BaseType != null && currentTypeSymbol.BaseType.SpecialType != SpecialType.System_Object)
                            {
                                var baseTypeSyntax = currentTypeSymbol.BaseType.DeclaringSyntaxReferences
                                    .FirstOrDefault()?.GetSyntax();

                                if (baseTypeSyntax is ClassDeclarationSyntax baseClassDeclarationSyntax)
                                {
                                    // First, try to get [Inject] members from the base class
                                    var baseInjectMembers = GetInjectMembers(baseClassDeclarationSyntax, context2.SemanticModel.Compilation.GetSemanticModel(baseTypeSyntax.SyntaxTree));

                                    if (baseInjectMembers.Count > 0)
                                    {
                                        // Base class has [Inject] members - use those
                                        baseDependencies.AddRange(baseInjectMembers);
                                    }
                                    else
                                    {
                                        // No [Inject] members - check for constructor parameters
                                        var constructorParams = GetConstructorParameters(currentTypeSymbol.BaseType);
                                        baseDependencies.AddRange(constructorParams);
                                    }
                                }
                                else
                                {
                                    // Base class syntax not available (e.g., from external assembly)
                                    // Fall back to checking constructor parameters via symbol
                                    var constructorParams = GetConstructorParameters(currentTypeSymbol.BaseType);
                                    baseDependencies.AddRange(constructorParams);
                                }

                                currentTypeSymbol = currentTypeSymbol.BaseType;
                            }

                            // Get PostConstruct method info (pass whether class has any dependencies)
                            var hasDependencies = injectFields.Count > 0 || baseDependencies.Count > 0;
                            var (postConstructMethodName, diagnostics) = GetPostConstructMethod(classDeclaration, context2.SemanticModel, className, hasDependencies);

                            return new Models.ClassWithBaseDependenciesAndInjectFields
                            {
                                FileName = classDeclaration.Identifier.Text,
                                ClassName = className,
                                ClassNamespace = pathSpec?.ContainingNamespace,
                                PathSpec = pathSpec,
                                BaseDependencies = baseDependencies,
                                InjectFields = injectFields,
                                PostConstructMethodName = postConstructMethodName,
                                Diagnostics = diagnostics,
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

        var classesDiagnosticProvider = classesProvider
            .Join(settingsProvider)
            .Select(static IDiagnosticReporter (providers, ct) => new InjectProducer(providers.Item1.NotNull(), providers.Item2.RootNamespace!));

        // Combine all source providers
        context.ProduceCode(classesSourceProvider);
        context.ReportDiagnostics(classesDiagnosticProvider);
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

    /// <summary>
    /// Gets constructor parameters from a type symbol.
    /// Used when the base class has no [Inject] members but has a constructor requiring parameters.
    /// </summary>
    private static List<Models.InjectField> GetConstructorParameters(INamedTypeSymbol typeSymbol)
    {
        var result = new List<Models.InjectField>();

        // Find the constructor with parameters (prefer the one with most parameters if multiple exist)
        // Skip parameterless constructors as they don't need forwarding
        var constructor = typeSymbol.Constructors
            .Where(c => !c.IsStatic && c.DeclaredAccessibility != Accessibility.Private)
            .Where(c => c.Parameters.Length > 0)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        if (constructor != null)
        {
            foreach (var param in constructor.Parameters)
            {
                var fqn = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters));
                result.Add(new Models.InjectField { Type = fqn, Name = param.Name });
            }
        }

        return result;
    }

    private static (string? MethodName, List<Models.PostConstructDiagnostic> Diagnostics) GetPostConstructMethod(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        string className,
        bool hasDependencies)
    {
        var diagnostics = new List<Models.PostConstructDiagnostic>();
        var postConstructMethods = new List<(string Name, MethodDeclarationSyntax Syntax)>();

        foreach (var method in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            var hasPostConstructAttribute = method.AttributeLists
                .SelectMany(attrs => attrs.Attributes)
                .Any(attr => semanticModel.GetTypeInfo(attr).Type?.Name == "PostConstructAttribute");

            if (!hasPostConstructAttribute)
                continue;

            var methodName = method.Identifier.Text;
            var location = method.Identifier.GetLocation().AsKey();

            // Check if method is static (INJECT003)
            if (method.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                diagnostics.Add(new Models.PostConstructDiagnostic
                {
                    Rule = DiagnosticRules.PostConstructCannotBeStatic,
                    Location = location,
                    MessageArgs = [methodName]
                });
                continue;
            }

            // Check if method has parameters (INJECT001)
            if (method.ParameterList.Parameters.Count > 0)
            {
                diagnostics.Add(new Models.PostConstructDiagnostic
                {
                    Rule = DiagnosticRules.PostConstructMustBeParameterless,
                    Location = location,
                    MessageArgs = [methodName]
                });
                continue;
            }

            postConstructMethods.Add((methodName, method));
        }

        // Check for multiple PostConstruct methods (INJECT002)
        if (postConstructMethods.Count > 1)
        {
            foreach (var (name, syntax) in postConstructMethods)
            {
                diagnostics.Add(new Models.PostConstructDiagnostic
                {
                    Rule = DiagnosticRules.OnlyOnePostConstructAllowed,
                    Location = syntax.Identifier.GetLocation().AsKey(),
                    MessageArgs = [className]
                });
            }
            return (null, diagnostics);
        }

        // Check if class has no dependencies (inject fields or base dependencies) but has PostConstruct (INJECT004)
        if (postConstructMethods.Count == 1 && !hasDependencies)
        {
            var (name, syntax) = postConstructMethods[0];
            diagnostics.Add(new Models.PostConstructDiagnostic
            {
                Rule = DiagnosticRules.PostConstructRequiresInject,
                Location = syntax.Identifier.GetLocation().AsKey(),
                MessageArgs = [name, className]
            });
        }

        return (postConstructMethods.FirstOrDefault().Name, diagnostics);
    }
}
