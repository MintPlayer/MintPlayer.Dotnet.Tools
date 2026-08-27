using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.Assertions.SourceGenerator.Helpers;
using MintPlayer.Assertions.SourceGenerator.Models;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.Assertions.SourceGenerator.Generators;

/// <summary>
/// Emits the reflection-free equivalency accessors: for every type that can reach a
/// <c>BeEquivalentTo</c> comparison — discovered at the call site, through
/// <c>[AssertEquivalency]</c>, or transitively through their members — a
/// <c>[ModuleInitializer]</c> registers a table of typed getters with the runtime registry.
/// Types the generator cannot emit an accessor for are silently skipped; the runtime falls back
/// to reflection for those, so the assertion behaves identically either way.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class EquivalencyRegistrationGenerator : IncrementalGenerator
{
    private const string AssertEquivalencyAttribute = "MintPlayer.Assertions.AssertEquivalencyAttribute";
    private const string RegistryTypeName = "MintPlayer.Assertions.Equivalency.EquivalencyRegistry";

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider, IncrementalValueProvider<ICompilationCache> valueComparerCacheProvider)
    {
        // (a) Call sites: <expr>.Should().BeEquivalentTo(expectation)
        var callSiteProvider = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, ct) => node is InvocationExpressionSyntax invocation && IsEquivalencyInvocation(invocation),
            static (ctx, ct) =>
            {
                var invocation = (InvocationExpressionSyntax)ctx.Node;
                var compilation = ctx.SemanticModel.Compilation;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var declarations = new List<EquivalencyTypeDeclaration>();

                // The expectation drives the comparison, so its static type is the primary candidate.
                if (invocation.ArgumentList.Arguments.FirstOrDefault() is { } firstArgument)
                    EquivalencyScanner.Collect(ctx.SemanticModel.GetTypeInfo(firstArgument.Expression, ct).Type, compilation, visited, declarations, ct);

                if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol method)
                {
                    foreach (var typeArgument in method.TypeArguments)
                        EquivalencyScanner.Collect(typeArgument, compilation, visited, declarations, ct);
                }

                // ... and the subject, when the chain is the canonical <expr>.Should().BeEquivalentTo(...)
                if (GetShouldReceiver(invocation) is { } receiver)
                    EquivalencyScanner.Collect(ctx.SemanticModel.GetTypeInfo(receiver, ct).Type, compilation, visited, declarations, ct);

                return new EquatableArray<EquivalencyTypeDeclaration>(declarations.ToArray());
            });

        // (b) Types explicitly opted in with [AssertEquivalency]
        var attributedProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            AssertEquivalencyAttribute,
            static (node, ct) => node is TypeDeclarationSyntax,
            static (ctx, ct) =>
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                var declarations = new List<EquivalencyTypeDeclaration>();
                if (ctx.TargetSymbol is INamedTypeSymbol type)
                    EquivalencyScanner.Collect(type, ctx.SemanticModel.Compilation, visited, declarations, ct);

                return new EquatableArray<EquivalencyTypeDeclaration>(declarations.ToArray());
            });

        var hasRuntimeProvider = context.CompilationProvider
            .Select(static (compilation, ct) => compilation.GetTypeByMetadataName(RegistryTypeName) is not null);

        var typesProvider = callSiteProvider.Collect()
            .Combine(attributedProvider.Collect())
            .Select(static (p, ct) => new EquatableArray<EquivalencyTypeDeclaration>(p.Left.Concat(p.Right)
                .SelectMany(static declarations => declarations)
                .DistinctBy(static declaration => declaration.TypeFullName)
                .OrderBy(static declaration => declaration.TypeFullName, StringComparer.Ordinal)
                .ToArray()));

        var sourceProvider = typesProvider
            .Join(hasRuntimeProvider)
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new EquivalencyRegistrationProducer(p.Item1, p.Item2, p.Item3.RootNamespace));

        context.ProduceCode(sourceProvider);
    }

    private static bool IsEquivalencyInvocation(InvocationExpressionSyntax invocation)
        => GetInvokedName(invocation.Expression) is "BeEquivalentTo" or "NotBeEquivalentTo";

    private static string? GetInvokedName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
        GenericNameSyntax generic => generic.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => null,
    };

    /// <summary>Returns <c>x</c> for <c>x.Should().BeEquivalentTo(...)</c>, otherwise null.</summary>
    private static ExpressionSyntax? GetShouldReceiver(InvocationExpressionSyntax invocation)
        => invocation.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax shouldInvocation }
            && GetInvokedName(shouldInvocation.Expression) == "Should"
            && shouldInvocation.Expression is MemberAccessExpressionSyntax shouldAccess
            ? shouldAccess.Expression
            : null;
}
