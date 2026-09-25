using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.ValueComparerGenerator.Models;

namespace MintPlayer.ValueComparerGenerator.Generators;

/// <summary>
/// Generates <see cref="IEquatable{T}"/>, <c>Equals(object)</c> and <c>GetHashCode()</c> directly on every
/// <c>[GenerateEquality]</c> type and every type deriving from one, one file per type.
/// </summary>
/// <remarks>
/// Discovery takes two providers and never collects over all classes (PRD spike S5, strategy B):
/// <list type="bullet">
///   <item><c>ForAttributeWithMetadataName</c> for the attributed types, including records and structs. A type that
///   carries the attribute AND derives from another model comes from here too; <see cref="Discovery.Build"/> works
///   out its role from its base chain.</item>
///   <item>A syntax provider over classes and records with a base list, for the derived types without the attribute.
///   Its transform walks the base chain to the nearest attributed ancestor and returns null for everything else, so
///   property reading happens only for the few types that are models.</item>
/// </list>
/// Both return <see cref="DiscoveredType"/>, which is value-equatable and carries no symbol or location, so every
/// step caches under the default comparer.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public class ValueComparerGenerator : IncrementalGenerator
{
    /// <summary>The step that feeds the producers. Tests assert it stays cached when only a diagnostic moved.</summary>
    public const string ModelsStep = "ValueComparerGenerator.Models";

    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var rootsProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
                Discovery.GenerateEqualityMetadataName,
                static (node, ct) => node is TypeDeclarationSyntax,
                static (ctx, ct) => ctx.TargetSymbol is INamedTypeSymbol type
                    ? Discovery.Build(type, ctx.SemanticModel.Compilation, ct)
                    : null)
            .WithTrackingName("ValueComparerGenerator.Roots")
            .Where(static t => t is not null)
            .Select(static (t, ct) => t!);

        var derivedProvider = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, ct) => Discovery.IsDerivableDeclaration(node),
                static (ctx, ct) =>
                {
                    if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol type) return null;
                    // The roots provider owns every attributed type, including derived ones.
                    if (Discovery.HasGenerateEquality(type)) return null;
                    if (Discovery.FindHierarchyRoot(type) is null) return null;
                    if (!Discovery.IsOwningDeclaration(type, ctx.Node)) return null;
                    return Discovery.Build(type, ctx.SemanticModel.Compilation, ct);
                })
            .WithTrackingName("ValueComparerGenerator.Derived")
            .Where(static t => t is not null)
            .Select(static (t, ct) => t!);

        context.ProduceCode(Producers(rootsProvider));
        context.ProduceCode(Producers(derivedProvider));

        // Diagnostics are selected per type before collecting, so the collected array only changes when a
        // diagnostic does, and a compilation with nothing to report never combines with the compilation at all.
        var diagnosticsProvider = rootsProvider.Select(static (t, ct) => t.Diagnostics).Collect()
            .Combine(derivedProvider.Select(static (t, ct) => t.Diagnostics).Collect())
            .Select(static (p, ct) => p.Left.Concat(p.Right).SelectMany(d => d).ToEquatableArray())
            .Select(static IDiagnosticReporter (d, ct) => new ValueComparerDiagnosticReporter(d));

        context.ReportDiagnostics(diagnosticsProvider);
    }

    private static IncrementalValuesProvider<Producer> Producers(IncrementalValuesProvider<DiscoveredType> provider)
        => provider
            .Select(static (t, ct) => t.Model)
            .WithTrackingName(ModelsStep)
            .Where(static m => m is not null)
            .Select(static Producer (m, ct) => new Producers.EqualityProducer(m!));
}

internal sealed class ValueComparerDiagnosticReporter(EquatableArray<DiagnosticInfo> diagnostics) : IConditionalDiagnosticReporter
{
    public bool HasDiagnostics => !diagnostics.IsEmpty;

    public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation)
        => diagnostics.Select(d => ValueComparerDiagnostics.Create(
            d.Rule,
            d.Location?.ToLocation(compilation),
            d.MessageArgs.Cast<object>().ToArray()));
}
