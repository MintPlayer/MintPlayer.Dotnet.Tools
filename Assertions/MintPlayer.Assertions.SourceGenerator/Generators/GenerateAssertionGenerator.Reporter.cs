using Microsoft.CodeAnalysis;
using MintPlayer.Assertions.SourceGenerator.Diagnostics;
using MintPlayer.Assertions.SourceGenerator.Models;
using MintPlayer.SourceGenerators.Tools;

namespace MintPlayer.Assertions.SourceGenerator.Generators;

/// <summary>Reports MPAG001 for [GenerateAssertion] methods whose shape cannot be generated.</summary>
internal sealed class UnsupportedAssertionReporter : IDiagnosticReporter
{
    private readonly EquatableArray<AssertionMethodDeclaration> declarations;

    public UnsupportedAssertionReporter(EquatableArray<AssertionMethodDeclaration> declarations)
    {
        this.declarations = declarations;
    }

    public IEnumerable<Diagnostic> GetDiagnostics(Compilation compilation)
    {
        foreach (var declaration in declarations)
        {
            yield return DiagnosticRules.UnsupportedGenerateAssertionRule.Create(
                declaration.Location.ToLocation(compilation),
                $"{declaration.ContainingTypeFullName}.{declaration.MethodName}",
                declaration.Diagnostic!);
        }
    }
}
