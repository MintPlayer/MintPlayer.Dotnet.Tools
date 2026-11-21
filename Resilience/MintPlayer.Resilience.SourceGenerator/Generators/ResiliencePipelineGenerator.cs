using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MintPlayer.Resilience.SourceGenerator.Models;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.Resilience.SourceGenerator.Generators;

/// <summary>
/// Compiles every <c>[ResiliencePipeline]</c> class into a flat pipeline (plan M4, spikes S1/S2/S6): one pooled
/// async method with retry, timeout and fallback inlined and their values folded in, and the strategies whose
/// state is intrinsic (breaker, limiters, chaos) driven through the runtime's own hooks. A pipeline with hedging
/// forwards to a runtime pipeline built once. All classes are written to one file, <c>ResiliencePipelines.g.cs</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public class ResiliencePipelineGenerator : IncrementalGenerator
{
    public override void Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)
    {
        var plain = context.SyntaxProvider.ForAttributeWithMetadataName(
            StrategySchema.PipelineAttribute,
            static (node, ct) => node is ClassDeclarationSyntax,
            static (ctx, ct) => ctx.TargetSymbol is INamedTypeSymbol type ? PipelineParser.Describe(type, ct) : null)
            .Where(static model => model is not null)
            .Select(static (model, ct) => model!);

        var generic = context.SyntaxProvider.ForAttributeWithMetadataName(
            StrategySchema.GenericPipelineAttribute,
            static (node, ct) => node is ClassDeclarationSyntax,
            static (ctx, ct) => ctx.TargetSymbol is INamedTypeSymbol type ? PipelineParser.Describe(type, ct) : null)
            .Where(static model => model is not null)
            .Select(static (model, ct) => model!);

        var pipelines = plain.Collect()
            .Combine(generic.Collect())
            .Select(static (pair, ct) => pair.Left.Concat(pair.Right)
                .Where(static model => model.SkipReason is null)
                .GroupBy(static model => model.FullName, StringComparer.Ordinal)
                .Select(static group => group.First())
                .OrderBy(static model => model.FullName, StringComparer.Ordinal)
                .ToEquatableArray());

        var sourceProvider = pipelines
            .Join(settingsProvider)
            .Select(static Producer (p, ct) => new ResiliencePipelineProducer(p.Item1, p.Item2.RootNamespace));

        context.ProduceCode(sourceProvider);

        // M4b: the analyzers MPR0004 (invalid combinations), MPR0005 (not awaited), MPR0006 (pooled ValueTask
        // misuse) and MPR0007 (strategy attributes split across partial declarations) are separate
        // DiagnosticAnalyzers in Diagnostics/. The generator itself reports nothing: a class it cannot generate
        // carries a SkipReason and is left out, and the analyzers explain why.
    }
}
