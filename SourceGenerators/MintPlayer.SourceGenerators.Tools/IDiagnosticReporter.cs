using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools;

public interface IDiagnosticReporter
{
    IEnumerable<Diagnostic> GetDiagnostics();
    //protected abstract void SuggestCodeFixes(CancellationToken cancellationToken);
}
