using Microsoft.CodeAnalysis;

namespace MintPlayer.SourceGenerators.Tools;

public interface IDiagnosticReporter
{
    IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation);
    //protected abstract void SuggestCodeFixes(CancellationToken cancellationToken);
}
