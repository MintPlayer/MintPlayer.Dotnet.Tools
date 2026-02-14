using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Models;
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
                         classDeclaration.BaseList is { })
                    ),
                static (context2, ct) =>
                {
                    if (context2.Node is ClassDeclarationSyntax classDeclaration)
                    {
                        var className = classDeclaration.Identifier.Text;
                        var configDiagnostics = new List<ConfigDiagnostic>();

                        // Check if class is partial (required for code generation)
                        var isPartial = classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

                        // Get dependencies for the current class (fields + get-only properties)
                        var injectFields = GetInjectMembers(classDeclaration, context2.SemanticModel);

                        // Get config fields ([Config], [ConnectionString], [Options])
                        var (configFields, connectionStringFields, optionsFields, cfgDiagnostics) =
                            GetConfigMembers(classDeclaration, context2.SemanticModel, className, isPartial);
                        configDiagnostics.AddRange(cfgDiagnostics);

                        // Check for IConfiguration in inject fields (for deduplication)
                        var (hasExplicitIConfig, explicitIConfigName) = DetectExplicitIConfiguration(injectFields);

                        // Traverse the inheritance hierarchy to collect dependencies from base classes
                        // (do this before PostConstruct check so we know if base dependencies exist)
                        var baseDependencies = new List<Models.InjectField>();
                        var currentType = context2.SemanticModel.GetDeclaredSymbol(classDeclaration);

                        if (currentType is INamedTypeSymbol currentTypeSymbol)
                        {
                            var pathSpec = currentTypeSymbol.GetPathSpec(ct);

                            while (currentTypeSymbol?.BaseType is { SpecialType: not SpecialType.System_Object })
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
                            var hasDependencies = injectFields.Count > 0 || baseDependencies.Count > 0 ||
                                                  configFields.Count > 0 || connectionStringFields.Count > 0 || optionsFields.Count > 0;
                            var (postConstructMethodName, diagnostics) = GetPostConstructMethod(classDeclaration, context2.SemanticModel, className, hasDependencies);

                            // Check if a constructor with the same signature already exists
                            // If so, skip generation to avoid duplicate constructor error
                            var optionsTypes = optionsFields.Select(o => o.Type);
                            var needsIConfiguration = (configFields.Count > 0 || connectionStringFields.Count > 0) && !hasExplicitIConfig;
                            var autoIConfigType = needsIConfiguration
                                ? new[] { "global::Microsoft.Extensions.Configuration.IConfiguration" }
                                : Array.Empty<string>();

                            var wouldGenerateParams = injectFields
                                .Concat(baseDependencies)
                                .Select(dep => dep.Type)
                                .Concat(optionsTypes)
                                .Concat(autoIConfigType)
                                .Distinct()
                                .ToList();

                            if (wouldGenerateParams.Count > 0 && currentType.HasMatchingConstructor(wouldGenerateParams))
                            {
                                // Class already has a constructor with this signature - skip generation
                                return default;
                            }

                            // Extract generic type parameters and constraints
                            var (genericTypeParams, genericConstraints) = GetGenericTypeInfo(classDeclaration, context2.SemanticModel);

                            return new Models.ClassWithBaseDependenciesAndInjectFields
                            {
                                FileName = classDeclaration.Identifier.Text,
                                ClassName = className,
                                ClassNamespace = pathSpec?.ContainingNamespace,
                                PathSpec = pathSpec,
                                BaseDependencies = baseDependencies,
                                InjectFields = injectFields,
                                ConfigFields = configFields,
                                ConnectionStringFields = connectionStringFields,
                                OptionsFields = optionsFields,
                                PostConstructMethodName = postConstructMethodName,
                                Diagnostics = diagnostics,
                                ConfigDiagnostics = configDiagnostics,
                                HasExplicitIConfiguration = hasExplicitIConfig,
                                ExplicitIConfigurationName = explicitIConfigName,
                                GenericTypeParameters = genericTypeParams,
                                GenericConstraints = genericConstraints,
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
                var isNullable = fieldTypeSymbol?.NullableAnnotation == NullableAnnotation.Annotated
                              || field.Declaration.Type is NullableTypeSyntax;
                result.Add(new Models.InjectField { Type = fqn, Name = name, IsNullable = isNullable });
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
                var isNullable = propTypeSymbol?.NullableAnnotation == NullableAnnotation.Annotated
                              || prop.Type is NullableTypeSyntax;
                result.Add(new Models.InjectField { Type = fqn, Name = name, IsNullable = isNullable });
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

        if (constructor is { Parameters: { } parameters })
        {
            foreach (var param in parameters)
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

    /// <summary>
    /// Extracts generic type parameters and constraints from a class declaration.
    /// </summary>
    /// <param name="classDeclaration">The class declaration syntax.</param>
    /// <param name="semanticModel">The semantic model for resolving types.</param>
    /// <returns>A tuple containing the type parameters (e.g., "&lt;T, TKey&gt;") and constraints (e.g., "where T : class where TKey : IEquatable&lt;TKey&gt;").</returns>
    private static (string? TypeParameters, string? Constraints) GetGenericTypeInfo(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel)
    {
        if (classDeclaration.TypeParameterList is not { Parameters.Count: > 0 })
            return (null, null);

        // Get type parameters string like "<T, TKey>"
        var typeParams = classDeclaration.TypeParameterList.ToString();

        // Get constraints with fully qualified type names
        if (classDeclaration.ConstraintClauses.Count == 0)
            return (typeParams, null);

        var constraintStrings = new List<string>();
        foreach (var constraintClause in classDeclaration.ConstraintClauses)
        {
            var typeParamName = constraintClause.Name.Identifier.Text;
            var constraints = new List<string>();

            foreach (var constraint in constraintClause.Constraints)
            {
                if (constraint is TypeConstraintSyntax typeConstraint)
                {
                    // Get fully qualified type name for the constraint
                    if (semanticModel.GetSymbolInfo(typeConstraint.Type).Symbol is ITypeSymbol typeSymbol)
                    {
                        var fqn = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters));
                        constraints.Add(fqn);
                    }
                    else
                    {
                        // Fallback to the syntax text if symbol resolution fails
                        constraints.Add(typeConstraint.Type.ToString());
                    }
                }
                else if (constraint is ClassOrStructConstraintSyntax classOrStructConstraint)
                {
                    // Handle 'class', 'struct', 'class?', 'notnull', 'unmanaged'
                    constraints.Add(classOrStructConstraint.ToString());
                }
                else if (constraint is ConstructorConstraintSyntax)
                {
                    // Handle 'new()'
                    constraints.Add("new()");
                }
                else if (constraint is DefaultConstraintSyntax)
                {
                    // Handle 'default'
                    constraints.Add("default");
                }
                else
                {
                    // Fallback for any other constraint type
                    constraints.Add(constraint.ToString());
                }
            }

            if (constraints.Count > 0)
            {
                constraintStrings.Add($"where {typeParamName} : {string.Join(", ", constraints)}");
            }
        }

        var constraintsResult = constraintStrings.Count > 0
            ? string.Join(" ", constraintStrings)
            : null;

        return (typeParams, constraintsResult);
    }

    /// <summary>
    /// Detects if IConfiguration is explicitly injected via [Inject] attribute.
    /// </summary>
    private static (bool HasExplicit, string? FieldName) DetectExplicitIConfiguration(List<Models.InjectField> injectFields)
    {
        foreach (var field in injectFields)
        {
            if (field.Type is not null &&
                (field.Type.EndsWith("IConfiguration") ||
                 field.Type == "global::Microsoft.Extensions.Configuration.IConfiguration" ||
                 field.Type == "Microsoft.Extensions.Configuration.IConfiguration"))
            {
                return (true, field.Name);
            }
        }
        return (false, null);
    }

    /// <summary>
    /// Extracts [Config], [ConnectionString], and [Options] members from a class.
    /// </summary>
    private static (List<ConfigField> ConfigFields, List<ConnectionStringField> ConnectionStringFields, List<OptionsField> OptionsFields, List<ConfigDiagnostic> Diagnostics)
        GetConfigMembers(ClassDeclarationSyntax classDeclaration, SemanticModel semanticModel, string className, bool isPartial)
    {
        var configFields = new List<ConfigField>();
        var connectionStringFields = new List<ConnectionStringField>();
        var optionsFields = new List<OptionsField>();
        var diagnostics = new List<ConfigDiagnostic>();
        var usedConfigKeys = new HashSet<string>();

        // Process fields
        foreach (var field in classDeclaration.Members.OfType<FieldDeclarationSyntax>())
        {
            if (semanticModel.GetTypeInfo(field.Declaration.Type) is { } typeInfo &&
                typeInfo.Type is { } fieldTypeSymbol)
            {
                // Get type info which preserves nullable annotations
                var fqn = fieldTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)) ?? string.Empty;
                var fieldName = field.Declaration.Variables.First().Identifier.Text;
                var location = field.Declaration.Variables.First().Identifier.GetLocation().AsKey();
                var isNullable = fieldTypeSymbol.NullableAnnotation == NullableAnnotation.Annotated
                              || field.Declaration.Type is NullableTypeSyntax;

                var attributes = field.AttributeLists.SelectMany(a => a.Attributes).ToList();

                var hasInject = attributes.Any(a => semanticModel.GetTypeInfo(a).Type?.Name == "InjectAttribute");
                var configAttr = attributes.FirstOrDefault(a => semanticModel.GetTypeInfo(a).Type?.Name == "ConfigAttribute");
                var connStrAttr = attributes.FirstOrDefault(a => semanticModel.GetTypeInfo(a).Type?.Name == "ConnectionStringAttribute");
                var optionsAttr = attributes.FirstOrDefault(a => semanticModel.GetTypeInfo(a).Type?.Name == "OptionsAttribute");

                // Validate attribute conflicts
                var attrCount = (configAttr != null ? 1 : 0) + (connStrAttr != null ? 1 : 0) + (optionsAttr != null ? 1 : 0);

                if (configAttr != null)
                {
                    if (!isPartial)
                    {
                        diagnostics.Add(new ConfigDiagnostic
                        {
                            Rule = DiagnosticRules.ConfigNonPartialClass,
                            Location = location,
                            MessageArgs = [className]
                        });
                        continue;
                    }

                    if (hasInject)
                    {
                        diagnostics.Add(new ConfigDiagnostic
                        {
                            Rule = DiagnosticRules.ConfigConflictWithInject,
                            Location = location,
                            MessageArgs = [fieldName]
                        });
                        continue;
                    }

                    if (connStrAttr != null)
                    {
                        diagnostics.Add(new ConfigDiagnostic
                        {
                            Rule = DiagnosticRules.ConfigConflictWithConnectionString,
                            Location = location,
                            MessageArgs = [fieldName]
                        });
                        continue;
                    }

                    var configField = ExtractConfigField(configAttr, fieldName, fqn, isNullable, fieldTypeSymbol, semanticModel, location, diagnostics, className, usedConfigKeys);
                    if (configField != null)
                        configFields.Add(configField);
                }
                else if (connStrAttr != null)
                {
                    if (!isPartial)
                    {
                        diagnostics.Add(new ConfigDiagnostic
                        {
                            Rule = DiagnosticRules.ConfigNonPartialClass,
                            Location = location,
                            MessageArgs = [className]
                        });
                        continue;
                    }

                    if (hasInject)
                    {
                        diagnostics.Add(new ConfigDiagnostic
                        {
                            Rule = DiagnosticRules.ConnectionStringConflictWithInject,
                            Location = location,
                            MessageArgs = [fieldName]
                        });
                        continue;
                    }

                    var connStrField = ExtractConnectionStringField(connStrAttr, fieldName, isNullable, fieldTypeSymbol, semanticModel, location, diagnostics);
                    if (connStrField != null)
                        connectionStringFields.Add(connStrField);
                }
                else if (optionsAttr != null)
                {
                    if (!isPartial)
                    {
                        diagnostics.Add(new ConfigDiagnostic
                        {
                            Rule = DiagnosticRules.ConfigNonPartialClass,
                            Location = location,
                            MessageArgs = [className]
                        });
                        continue;
                    }

                    if (hasInject)
                    {
                        diagnostics.Add(new ConfigDiagnostic
                        {
                            Rule = DiagnosticRules.OptionsConflictWithInject,
                            Location = location,
                            MessageArgs = [fieldName]
                        });
                        continue;
                    }

                    var optionsField = ExtractOptionsField(optionsAttr, fieldName, fqn, fieldTypeSymbol, semanticModel, location, diagnostics);
                    if (optionsField != null)
                        optionsFields.Add(optionsField);
                }
            }
        }

        // Process properties (similar logic)
        foreach (var prop in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            // Get type info which preserves nullable annotations
            var typeInfo = semanticModel.GetTypeInfo(prop.Type);
            var propTypeSymbol = typeInfo.Type;
            var fqn = propTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)) ?? string.Empty;
            var propName = prop.Identifier.Text;
            var location = prop.Identifier.GetLocation().AsKey();
            var isNullable = propTypeSymbol?.NullableAnnotation == NullableAnnotation.Annotated
                          || prop.Type is NullableTypeSyntax;

            var attributes = prop.AttributeLists.SelectMany(a => a.Attributes).ToList();

            var hasInject = attributes.Any(a => semanticModel.GetTypeInfo(a).Type?.Name == "InjectAttribute");
            var configAttr = attributes.FirstOrDefault(a => semanticModel.GetTypeInfo(a).Type?.Name == "ConfigAttribute");
            var connStrAttr = attributes.FirstOrDefault(a => semanticModel.GetTypeInfo(a).Type?.Name == "ConnectionStringAttribute");
            var optionsAttr = attributes.FirstOrDefault(a => semanticModel.GetTypeInfo(a).Type?.Name == "OptionsAttribute");

            if (configAttr != null)
            {
                if (!isPartial)
                {
                    diagnostics.Add(new ConfigDiagnostic
                    {
                        Rule = DiagnosticRules.ConfigNonPartialClass,
                        Location = location,
                        MessageArgs = [className]
                    });
                    continue;
                }

                if (hasInject)
                {
                    diagnostics.Add(new ConfigDiagnostic
                    {
                        Rule = DiagnosticRules.ConfigConflictWithInject,
                        Location = location,
                        MessageArgs = [propName]
                    });
                    continue;
                }

                if (connStrAttr != null)
                {
                    diagnostics.Add(new ConfigDiagnostic
                    {
                        Rule = DiagnosticRules.ConfigConflictWithConnectionString,
                        Location = location,
                        MessageArgs = [propName]
                    });
                    continue;
                }

                var configField = ExtractConfigField(configAttr, propName, fqn, isNullable, propTypeSymbol, semanticModel, location, diagnostics, className, usedConfigKeys);
                if (configField != null)
                    configFields.Add(configField);
            }
            else if (connStrAttr != null)
            {
                if (!isPartial)
                {
                    diagnostics.Add(new ConfigDiagnostic
                    {
                        Rule = DiagnosticRules.ConfigNonPartialClass,
                        Location = location,
                        MessageArgs = [className]
                    });
                    continue;
                }

                if (hasInject)
                {
                    diagnostics.Add(new ConfigDiagnostic
                    {
                        Rule = DiagnosticRules.ConnectionStringConflictWithInject,
                        Location = location,
                        MessageArgs = [propName]
                    });
                    continue;
                }

                var connStrField = ExtractConnectionStringField(connStrAttr, propName, isNullable, propTypeSymbol, semanticModel, location, diagnostics);
                if (connStrField != null)
                    connectionStringFields.Add(connStrField);
            }
            else if (optionsAttr != null)
            {
                if (!isPartial)
                {
                    diagnostics.Add(new ConfigDiagnostic
                    {
                        Rule = DiagnosticRules.ConfigNonPartialClass,
                        Location = location,
                        MessageArgs = [className]
                    });
                    continue;
                }

                if (hasInject)
                {
                    diagnostics.Add(new ConfigDiagnostic
                    {
                        Rule = DiagnosticRules.OptionsConflictWithInject,
                        Location = location,
                        MessageArgs = [propName]
                    });
                    continue;
                }

                var optionsField = ExtractOptionsField(optionsAttr, propName, fqn, propTypeSymbol, semanticModel, location, diagnostics);
                if (optionsField != null)
                    optionsFields.Add(optionsField);
            }
        }

        return (configFields, connectionStringFields, optionsFields, diagnostics);
    }

    /// <summary>
    /// Extracts ConfigField from a [Config] attribute.
    /// </summary>
    private static ConfigField? ExtractConfigField(
        AttributeSyntax configAttr,
        string fieldName,
        string fqn,
        bool isNullableFromSyntax,
        ITypeSymbol? typeSymbol,
        SemanticModel semanticModel,
        LocationKey location,
        List<ConfigDiagnostic> diagnostics,
        string className,
        HashSet<string> usedConfigKeys)
    {
        // Get attribute arguments
        var attrSymbol = semanticModel.GetSymbolInfo(configAttr).Symbol as IMethodSymbol;
        var attrData = attrSymbol?.ContainingType.Name == "ConfigAttribute"
            ? semanticModel.GetOperation(configAttr)
            : null;

        string? key = null;
        object? defaultValue = null;
        var hasDefaultValue = false;

        // Extract constructor argument (Key)
        if (configAttr.ArgumentList?.Arguments.Count > 0)
        {
            var keyArg = configAttr.ArgumentList.Arguments[0];
            if (keyArg.Expression is LiteralExpressionSyntax literal)
            {
                key = literal.Token.ValueText;
            }
        }

        // Extract named arguments (DefaultValue)
        if (configAttr.ArgumentList != null)
        {
            foreach (var arg in configAttr.ArgumentList.Arguments)
            {
                if (arg.NameEquals?.Name.Identifier.Text == "DefaultValue")
                {
                    hasDefaultValue = true;
                    var constValue = semanticModel.GetConstantValue(arg.Expression);
                    if (constValue.HasValue)
                    {
                        defaultValue = constValue.Value;
                    }
                }
            }
        }

        // Validate key
        if (string.IsNullOrWhiteSpace(key))
        {
            diagnostics.Add(new ConfigDiagnostic
            {
                Rule = DiagnosticRules.ConfigEmptyKey,
                Location = location,
                MessageArgs = []
            });
            return null;
        }

        // Check for duplicate keys
        if (!usedConfigKeys.Add(key))
        {
            diagnostics.Add(new ConfigDiagnostic
            {
                Rule = DiagnosticRules.ConfigDuplicateKey,
                Location = location,
                MessageArgs = [key, className]
            });
        }

        // Determine type category
        var (typeCategory, isNullableFromClassify, isEnum, isComplexType, isCollection, elementType) = ClassifyType(typeSymbol);

        if (typeCategory == ConfigFieldTypeCategory.Unsupported)
        {
            diagnostics.Add(new ConfigDiagnostic
            {
                Rule = DiagnosticRules.ConfigUnsupportedType,
                Location = location,
                MessageArgs = [fqn]
            });
            return null;
        }

        // Use nullable from syntax (NullableTypeSyntax check) or from semantic model
        var isNullable = isNullableFromSyntax || isNullableFromClassify;

        return new ConfigField
        {
            Key = key,
            Type = fqn,
            Name = fieldName,
            DefaultValue = defaultValue,
            HasDefaultValue = hasDefaultValue,
            IsNullable = isNullable,
            IsEnum = isEnum,
            IsComplexType = isComplexType,
            IsCollection = isCollection,
            ElementOrUnderlyingType = elementType,
            TypeCategory = typeCategory
        };
    }

    /// <summary>
    /// Extracts ConnectionStringField from a [ConnectionString] attribute.
    /// </summary>
    private static ConnectionStringField? ExtractConnectionStringField(
        AttributeSyntax connStrAttr,
        string fieldName,
        bool isNullable,
        ITypeSymbol? typeSymbol,
        SemanticModel semanticModel,
        LocationKey location,
        List<ConfigDiagnostic> diagnostics)
    {
        string? name = null;

        // Extract constructor argument (Name)
        if (connStrAttr.ArgumentList?.Arguments.Count > 0)
        {
            var nameArg = connStrAttr.ArgumentList.Arguments[0];
            if (nameArg.Expression is LiteralExpressionSyntax literal)
            {
                name = literal.Token.ValueText;
            }
        }

        // Validate name
        if (string.IsNullOrWhiteSpace(name))
        {
            diagnostics.Add(new ConfigDiagnostic
            {
                Rule = DiagnosticRules.ConnectionStringEmptyName,
                Location = location,
                MessageArgs = []
            });
            return null;
        }

        // Validate type is string
        var isString = typeSymbol?.SpecialType == SpecialType.System_String;

        if (!isString)
        {
            diagnostics.Add(new ConfigDiagnostic
            {
                Rule = DiagnosticRules.ConnectionStringInvalidType,
                Location = location,
                MessageArgs = [fieldName, typeSymbol?.ToDisplayString() ?? "unknown"]
            });
            return null;
        }

        return new ConnectionStringField
        {
            Name = name,
            FieldName = fieldName,
            IsNullable = isNullable
        };
    }

    /// <summary>
    /// Extracts OptionsField from an [Options] attribute.
    /// </summary>
    private static OptionsField? ExtractOptionsField(
        AttributeSyntax optionsAttr,
        string fieldName,
        string fqn,
        ITypeSymbol? typeSymbol,
        SemanticModel semanticModel,
        LocationKey location,
        List<ConfigDiagnostic> diagnostics)
    {
        string? section = null;

        // Extract constructor argument (Section)
        if (optionsAttr.ArgumentList?.Arguments.Count > 0)
        {
            var sectionArg = optionsAttr.ArgumentList.Arguments[0];
            if (sectionArg.Expression is LiteralExpressionSyntax literal)
            {
                section = literal.Token.ValueText;
            }
        }

        // Validate type is IOptions<T>, IOptionsSnapshot<T>, or IOptionsMonitor<T>
        if (typeSymbol is not INamedTypeSymbol namedType || !namedType.IsGenericType)
        {
            diagnostics.Add(new ConfigDiagnostic
            {
                Rule = DiagnosticRules.OptionsInvalidType,
                Location = location,
                MessageArgs = [fieldName, fqn]
            });
            return null;
        }

        var typeName = namedType.OriginalDefinition.ToDisplayString();
        OptionsFieldKind kind;

        if (typeName == "Microsoft.Extensions.Options.IOptions<T>" ||
            typeName.EndsWith(".IOptions<T>") ||
            namedType.Name == "IOptions")
        {
            kind = OptionsFieldKind.Options;
        }
        else if (typeName == "Microsoft.Extensions.Options.IOptionsSnapshot<T>" ||
                 typeName.EndsWith(".IOptionsSnapshot<T>") ||
                 namedType.Name == "IOptionsSnapshot")
        {
            kind = OptionsFieldKind.OptionsSnapshot;
        }
        else if (typeName == "Microsoft.Extensions.Options.IOptionsMonitor<T>" ||
                 typeName.EndsWith(".IOptionsMonitor<T>") ||
                 namedType.Name == "IOptionsMonitor")
        {
            kind = OptionsFieldKind.OptionsMonitor;
        }
        else
        {
            diagnostics.Add(new ConfigDiagnostic
            {
                Rule = DiagnosticRules.OptionsInvalidType,
                Location = location,
                MessageArgs = [fieldName, fqn]
            });
            return null;
        }

        var optionsType = namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new OptionsField
        {
            Section = section,
            Type = fqn,
            Name = fieldName,
            OptionsType = optionsType,
            Kind = kind
        };
    }

    /// <summary>
    /// Classifies a type symbol into a ConfigFieldTypeCategory.
    /// </summary>
    private static (ConfigFieldTypeCategory Category, bool IsNullable, bool IsEnum, bool IsComplexType, bool IsCollection, string? ElementType) ClassifyType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol == null)
            return (ConfigFieldTypeCategory.Unsupported, false, false, false, false, null);

        var isNullable = false;
        var underlyingType = typeSymbol;

        // Handle nullable value types (Nullable<T>)
        if (typeSymbol is INamedTypeSymbol { IsGenericType: true } namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            isNullable = true;
            underlyingType = namedType.TypeArguments[0];
        }

        // Handle nullable reference types
        if (typeSymbol.NullableAnnotation == NullableAnnotation.Annotated)
        {
            isNullable = true;
        }

        // Check for string
        if (underlyingType.SpecialType == SpecialType.System_String)
            return (ConfigFieldTypeCategory.String, isNullable, false, false, false, null);

        // Check for bool
        if (underlyingType.SpecialType == SpecialType.System_Boolean)
            return (ConfigFieldTypeCategory.Boolean, isNullable, false, false, false, null);

        // Check for char
        if (underlyingType.SpecialType == SpecialType.System_Char)
            return (ConfigFieldTypeCategory.Char, isNullable, false, false, false, null);

        // Check for numeric types
        if (underlyingType.SpecialType is
            SpecialType.System_Byte or SpecialType.System_SByte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double or
            SpecialType.System_Decimal)
        {
            return (ConfigFieldTypeCategory.Numeric, isNullable, false, false, false, null);
        }

        // Check for enum
        if (underlyingType.TypeKind == TypeKind.Enum)
        {
            var enumFqn = underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return (ConfigFieldTypeCategory.Enum, isNullable, true, false, false, enumFqn);
        }

        // Check for DateTime types
        var fullName = underlyingType.ToDisplayString();
        if (fullName == "System.DateTime" || fullName == "global::System.DateTime")
            return (ConfigFieldTypeCategory.DateTime, isNullable, false, false, false, null);

        if (fullName == "System.DateTimeOffset" || fullName == "global::System.DateTimeOffset")
            return (ConfigFieldTypeCategory.DateTime, isNullable, false, false, false, null);

        if (fullName == "System.TimeSpan" || fullName == "global::System.TimeSpan")
            return (ConfigFieldTypeCategory.TimeSpan, isNullable, false, false, false, null);

        if (fullName == "System.DateOnly" || fullName == "global::System.DateOnly")
            return (ConfigFieldTypeCategory.DateOnly, isNullable, false, false, false, null);

        if (fullName == "System.TimeOnly" || fullName == "global::System.TimeOnly")
            return (ConfigFieldTypeCategory.TimeOnly, isNullable, false, false, false, null);

        if (fullName == "System.Guid" || fullName == "global::System.Guid")
            return (ConfigFieldTypeCategory.Guid, isNullable, false, false, false, null);

        if (fullName == "System.Uri" || fullName == "global::System.Uri")
            return (ConfigFieldTypeCategory.Uri, isNullable, false, false, false, null);

        // Check for arrays
        if (underlyingType is IArrayTypeSymbol arrayType)
        {
            var elementFqn = arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return (ConfigFieldTypeCategory.Collection, isNullable, false, false, true, elementFqn);
        }

        // Check for List<T>, IEnumerable<T>, IList<T>, ICollection<T>
        if (underlyingType is INamedTypeSymbol { IsGenericType: true } genericType)
        {
            var genericDef = genericType.OriginalDefinition.ToDisplayString();
            if (genericDef.StartsWith("System.Collections.Generic.List<") ||
                genericDef.StartsWith("System.Collections.Generic.IList<") ||
                genericDef.StartsWith("System.Collections.Generic.IEnumerable<") ||
                genericDef.StartsWith("System.Collections.Generic.ICollection<") ||
                genericType.Name == "List" || genericType.Name == "IList" ||
                genericType.Name == "IEnumerable" || genericType.Name == "ICollection")
            {
                var elementFqn = genericType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return (ConfigFieldTypeCategory.Collection, isNullable, false, false, true, elementFqn);
            }
        }

        // Treat as complex type (POCO) - use GetSection().Get<T>()
        if (underlyingType.TypeKind == TypeKind.Class || underlyingType.TypeKind == TypeKind.Struct)
        {
            return (ConfigFieldTypeCategory.Complex, isNullable, false, true, false, null);
        }

        return (ConfigFieldTypeCategory.Unsupported, isNullable, false, false, false, null);
    }
}
