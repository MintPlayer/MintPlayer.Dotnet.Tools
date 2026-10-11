using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MintPlayer.SourceGenerators.Tests._Infrastructure;

/// <summary>
/// Checks on the generated files of a run only, for fixtures whose own code is deliberately incomplete (a
/// <c>[GeneratedRegex]</c> partial without the regex generator, a CS0592 attribute target) or that check warnings.
/// </summary>
internal static class GeneratedCodeChecks
{
    private static bool IsGenerated(Diagnostic d)
        => d.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Errors located in a generated file.</summary>
    public static IReadOnlyList<Diagnostic> GeneratedErrors(this GeneratorRun run)
        => run.Errors.Where(IsGenerated).ToList();

    /// <summary>
    /// Warnings and errors in the generated files when the output is compiled with <c>#nullable enable</c> for the
    /// whole project, as most consumers do.
    /// </summary>
    public static IReadOnlyList<Diagnostic> GeneratedWarningsUnderNullable(this GeneratorRun run)
    {
        var options = (CSharpCompilationOptions)run.UpdatedCompilation.Options;
        var compilation = run.UpdatedCompilation.WithOptions(options.WithNullableContextOptions(NullableContextOptions.Enable));
        return compilation.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning && IsGenerated(d))
            .ToList();
    }

    /// <summary>The type with this metadata name in the run's output compilation.</summary>
    public static INamedTypeSymbol? Type(this GeneratorRun run, string metadataName)
        => run.UpdatedCompilation.GetTypeByMetadataName(metadataName);

    /// <summary>
    /// A <c>[GeneratedRegex]</c> partial method in a record, a record struct, a struct and an interface: code that has
    /// nothing to do with the generator under test and crashed one of them (#193).
    /// </summary>
    public const string UnrelatedPartials = """
        namespace Unrelated
        {
            using System.Text.RegularExpressions;

            public partial record R { [GeneratedRegex("a")] private static partial Regex Rx(); }
            public partial record struct RS { [GeneratedRegex("b")] private static partial Regex Rx(); }
            public partial struct S { [GeneratedRegex("c")] private static partial Regex Rx(); }
            public partial interface I { [GeneratedRegex("d")] private static partial Regex Rx(); }
        }
        """;
}
