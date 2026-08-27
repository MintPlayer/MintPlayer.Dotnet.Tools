using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.Assertions.SourceGenerator.Helpers;
using MintPlayer.Assertions.SourceGenerator.Models;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.Assertions.SourceGenerator.Generators;

/// <summary>
/// Turns a plain predicate marked with <c>[GenerateAssertion]</c> into a fluent assertion:
/// <c>static bool IsEven(int value)</c> becomes <c>value.Should().BeEven()</c>, with
/// because/becauseArgs support and a failure message derived from the assertion name.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class GenerateAssertionGenerator : IncrementalGenerator
{
    private const string GenerateAssertionAttribute = "MintPlayer.Assertions.GenerateAssertionAttribute";
    private const string AssertionExtensionsSuffix = "AssertionExtensions";

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider, IncrementalValueProvider<ICompilationCache> valueComparerCacheProvider)
    {
        var methodProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            GenerateAssertionAttribute,
            static (node, ct) => node is MethodDeclarationSyntax,
            static (ctx, ct) => ctx.TargetSymbol is IMethodSymbol method
                ? Describe(method, ctx.Attributes.FirstOrDefault())
                : null)
            .Where(static declaration => declaration is not null)
            .Select(static (declaration, ct) => declaration!);

        var declarationsProvider = methodProvider.Collect()
            .Select(static (declarations, ct) => new EquatableArray<AssertionMethodDeclaration>(declarations
                .Where(static declaration => declaration.Diagnostic is null)
                .OrderBy(static declaration => declaration.ContainingTypeFullName, StringComparer.Ordinal)
                .ThenBy(static declaration => declaration.GeneratedName, StringComparer.Ordinal)
                .ToArray()));

        var diagnosticsProvider = methodProvider.Collect()
            .Select(static IDiagnosticReporter (declarations, ct) => new UnsupportedAssertionReporter(new EquatableArray<AssertionMethodDeclaration>(declarations
                .Where(static declaration => declaration.Diagnostic is not null)
                .ToArray())));

        var sourceProvider = declarationsProvider
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new GenerateAssertionProducer(p.Item1, p.Item2.RootNamespace));

        context.ProduceCode(sourceProvider);
        context.ReportDiagnostics(diagnosticsProvider);
    }

    private static AssertionMethodDeclaration Describe(IMethodSymbol method, AttributeData? attribute)
    {
        var location = method.Locations.FirstOrDefault().AsKey();
        var reason = GetUnsupportedReason(method);
        if (reason is not null)
            return new AssertionMethodDeclaration { MethodName = method.Name, ContainingTypeFullName = method.ContainingType?.ToDisplayString() ?? string.Empty, Diagnostic = reason, Location = location };

        var subjectType = method.Parameters[0].Type;
        var underlying = subjectType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : subjectType;

        var explicitName = attribute?.NamedArguments.FirstOrDefault(a => a.Key == "Name").Value.Value as string;
        var generatedName = string.IsNullOrWhiteSpace(explicitName) ? AssertionNaming.DeriveName(method.Name) : explicitName!;

        return new AssertionMethodDeclaration
        {
            Namespace = method.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : string.Empty,
            ContainingTypeName = GetNestedName(method.ContainingType),
            ContainingTypeFullName = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            MethodName = method.Name,
            GeneratedName = generatedName,
            Phrase = AssertionNaming.Humanize(generatedName),
            SubjectKind = GetSubjectKind(underlying),
            SubjectTypeFullName = underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ExtraParameters = method.Parameters.Skip(1)
                .Select(p => new ParameterDeclaration(p.Name, p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                .ToArray(),
            IsPublic = IsPubliclyVisible(method.ContainingType) && method.Parameters.Skip(1).All(p => IsPubliclyVisible(p.Type)),
            Location = location,
        };
    }

    private static string? GetUnsupportedReason(IMethodSymbol method)
    {
        if (!method.IsStatic) return "the method must be static";
        if (method.ReturnType.SpecialType != SpecialType.System_Boolean) return "the method must return bool";
        if (method.Parameters.Length == 0) return "the method must take the subject as its first parameter";
        if (method.TypeParameters.Length > 0) return "generic methods are not supported";
        if (method.ContainingType is null or { IsGenericType: true }) return "the declaring type must be non-generic";
        if (method.Parameters.Any(p => p.RefKind is not RefKind.None)) return "by-ref parameters are not supported";
        return null;
    }

    private static SubjectKind GetSubjectKind(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_String => SubjectKind.String,
        SpecialType.System_Boolean => SubjectKind.Boolean,
        SpecialType.System_SByte or SpecialType.System_Byte or
        SpecialType.System_Int16 or SpecialType.System_UInt16 or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or
        SpecialType.System_Int64 or SpecialType.System_UInt64 or
        SpecialType.System_Single or SpecialType.System_Double or
        SpecialType.System_Decimal => SubjectKind.Numeric,
        _ => SubjectKind.Object,
    };

    /// <summary>True when the type (and everything it is nested in or built from) is public.</summary>
    private static bool IsPubliclyVisible(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array: return IsPubliclyVisible(array.ElementType);
            case ITypeParameterSymbol: return true;
            case INamedTypeSymbol named:
                for (INamedTypeSymbol? current = named; current is not null; current = current.ContainingType)
                {
                    if (current.DeclaredAccessibility is not Accessibility.Public and not Accessibility.NotApplicable) return false;
                }
                return named.TypeArguments.All(IsPubliclyVisible);
            default: return true;
        }
    }

    /// <summary>Joins nested type names, so a nested Helpers class yields "Outer_HelpersAssertionExtensions".</summary>
    private static string GetNestedName(INamedTypeSymbol type)
    {
        var names = new List<string>();
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
            names.Insert(0, current.Name);
        return string.Join("_", names) + AssertionExtensionsSuffix;
    }
}
