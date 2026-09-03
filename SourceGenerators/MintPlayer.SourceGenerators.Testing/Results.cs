using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MintPlayer.SourceGenerators.Testing;

/// <summary>One file a generator emitted.</summary>
public sealed record GeneratedSource(string HintName, string Source);

/// <summary>What a single generator run produced.</summary>
public sealed record GeneratorResult(
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<GeneratedSource> GeneratedSources,
    Compilation UpdatedCompilation)
{
    /// <summary>
    /// Errors from the generator itself <em>plus</em> errors in the code it produced.
    /// </summary>
    /// <remarks>
    /// Both halves matter and only the union is meaningful: a generator that emits uncompilable
    /// code reports no diagnostics of its own, so asserting on
    /// <see cref="Diagnostics"/> alone passes while the consumer's build breaks.
    /// </remarks>
    public ImmutableArray<Diagnostic> Errors
        => Diagnostics
            .Concat(UpdatedCompilation.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

    /// <summary>The errors, formatted for an assertion failure message.</summary>
    public string ErrorText => string.Join(Environment.NewLine, Errors.Select(d => d.ToString()));

    /// <summary>The source emitted under <paramref name="hintName"/>, or null if there was none.</summary>
    public string? SourceFor(string hintName)
        => GeneratedSources.FirstOrDefault(s => s.HintName == hintName)?.Source;

    /// <summary>Every generated file concatenated, for a "contains this member anywhere" check.</summary>
    public string AllSources => string.Join(Environment.NewLine, GeneratedSources.Select(s => s.Source));

    /// <summary>Diagnostics with the given id, whatever their severity.</summary>
    public ImmutableArray<Diagnostic> Of(string diagnosticId)
        => Diagnostics.Where(d => d.Id == diagnosticId).ToImmutableArray();
}

/// <summary>
/// The two runs from <see cref="GeneratorHarness.RunGeneratorTwice"/>, with helpers for asking what
/// the second one reused.
/// </summary>
public sealed record IncrementalGeneratorResult(
    GeneratorRunResult First,
    GeneratorRunResult Second)
{
    /// <summary>
    /// Every run reason recorded against <paramref name="stepName"/> on the second run.
    /// </summary>
    /// <remarks>
    /// A step whose inputs compared equal reports <see cref="IncrementalStepRunReason.Cached"/> or
    /// <see cref="IncrementalStepRunReason.Unchanged"/>; one that recomputed reports
    /// <see cref="IncrementalStepRunReason.Modified"/> or <see cref="IncrementalStepRunReason.New"/>.
    /// </remarks>
    public IReadOnlyList<IncrementalStepRunReason> ReasonsFor(string stepName)
        => Second.TrackedSteps.TryGetValue(stepName, out var steps)
            ? steps.SelectMany(s => s.Outputs).Select(o => o.Reason).ToList()
            : [];

    /// <summary>Names of every tracked step, for discovering what to assert on.</summary>
    public IReadOnlyList<string> StepNames
        => Second.TrackedSteps.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>True when nothing in <paramref name="stepName"/> had to be recomputed.</summary>
    /// <remarks>
    /// False for an unknown step name rather than throwing: step names are Roslyn implementation
    /// detail plus whatever the generator chose to name, and a test that asks about a step which
    /// no longer exists should fail on its assertion, not on a lookup.
    /// </remarks>
    public bool WasFullyCached(string stepName)
    {
        var reasons = ReasonsFor(stepName);
        return reasons.Count > 0
            && reasons.All(r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged);
    }

    /// <summary>Steps that were entirely served from cache on the second run.</summary>
    public IReadOnlyList<string> CachedSteps => StepNames.Where(WasFullyCached).ToList();

    /// <summary>True when both runs emitted byte-identical sources.</summary>
    public bool OutputUnchanged
        => First.GeneratedSources.Select(s => s.SourceText.ToString())
            .SequenceEqual(Second.GeneratedSources.Select(s => s.SourceText.ToString()));
}

/// <summary>The outcome of offering a code fix.</summary>
public sealed record CodeFixResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    string FixedSource,
    bool Applied,
    string? ActionTitle = null)
{
    /// <summary>Diagnostics with the given id.</summary>
    public IReadOnlyList<Diagnostic> Of(string diagnosticId)
        => Diagnostics.Where(d => d.Id == diagnosticId).ToList();
}
