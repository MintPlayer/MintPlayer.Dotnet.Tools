using Microsoft.CodeAnalysis;
using MintPlayer.SourceGenerators.Testing;

namespace MintPlayer.SourceGenerators.Tests._Infrastructure;

/// <summary>
/// Analyzer-plus-code-fix runs for this solution folder's four components.
/// </summary>
/// <remarks>
/// A thin adapter over MintPlayer.SourceGenerators.Testing, kept so the existing tests keep their
/// call shape. The workspace plumbing, the single-diagnostic rule Roslyn enforces on
/// <c>CodeFixContext</c>, and the decision to avoid Microsoft.CodeAnalysis.Testing all live in the
/// package now.
/// </remarks>
internal static class CodeFixHarness
{
    public static Task<CodeFixResult> ApplyAsync(
        string analyzerTypeName,
        string codeFixTypeName,
        string source,
        string? analyzerAssemblyName = null,
        IEnumerable<Type>? referenceTypes = null)
        => Harness(analyzerAssemblyName, referenceTypes)
            .ApplyCodeFixAsync(analyzerTypeName, codeFixTypeName, source);

    /// <summary>Diagnostics only, without applying any fix.</summary>
    public static async Task<IReadOnlyList<Diagnostic>> DiagnoseAsync(
        string analyzerTypeName,
        string source,
        string? analyzerAssemblyName = null,
        IEnumerable<Type>? referenceTypes = null)
        => (await ApplyAsync(analyzerTypeName, analyzerTypeName, source, analyzerAssemblyName, referenceTypes))
            .Diagnostics;

    /// <remarks>
    /// Unlike the generator side there is no probing: every analyzer and code fix reachable from
    /// these tests lives in MintPlayer.SourceGenerators, and a caller with something else passes
    /// the assembly name explicitly.
    /// </remarks>
    private static Testing.GeneratorHarness Harness(string? assemblyName, IEnumerable<Type>? referenceTypes)
    {
        var harness = Testing.GeneratorHarness
            .ForAssembly(assemblyName ?? "MintPlayer.SourceGenerators")
            .AddReferences(
                typeof(System.ComponentModel.DescriptionAttribute),
                typeof(System.Text.StringBuilder));

        var extra = referenceTypes?.ToArray() ?? [];
        return extra.Length == 0 ? harness : harness.AddReferences(extra);
    }
}
