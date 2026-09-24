using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MintPlayer.SourceGenerators.Testing;

/// <summary>
/// The named component is not in the assembly the harness loaded.
/// </summary>
/// <remarks>
/// A distinct type so callers that probe several assemblies can catch "not here, try the next one"
/// without also swallowing an <see cref="InvalidOperationException"/> thrown from inside a
/// generator run — which would hide the real failure behind a misleading "type not found".
/// </remarks>
public sealed class ComponentTypeNotFoundException(string message) : InvalidOperationException(message);

/// <summary>
/// A code-fix fixture is not a valid test subject: it does not compile, or the fix claimed success
/// while changing nothing.
/// </summary>
/// <remarks>
/// <para>
/// Both conditions are silent by nature, which is why they get an exception rather than a flag on
/// the result. A fixture that does not compile produces no analyzer diagnostics, so the harness
/// reports <see cref="CodeFixResult.Applied"/> <see langword="false"/> — indistinguishable from a
/// fix that correctly declined, and every "offers nothing here" assertion passes for the wrong
/// reason. A fix that returns its solution unmodified is wrapped by Roslyn in a perfectly valid
/// <c>ApplyChangesOperation</c>, so it reads as success.
/// </para>
/// <para>
/// Both have happened in this repo. The second is why <c>filePath:</c> had to be added to the
/// harness in d73d877: without it a fix that located a sibling document by file path matched
/// nothing and returned the solution unchanged, and the whole body of the fix stayed unreachable
/// while its tests passed.
/// </para>
/// </remarks>
public sealed class FixtureNotUsableException(string message) : InvalidOperationException(message);

/// <summary>
/// One project in a code-fix fixture, and the sources it contains.
/// </summary>
/// <remarks>
/// Projects are supplied in dependency order and each references all of its predecessors, which is
/// what lets a fixture express the case a cross-project code fix exists for: the diagnostic is
/// reported in the last project, and the fix edits a document in an earlier one.
/// </remarks>
public sealed record FixtureProject(string Name, IReadOnlyList<(string FileName, string Source)> Files)
{
    /// <summary>A project with several named sources.</summary>
    public static FixtureProject Of(string name, params (string FileName, string Source)[] files)
        => new(name, files);

    /// <summary>A project with a single source, named <c>Input.cs</c>.</summary>
    public static FixtureProject Of(string name, string source)
        => new(name, [("Input.cs", source)]);
}

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

    /// <summary>
    /// Every run reason recorded against the generator's output steps (<c>RegisterSourceOutput</c> and
    /// friends) on the second run.
    /// </summary>
    /// <remarks>
    /// This is the step that matters. A pipeline can report cache hits on every syntax step and still
    /// regenerate every file on every keystroke if anything between the comparers and the output —
    /// a <c>Combine</c> with the <c>Compilation</c>, a model compared by reference — lets a new value
    /// through. Asserting on <see cref="CachedSteps"/> alone cannot see that; asserting here can.
    /// </remarks>
    public IReadOnlyList<IncrementalStepRunReason> OutputReasons
        => Second.TrackedOutputSteps.Values
            .SelectMany(steps => steps)
            .SelectMany(s => s.Outputs)
            .Select(o => o.Reason)
            .ToList();

    /// <summary>
    /// True when the second run reused every output step. False when there were no output steps at all,
    /// so a generator that stopped registering output cannot pass vacuously.
    /// </summary>
    public bool OutputsFullyCached
        => OutputReasons.Count > 0
            && OutputReasons.All(r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged);

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
    string? ActionTitle = null,
    IReadOnlyDictionary<string, string>? Documents = null,
    IReadOnlyList<string>? ActionTitles = null,
    IReadOnlyList<Diagnostic>? FixedErrors = null)
{
    /// <summary>
    /// Compile errors in the solution the fix produced, across every project.
    /// </summary>
    /// <remarks>
    /// A code fix that emits uncompilable code reports no diagnostic of its own, so asserting only
    /// on the fixed text passes while the consumer's build breaks. Cross-project matters here: a
    /// fix that edits an interface in one project can break the class implementing it in another,
    /// which is exactly what a single-project fixture cannot show.
    /// </remarks>
    public IReadOnlyList<Diagnostic> Errors => FixedErrors ?? [];

    /// <summary>The fixed-code errors, formatted for an assertion failure message.</summary>
    public string ErrorText => Errors.Count == 0
        ? "no errors"
        : string.Join(Environment.NewLine, Errors.Select(d => "  " + d));

    /// <summary>Diagnostics with the given id.</summary>
    public IReadOnlyList<Diagnostic> Of(string diagnosticId)
        => Diagnostics.Where(d => d.Id == diagnosticId).ToList();

    /// <summary>
    /// Every document in the fixed solution, keyed <c>"{project}/{file}"</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="FixedSource"/> carries only the document the diagnostic was reported in, which is
    /// the wrong one to assert on for a fix whose entire purpose is to edit a file elsewhere.
    /// </remarks>
    public string Document(string key)
        => Documents is not null && Documents.TryGetValue(key, out var text)
            ? text
            : throw new KeyNotFoundException(
                $"No document '{key}' in the fixed solution. Available: " +
                (Documents is null or { Count: 0 }
                    ? "(none — this result came from an overload that does not capture documents)"
                    : string.Join(", ", Documents.Keys.OrderBy(k => k, StringComparer.Ordinal))));

    /// <summary>
    /// The titles of every action the provider offered, in registration order.
    /// </summary>
    /// <remarks>
    /// A provider may legitimately offer several actions for one diagnostic — one per interface a
    /// member could be added to, say. Asserting on the set of titles is how a test pins down that
    /// the choice is offered at all, which <see cref="ActionTitle"/> alone cannot express.
    /// </remarks>
    public IReadOnlyList<string> Titles => ActionTitles ?? (ActionTitle is null ? [] : [ActionTitle]);
}
