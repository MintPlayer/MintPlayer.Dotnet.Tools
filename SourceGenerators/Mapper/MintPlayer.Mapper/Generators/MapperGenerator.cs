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
                        ctx.Attributes.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.GenerateMapperAttribute") is { } attr1)
                    {
                        // Applied on assembly
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
                                Location = ctx.TargetSymbol.Locations.FirstOrDefault(),

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
                                Location = ctx.TargetSymbol.Locations.FirstOrDefault(),
                            };
                        }
                    }


                    if (ctx.TargetSymbol is INamedTypeSymbol typeSymbol &&
                        ctx.Attributes.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.GenerateMapperAttribute") is { } attr)
                    {
                        // Applied on class or struct
                        if (attr.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol destType2 &&
                            attr.ConstructorArguments.ElementAtOrDefault(1) is TypedConstant { Kind: TypedConstantKind.Primitive, Type.SpecialType: SpecialType.System_String } typeMethodName2)
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
                                Location = ctx.TargetSymbol.Locations.FirstOrDefault(),

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
                                Location = ctx.TargetSymbol.Locations.FirstOrDefault(),
                            };
                        }
                    }
                    return null;
                }
            )
            .Where(static (i) => i is { })
            .Select(static (i, ct) => i!)
            .WithComparer();

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
                                .Where(m => !m.IsImplicitlyDeclared && m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && m.Parameters.Length == 1 && !m.ReturnsVoid)
                                .Select(m => new Models.ConversionMethod
                                {
                                    MethodName = m.Name,
                                    SourceType = m.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                    SourceTypeName = m.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)),
                                    SourceTypeNullable = m.Parameters[0].NullableAnnotation == NullableAnnotation.Annotated,
                                    DestinationType = m.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included)),
                                    DestinationTypeName = m.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(SymbolDisplayGenericsOptions.IncludeTypeParameters)),
                                })
                                .ToArray(),
                        };
                    }
                    return null;
                }
            )
            .Where(static (m) => m.ConversionMethods.Any())
            .WithNullableComparer()
            .Collect();


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
            .Where(p => !p.IsIndexer && !p.IsImplicitlyDeclared && !p.IsStatic && !p.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.MapperIgnoreAttribute"))
            .Select(p => new Models.PropertyDeclaration
            {
                PropertyName = p.Name,
                PropertyType = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                PropertyTypeName = p.Type is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.Name == "List" && namedType.TypeArguments.Length == 1
                    ? namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                    : p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),

                Alias = p.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "MintPlayer.Mapper.Attributes.MapperAliasAttribute")
                    is { ConstructorArguments.Length: > 0 } aliasAttr
                    && aliasAttr.ConstructorArguments[0].Value is string aliasName
                    ? aliasName : p.Name,
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