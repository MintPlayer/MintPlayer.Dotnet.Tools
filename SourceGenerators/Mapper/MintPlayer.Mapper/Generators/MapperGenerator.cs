using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.Extensions;

namespace MintPlayer.Mapper.Generators;

[Generator(LanguageNames.CSharp)]
public class MapperGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var typesToMapProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "MintPlayer.Mapper.Attributes.GenerateMapperAttribute",
                static (node, ct) => node is not null,
                static (ctx, ct) =>
                {
                    if (ctx.TargetSymbol is IAssemblySymbol assemblySymbol &&
                        ctx.Attributes.Where(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.GenerateMapperAttribute").ToArray() is { Length: > 0 } attributes1)
                    {
                        // Applied on assembly
                        return attributes1.Select(attr1 =>
                        {
                            if (attr1.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol sourceType &&
                                attr1.ConstructorArguments.ElementAtOrDefault(1).Value is INamedTypeSymbol destType1 &&
                                attr1.ConstructorArguments.ElementAtOrDefault(2) is TypedConstant { Kind: TypedConstantKind.Primitive, Type.SpecialType: SpecialType.System_String } methodNamePrefix)
                            {
                                var destAttr = destType1.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.GenerateMapperAttribute");
                                //var destTypeMethodName = destAttr?.ConstructorArguments.ElementAtOrDefault(1);
                                var mappingMethodName = destType1.Name.EnsureStartsWith("MapTo");
                                var declaredMethodName = sourceType.Name.EnsureStartsWith("MapTo");
                                return new Models.TypeToMap
                                {
                                    DestinationNamespace = sourceType.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
                                    DeclaredType = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    DeclaredTypeName = sourceType.Name,
                                    PreferredDeclaredMethodName = declaredMethodName,
                                    MappingType = destType1.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    MappingTypeName = destType1.Name,
                                    PreferredMappingMethodName = mappingMethodName,
                                    AreBothDecorated = destType1.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.GenerateMapperAttribute"),
                                    AppliedOn = Models.EAppliedOn.Assembly,
                                    HasError = false,
                                    Location = attr1.ApplicationSyntaxReference?.GetSyntax(ct)?.GetLocation().AsKey(),

                                    DeclaredProperties = ProcessProperties(sourceType).ToArray(),
                                    MappingProperties = ProcessProperties(destType1).ToArray(),
                                };
                            }
                            else
                            {
                                return new Models.TypeToMap
                                {
                                    AppliedOn = Models.EAppliedOn.Assembly,
                                    HasError = true,
                                    Location = attr1.ApplicationSyntaxReference?.GetSyntax(ct)?.GetLocation().AsKey(),
                                };
                            }
                        });
                    }


                    if (ctx.TargetSymbol is INamedTypeSymbol typeSymbol &&
                        ctx.Attributes.Where(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.GenerateMapperAttribute").ToArray() is { Length: > 0 } attributes2)
                    {
                        // Applied on class or struct
                        return attributes2.Select(attr2 =>
                        {
                            if (attr2.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol destType2 &&
                                attr2.ConstructorArguments.ElementAtOrDefault(1) is TypedConstant { Kind: TypedConstantKind.Primitive, Type.SpecialType: SpecialType.System_String } typeMethodName2)
                            {
                                var destAttr = destType2.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.GenerateMapperAttribute");
                                var destTypeMethodName = destAttr?.ConstructorArguments.ElementAtOrDefault(1);
                                var mappingMethodName = destTypeMethodName is null ? destType2.Name.EnsureStartsWith("MapTo") : CreateMethodName((TypedConstant)destTypeMethodName, destType2);
                                return new Models.TypeToMap
                                {
                                    DestinationNamespace = typeSymbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)),
                                    DeclaredType = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    DeclaredTypeName = typeSymbol.Name,
                                    PreferredDeclaredMethodName = CreateMethodName(typeMethodName2, typeSymbol),
                                    MappingType = destType2.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    MappingTypeName = destType2.Name,
                                    PreferredMappingMethodName = mappingMethodName,
                                    AreBothDecorated = destType2.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.GenerateMapperAttribute"),
                                    AppliedOn = Models.EAppliedOn.Class,
                                    HasError = false,
                                    Location = attr2.ApplicationSyntaxReference?.GetSyntax(ct)?.GetLocation().AsKey(),

                                    DeclaredProperties = ProcessProperties(typeSymbol).ToArray(),
                                    MappingProperties = ProcessProperties(destType2).ToArray(),
                                };
                            }
                            else
                            {
                                return new Models.TypeToMap
                                {
                                    AppliedOn = Models.EAppliedOn.Class,
                                    HasError = true,
                                    Location = attr2.ApplicationSyntaxReference?.GetSyntax(ct)?.GetLocation().AsKey(),
                                };
                            }
                        });
                    }
                    return [];
                }
            )
            .Where(static (i) => i is { })
            .SelectMany(static (i, ct) => i)
            .WithComparer();

        //var mapperConversionMethodsProvider = context.SyntaxProvider
        //    .ForAttributeWithMetadataName(
        //        "MintPlayer.Mapper.Attributes.MapperConversionAttribute",
        //        static (node, ct) => node is MethodDeclarationSyntax methodDeclaration && methodDeclaration.ParameterList.Parameters.Count == 1 && !methodDeclaration.ReturnType.IsKind(SyntaxKind.VoidKeyword),
        //        static (ctx, ct) =>
        //        {
        //            if (ctx.TargetSymbol is IMethodSymbol methodSymbol &&
        //                methodSymbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.MapperConversionAttribute") is { } attribute)
        //            {
        //                return new Models.ConversionMethod
        //                {
        //                    MethodName = methodSymbol.Name,
        //                };
        //            }
        //            return null;
        //        }
        //    )


        //var mapperConversionMethodsProvider = context.SyntaxProvider
        //    .CreateSyntaxProvider(
        //        static (node, ct) => node is MethodDeclarationSyntax methodDeclaration && methodDeclaration.AttributeLists.Count > 0,
        //        static (context2, ct) =>
        //        {
        //            if (context2.Node is MethodDeclarationSyntax methodDeclaration &&
        //                context2.SemanticModel.GetDeclaredSymbol(methodDeclaration, ct) is IMethodSymbol methodSymbol &&
        //                methodSymbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.MapperConversionAttribute") is { } attribute)
        //            {
        //                return new
        //                {
        //                    Method = methodSymbol,
        //                    Attribute = attribute,
        //                };
        //            }
        //            return null;
        //        }
        //    )
        //    .Where(static (m) => m is not null)
        //    .WithNullableComparer()
        //    .Collect();

        var distinctTypesToMapProvider = typesToMapProvider
            .Select(static (i, ct) => new Models.TypeWithMappedProperties
            {
                TypeToMap = i,
                MappedProperties = i.DeclaredProperties
                    .Select(dp => (Source: dp, Destination: i.MappingProperties.FirstOrDefault(mp => mp.Alias == dp.Alias)))
                    .Where(p => p.Source is { IsStatic: false } && p.Destination is { IsStatic: false })
            })
            .WithComparer()
            .Collect();

        var staticClassesProvider = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, ct) => node is ClassDeclarationSyntax classDeclaration && classDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword),
                static (context2, ct) =>
                {
                    if (context2.Node is ClassDeclarationSyntax classDeclaration &&
                        context2.SemanticModel.GetDeclaredSymbol(classDeclaration, ct) is INamedTypeSymbol classSymbol)
                    {
                        return new Models.ClassDeclaration
                        {
                            Namespace = classSymbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            FullyQualifiedName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                            Name = classSymbol.Name,
                            ConversionMethods = classSymbol.GetMembers()
                                .OfType<IMethodSymbol>()
                                .Where(m => !m.IsImplicitlyDeclared && m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && (m.Parameters.Length is 1 or 3) && !m.ReturnsVoid)
                                .Select(m => new
                                {
                                    Method = m,
                                    Attribute = m.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.None).WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)) == "MintPlayer.Mapper.Attributes.MapperConversionAttribute"),
                                })
                                .Where(m => m.Attribute is not null)
                                .Select(m => new Models.ConversionMethod
                                {
                                    MethodName = m.Method.Name,
                                    MethodParameterCount = m.Method.Parameters.Length,
                                    SourceType = m.Method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                    SourceTypeName = m.Method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)),
                                    SourceTypeNullable = m.Method.Parameters[0].NullableAnnotation == NullableAnnotation.Annotated,
                                    SourceState = m.Attribute!.ConstructorArguments.Length >= 1 && m.Attribute.ConstructorArguments[0].Value is int sourceState ? sourceState : null,

                                    DestinationType = m.Method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                    DestinationTypeName = m.Method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)),
                                    DestinationState = m.Attribute.ConstructorArguments.Length >= 2 && m.Attribute.ConstructorArguments[1].Value is int destState ? destState : null,
                                    StateType = m.Attribute.AttributeConstructor?.ContainingType?.TypeArguments.FirstOrDefault()?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                    StateTypeName = m.Attribute.AttributeConstructor?.ContainingType?.TypeArguments.FirstOrDefault()?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),

                                    AttributeLocation = (m.Attribute.ApplicationSyntaxReference?.GetSyntax(ct)?.GetLocation()).AsKey(),
                                })
                                .ToArray(),
                        };
                    }
                    return null;
                }
            )
            .Where(static (m) => m is { ConversionMethods.Length: > 0 })
            .WithNullableComparer()
            .Collect();

        var conversionMethodsWithMissingStateProvider = staticClassesProvider
            .SelectMany(static (c, ct) => c.SelectMany(cl => cl is null ? [] : cl.ConversionMethods.Where(m => m.SourceState is null || m.DestinationState is null)))
            .Where(static (m) => m.SourceType == m.DestinationType)
            .WithComparer();

        var conversionMethodsWithUnnecessaryStateProvider = staticClassesProvider
            .SelectMany(static (c, ct) => c.SelectMany(cl => cl is null ? [] : cl.ConversionMethods.Where(m => m.SourceState is not null || m.DestinationState is not null)))
            .Where(static (m) => m.SourceType != m.DestinationType)
            .WithComparer();


        var typesToMapSourceProvider = distinctTypesToMapProvider
            .Join(staticClassesProvider)
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new MapperProducer(p.Item1, p.Item2, p.Item3.RootNamespace!));

        var typesToMapDiagnosticProvider = distinctTypesToMapProvider
            .Join(staticClassesProvider)
            .Join(settingsProvider)
            .Select(static IDiagnosticReporter (p, ct) => new MapperProducer(p.Item1, p.Item2, p.Item3.RootNamespace!));

        var mapperEntrypointSourceProvider = distinctTypesToMapProvider
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new MapperEntrypointProducer(p.Item1, p.Item2.RootNamespace!));


        context.ProduceCode(typesToMapSourceProvider, mapperEntrypointSourceProvider);
        context.ReportDiagnostics(typesToMapDiagnosticProvider);
    }

    private static string CreateMethodName(TypedConstant preferred, INamedTypeSymbol type)
    {
        var preferredMappingMethodName = (preferred.Value as string) ?? type.Name;
        return preferredMappingMethodName.EnsureStartsWith("MapTo");
    }

    private static IEnumerable<Models.PropertyDeclaration> ProcessProperties(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.GetAllProperties()
            .Where(p => !p.IsIndexer && !p.IsImplicitlyDeclared && !p.IsStatic && !p.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.IgnoreMapAttribute"))
            .Select(p => new Models.PropertyDeclaration
            {
                PropertyName = p.Name,
                PropertyType = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                PropertyTypeName = p.Type is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.Name == "List" && namedType.TypeArguments.Length == 1
                    ? namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                    : p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),

                Alias = p.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.MapToAttribute")
                    is { ConstructorArguments.Length: > 0 } aliasAttr
                    && aliasAttr.ConstructorArguments[0].Value is string aliasName
                    ? aliasName : p.Name,

                StateName = p.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted).WithGenericsOptions(SymbolDisplayGenericsOptions.None)) == "MintPlayer.Mapper.Attributes.MapperStateAttribute")
                    is { ConstructorArguments.Length: > 0 } stateAttr
                    && stateAttr.ConstructorArguments[0] is TypedConstant stateRef
                    && stateRef.Kind == TypedConstantKind.Enum
                    && stateRef.Value is int stateValue
                    ? stateValue : null,
                //IsNullable = p.NullableAnnotation == NullableAnnotation.Annotated,
                //IsReadOnly = p.IsReadOnly,
                IsStatic = p.IsStatic,
                //IsVirtual = p.IsVirtual,
                //IsAbstract = p.IsAbstract,
                //IsOverride = p.IsOverride,
                IsPrimitive = p.Type.IsValueType || p.Type.SpecialType == SpecialType.System_String,
            });
    }

    public override void RegisterComparers()
    {
    }
}