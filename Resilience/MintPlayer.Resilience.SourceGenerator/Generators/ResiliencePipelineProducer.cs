using System.CodeDom.Compiler;
using MintPlayer.Resilience.SourceGenerator.Models;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.Resilience.SourceGenerator.Generators;

/// <summary>Writes every <c>[ResiliencePipeline]</c> class of the compilation to <c>ResiliencePipelines.g.cs</c>.</summary>
internal sealed class ResiliencePipelineProducer : Producer
{
    public const string HintName = "ResiliencePipelines.g.cs";

    private readonly EquatableArray<PipelineModel> _pipelines;

    public ResiliencePipelineProducer(EquatableArray<PipelineModel> pipelines, string? rootNamespace)
        : base(string.IsNullOrWhiteSpace(rootNamespace) ? "MintPlayer.Resilience.Generated" : rootNamespace!, HintName)
    {
        _pipelines = pipelines;
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        if (_pipelines.Count == 0)
        {
            return;
        }

        writer.WriteLine(Header);
        writer.WriteLine("#nullable enable");
        writer.WriteLine();

        foreach (var group in _pipelines.GroupBy(p => p.Namespace).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(group.Key))
            {
                foreach (var pipeline in group)
                {
                    new PipelineEmitter(writer, pipeline).Emit();
                }
            }
            else
            {
                using (writer.OpenBlock($"namespace {group.Key}"))
                {
                    var first = true;
                    foreach (var pipeline in group)
                    {
                        if (!first)
                        {
                            writer.WriteLine();
                        }

                        first = false;
                        new PipelineEmitter(writer, pipeline).Emit();
                    }
                }
            }

            writer.WriteLine();
        }
    }
}
