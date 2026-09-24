using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools;

public interface IDiagnosticReporter
{
    IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation);
    //protected abstract void SuggestCodeFixes(CancellationToken cancellationToken);
}

/// <summary>
/// An <see cref="IDiagnosticReporter"/> that can say, without a <see cref="Compilation"/>, whether it has
/// anything to report.
/// </summary>
/// <remarks>
/// Reporting needs the compilation to turn a stored <see cref="LocationKey"/> back into an in-tree
/// <see cref="Location"/> — a location rebuilt from a file path is accepted by the driver, but
/// <c>#pragma warning disable</c> and per-file <c>.editorconfig</c> severity silently stop applying to it.
/// The compilation is a new object on every edit, though, so a reporter combined with it re-runs on every
/// keystroke. <see cref="GeneratorExtensions.ReportDiagnostics"/> uses <see cref="HasDiagnostics"/> to
/// keep a reporter with nothing to say out of that combine altogether; in the common case no step runs.
/// </remarks>
public interface IConditionalDiagnosticReporter : IDiagnosticReporter
{
    /// <summary>
    /// False only when <see cref="IDiagnosticReporter.GetDiagnostics"/> would return nothing. Computed
    /// from the reporter's own data; must not need the compilation.
    /// </summary>
    bool HasDiagnostics { get; }
}
