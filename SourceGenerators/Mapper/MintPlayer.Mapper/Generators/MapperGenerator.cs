using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.Mapper.Generators;

[Generator(LanguageNames.CSharp)]
public class MapperGenerator : IncrementalGenerator
{
    private const string GenerateMapperAttribute = "MintPlayer.Mapper.Attributes.GenerateMapperAttribute";
    private const string MapperConversionAttribute = "MintPlayer.Mapper.Attributes.MapperConversionAttribute";
    private const string MapperConversionOfTAttribute = "MintPlayer.Mapper.Attributes.MapperConversionAttribute`1";
    private const string MapperStateAttribute = "MintPlayer.Mapper.Attributes.MapperStateAttribute`1";
    private const string MapToAttribute = "MintPlayer.Mapper.Attributes.MapToAttribute";
    private const string IgnoreMapAttribute = "MintPlayer.Mapper.Attributes.IgnoreMapAttribute";
    private const string NotNullIfNotNullAttribute = "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute";
    private const string DefaultMethodPrefix = "MapTo";

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        // One target (the assembly) can carry many [GenerateMapper] attributes, so each target gives an array.
        var typesToMap = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GenerateMapperAttribute,
                static (node, ct) => node is not null,
                static (ctx, ct) => new EquatableArray<Discovered<Models.TypeToMap>>(ctx.Attributes.Select(attr => DiscoverTypeToMap(ctx.TargetSymbol, attr, ct))))
            .SelectMany(static (d, ct) => d);

        var distinctTypesToMapProvider = typesToMap
            .Models()
            .Select(static (i, ct) => new Models.TypeWithMappedProperties
            {
                TypeToMap = i,
                MappedProperties = i.DeclaredProperties
                    .Select(dp => (Source: dp, Destination: i.MappingProperties.FirstOrDefault(mp => mp.Alias == dp.Alias)))
                    .Where(p => p.Source is { IsStatic: false } && p.Destination is { IsStatic: false })
                    // Materialized: a deferred Where compares by reference, and was re-evaluated
                    // on every enumeration by both producers.
                    .ToArray()
            })
            .Collect();

        // Found through the attribute, one method at a time. This used to visit every static class in the
        // compilation, and visited a partial static class once per part, so a conversion declared in one of
        // two parts was registered twice.
        var plainConversions = context.SyntaxProvider.ForAttribute<Models.ConversionMethod>(
            MapperConversionAttribute,
            static (node, ct) => node is MethodDeclarationSyntax,
            static (ctx, ct) => DiscoverConversionMethod(ctx, ct));
        var statefulConversions = context.SyntaxProvider.ForAttribute<Models.ConversionMethod>(
            MapperConversionOfTAttribute,
            static (node, ct) => node is MethodDeclarationSyntax,
            static (ctx, ct) => DiscoverConversionMethod(ctx, ct));

        var conversionMethodsProvider = plainConversions.Models().Collect()
            .Join(statefulConversions.Models().Collect());

        // The attribute is missing on netstandard2.0 and .NET Framework without a polyfill.
        var hasNotNullIfNotNullProvider = context.CompilationProvider
            .Select(static (c, ct) => c.GetTypeByMetadataName(NotNullIfNotNullAttribute) is { } attribute
                && c.IsSymbolAccessibleWithin(attribute, c.Assembly));

        var typesToMapSourceProvider = distinctTypesToMapProvider
            .Join(conversionMethodsProvider)
            .Join(hasNotNullIfNotNullProvider)
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new MapperProducer(p.Item1, p.Item2.Item1.AddRange(p.Item2.Item2), p.Item3, p.Item4.RootNamespace!));

        var mapperEntrypointSourceProvider = distinctTypesToMapProvider
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new MapperEntrypointProducer(p.Item1, p.Item2.RootNamespace!));

        context.ProduceCode(typesToMapSourceProvider, mapperEntrypointSourceProvider);
        context.ReportDiagnostics(typesToMap.Diagnostics(), plainConversions.Diagnostics(), statefulConversions.Diagnostics());
    }

    private static Discovered<Models.TypeToMap> DiscoverTypeToMap(ISymbol target, AttributeData attribute, CancellationToken ct)
    {
        var location = attribute.ApplicationSyntaxReference?.GetSyntax(ct)?.GetLocation();

        if (target is IAssemblySymbol)
        {
            // [assembly: GenerateMapper(typeof(Source), typeof(Target), "Prefix")]
            if (!attribute.TryGetArgument<TypedConstant>("sourceType", out _) || !attribute.TryGetArgument<TypedConstant>("targetType", out _))
                return Discovered<Models.TypeToMap>.Fail(DiagnosticInfo.Create(DiagnosticRules.GenerateMapperTwoParameters, location));

            // A typeof that did not bind is already a compiler error.
            if (!attribute.TryGetArgument<ITypeSymbol>("sourceType", out var sourceType) || !attribute.TryGetArgument<ITypeSymbol>("targetType", out var targetType))
                return Discovered<Models.TypeToMap>.None;

            var prefix = attribute.GetArgument<string?>("methodName", null) ?? DefaultMethodPrefix;
            return CreateTypeToMap(
                sourceType,
                targetType,
                declaredMethodName: prefix + sourceType.Name,
                mappingMethodName: prefix + targetType.Name,
                location);
        }

        if (target is INamedTypeSymbol typeSymbol)
        {
            // [GenerateMapper(typeof(Target), "MethodName")]
            if (!attribute.TryGetArgument<TypedConstant>("mapType", out _))
                return Discovered<Models.TypeToMap>.Fail(DiagnosticInfo.Create(DiagnosticRules.GenerateMapperOneParameter, location));

            if (!attribute.TryGetArgument<ITypeSymbol>("mapType", out var destType))
                return Discovered<Models.TypeToMap>.None;

            var destTypeMethodName = destType.FindAttribute(GenerateMapperAttribute).GetArgument<string?>("methodName", null);
            return CreateTypeToMap(
                typeSymbol,
                destType,
                declaredMethodName: CreateMethodName(attribute.GetArgument<string?>("methodName", null), typeSymbol),
                mappingMethodName: CreateMethodName(destTypeMethodName, destType),
                location);
        }

        return Discovered<Models.TypeToMap>.None;
    }

    private static Discovered<Models.TypeToMap> CreateTypeToMap(ITypeSymbol declared, ITypeSymbol mapping, string declaredMethodName, string mappingMethodName, Location? location)
    {
        var diagnostics = new List<DiagnosticInfo>();
        var types = SymbolEqualityComparer.Default.Equals(declared, mapping) ? new[] { declared } : new[] { declared, mapping };
        foreach (var type in types)
        {
            if (UnsupportedReason(type) is { } reason)
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.MappedTypeCannotBeInstantiated, location, type, reason));
            else if (EffectiveAccessibility(type) == Accessibility.Private)
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticRules.MappedTypeNotAccessible, location, type));
        }

        if (diagnostics.Count > 0)
            return new Discovered<Models.TypeToMap>(null, diagnostics.ToArray());

        var declaredType = (INamedTypeSymbol)declared;
        var mappingType = (INamedTypeSymbol)mapping;
        return new Discovered<Models.TypeToMap>(new Models.TypeToMap
        {
            DestinationNamespace = declaredType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
            DeclaredType = declaredType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            DeclaredTypeName = declaredType.Name,
            DeclaredTypeAccessibility = (int)EffectiveAccessibility(declaredType),
            DeclaredTypeIsValueType = declaredType.IsValueType,
            DeclaredTypeDepth = InheritanceDepth(declaredType),
            DeclaredTypeHasParameterlessConstructor = HasParameterlessConstructor(declaredType),
            DeclaredTypePrimaryConstructorParameters = GetPrimaryConstructorParameters(declaredType),
            PreferredDeclaredMethodName = declaredMethodName,

            MappingType = mappingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            MappingTypeName = mappingType.Name,
            MappingTypeAccessibility = (int)EffectiveAccessibility(mappingType),
            MappingTypeIsValueType = mappingType.IsValueType,
            MappingTypeDepth = InheritanceDepth(mappingType),
            MappingTypeHasParameterlessConstructor = HasParameterlessConstructor(mappingType),
            MappingTypePrimaryConstructorParameters = GetPrimaryConstructorParameters(mappingType),
            PreferredMappingMethodName = mappingMethodName,
            AreBothDecorated = mappingType.HasAttribute(GenerateMapperAttribute),

            DeclaredProperties = ProcessProperties(declaredType).ToArray(),
            MappingProperties = ProcessProperties(mappingType).ToArray(),
        });
    }

    /// <summary>
    /// Why the generated mapper cannot write <c>new T()</c> for <paramref name="type"/>, or null when it can.
    /// </summary>
    private static string? UnsupportedReason(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named) return "it is not a class or struct";
        if (named.TypeKind == TypeKind.Interface) return "it is an interface";
        if (named.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return "it is not a class or struct";
        if (named.IsStatic) return "it is static";
        if (named.IsAbstract) return "it is abstract";
        if (named.IsRefLikeType) return "it is a ref struct";
        if (named.IsUnboundGenericType || ContainsTypeParameter(named)) return "it is an open generic type";
        return null;
    }

    private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter)
            || (named.ContainingType is { } containing && ContainsTypeParameter(containing)),
        _ => false,
    };

    /// <summary>
    /// The accessibility of <paramref name="type"/> as seen from the generated top-level <c>MapperExtensions</c>
    /// class: <see cref="Accessibility.Public"/>, <see cref="Accessibility.Internal"/>, or
    /// <see cref="Accessibility.Private"/> when the generated code cannot name it at all (a private, protected or
    /// file-local type, or one nested in such a type).
    /// </summary>
    private static Accessibility EffectiveAccessibility(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return EffectiveAccessibility(array.ElementType);
            case ITypeParameterSymbol:
                return Accessibility.Private;
            case INamedTypeSymbol named:
                var result = Accessibility.Public;
                for (var current = named; current is not null; current = current.ContainingType)
                {
                    if (current.IsFileLocal) return Accessibility.Private;
                    result = Narrowest(result, current.DeclaredAccessibility switch
                    {
                        Accessibility.Public or Accessibility.NotApplicable => Accessibility.Public,
                        Accessibility.Internal or Accessibility.ProtectedOrInternal => Accessibility.Internal,
                        _ => Accessibility.Private,
                    });
                    foreach (var argument in current.TypeArguments)
                        result = Narrowest(result, EffectiveAccessibility(argument));
                }
                return result;
            default:
                return Accessibility.Public;
        }

        static Accessibility Narrowest(Accessibility a, Accessibility b) => a < b ? a : b;
    }

    private static int InheritanceDepth(INamedTypeSymbol type)
    {
        var depth = 0;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            depth++;
        return depth;
    }

    private static Discovered<Models.ConversionMethod>? DiscoverConversionMethod(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method || ctx.Attributes.FirstOrDefault() is not { } attribute)
            return null;

        // The shapes the generated ConvertProperty can call. Anything else was never picked up, and still isn't.
        if (method.ContainingType is not { TypeKind: TypeKind.Class, IsStatic: true } containingType
            || !method.IsStatic
            || method.DeclaredAccessibility != Accessibility.Public
            || method.Parameters.Length is not (1 or 3)
            || method.ReturnsVoid)
            return null;

        var location = attribute.ApplicationSyntaxReference?.GetSyntax(ct)?.GetLocation();
        if (ContainsTypeParameter(containingType) || method.IsGenericMethod)
            return Discovered<Models.ConversionMethod>.Fail(DiagnosticInfo.Create(DiagnosticRules.ConversionMethodCannotBeCalled, location, method, "it is generic, or declared in a generic class"));
        if (EffectiveAccessibility(containingType) == Accessibility.Private)
            return Discovered<Models.ConversionMethod>.Fail(DiagnosticInfo.Create(DiagnosticRules.ConversionMethodCannotBeCalled, location, method, "its class is not accessible from the generated MapperExtensions class"));

        var includeGlobal = SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included);
        var stateType = attribute.AttributeClass?.TypeArguments.FirstOrDefault();
        return new Discovered<Models.ConversionMethod>(new Models.ConversionMethod
        {
            ContainingType = containingType.ToDisplayString(includeGlobal),
            MethodName = method.Name,
            MethodParameterCount = method.Parameters.Length,
            SourceType = method.Parameters[0].Type.ToDisplayString(includeGlobal),
            SourceTypeName = method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)),
            SourceTypeNullable = method.Parameters[0].NullableAnnotation == NullableAnnotation.Annotated,
            SourceState = attribute.GetArgument<int?>("inState", null),

            DestinationType = method.ReturnType.ToDisplayString(includeGlobal),
            DestinationTypeName = method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)),
            DestinationState = attribute.GetArgument<int?>("outState", null),
            StateType = stateType?.ToDisplayString(includeGlobal),
            StateTypeName = stateType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        });
    }

    private static string CreateMethodName(string? preferred, ITypeSymbol type)
        => (preferred ?? type.Name).EnsureStartsWith(DefaultMethodPrefix);

    private static bool HasParameterlessConstructor(INamedTypeSymbol typeSymbol)
    {
        // Struct types (including record structs) always have a parameterless constructor
        if (typeSymbol.TypeKind == TypeKind.Struct)
            return true;

        return typeSymbol.Constructors.Any(c => !c.IsStatic && c.Parameters.Length == 0);
    }

    private static Models.ConstructorParameterInfo[] GetPrimaryConstructorParameters(INamedTypeSymbol typeSymbol)
    {
        // Find the primary constructor: the one whose parameters correspond to properties
        // For records, this is the constructor generated from the positional parameters
        var primaryCtor = typeSymbol.Constructors
            .Where(c => !c.IsStatic && !c.IsImplicitlyDeclared)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault(c => c.Parameters.All(p =>
                typeSymbol.GetMembers().OfType<IPropertySymbol>().Any(prop =>
                    string.Equals(prop.Name, p.Name, System.StringComparison.OrdinalIgnoreCase))));

        if (primaryCtor is null || primaryCtor.Parameters.Length == 0)
            return [];

        return primaryCtor.Parameters.Select(p =>
        {
            var correspondingProperty = typeSymbol.GetMembers().OfType<IPropertySymbol>()
                .FirstOrDefault(prop => string.Equals(prop.Name, p.Name, System.StringComparison.OrdinalIgnoreCase));

            return new Models.ConstructorParameterInfo
            {
                ParameterName = p.Name,
                CorrespondingPropertyName = correspondingProperty?.Name ?? p.Name,
                ParameterType = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            };
        }).ToArray();
    }

    private static IEnumerable<Models.PropertyDeclaration> ProcessProperties(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.GetAllProperties()
            .Where(p => !p.IsIndexer && !p.IsImplicitlyDeclared && !p.IsStatic && !p.HasAttribute(IgnoreMapAttribute))
            .Select(p => new Models.PropertyDeclaration
            {
                PropertyName = p.Name,
                PropertyType = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                PropertyTypeName = p.Type is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.Name == "List" && namedType.TypeArguments.Length == 1
                    ? namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                    : p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat.RemoveMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)),

                Alias = p.FindAttribute(MapToAttribute).GetArgument<string?>("alias", null) ?? p.Name,
                StateName = p.FindAttribute(MapperStateAttribute).GetArgument<int?>("state", null),
                IsStatic = p.IsStatic,
                IsReadOnly = p.SetMethod is null,
                IsInitOnly = p.SetMethod is { IsInitOnly: true },
                IsPrimitive = p.Type.IsValueType || p.Type.SpecialType == SpecialType.System_String,
            });
    }
}
