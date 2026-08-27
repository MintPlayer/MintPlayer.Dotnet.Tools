using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace MintPlayer.SourceGenerators.Tests._Infrastructure;

/// <summary>
/// Runs an analyzer over an <see cref="AdhocWorkspace"/> document, hands its diagnostics to a
/// <see cref="CodeFixProvider"/>, applies the resulting operations, and returns the fixed text.
/// </summary>
/// <remarks>
/// Deliberately NOT Microsoft.CodeAnalysis.CSharp.CodeFix.Testing. That package works (verified
/// at 1.1.4 against Roslyn 5.3.0) and its <c>{|MP001:...|}</c> markup is nicer, but it drags in
/// NuGet.Common/Packaging/Protocol/Resolver, Microsoft.VisualStudio.Composition and DiffPlex,
/// resolves targeting packs over the network at TEST time, and runs several times slower per
/// test. MintPlayer.Spark does not use it either. One harness for generators, analyzers and
/// fixes beats two.
///
/// The analyzer and fix types are loaded the same way as the generators — by name, from the
/// test output directory — so their coverage attributes.
/// </remarks>
internal static class CodeFixHarness
{
    private const string ProjectName = "FixInput";

    public static async Task<CodeFixResult> ApplyAsync(
        string analyzerTypeName,
        string codeFixTypeName,
        string source,
        string? analyzerAssemblyName = null,
        IEnumerable<Type>? referenceTypes = null)
    {
        var analyzer = Instantiate<DiagnosticAnalyzer>(analyzerTypeName, analyzerAssemblyName);
        var codeFix = Instantiate<CodeFixProvider>(codeFixTypeName, analyzerAssemblyName);

        using var workspace = new AdhocWorkspace();

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.ComponentModel.DescriptionAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Text.StringBuilder).Assembly.Location),
        };

        foreach (var t in referenceTypes ?? [])
            references.Add(MetadataReference.CreateFromFile(t.Assembly.Location));

        var projectId = ProjectId.CreateNewId(ProjectName);
        var documentId = DocumentId.CreateNewId(projectId, "Input.cs");

        var solution = workspace.CurrentSolution
            .AddProject(projectId, ProjectName, ProjectName, LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId,
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(projectId, references)
            .AddDocument(documentId, "Input.cs", SourceText.From(source));

        var project = solution.GetProject(projectId)!;
        var compilation = (await project.GetCompilationAsync())!;

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync(default);

        var fixableIds = codeFix.FixableDiagnosticIds.ToHashSet(StringComparer.Ordinal);
        var fixable = diagnostics.Where(d => fixableIds.Contains(d.Id)).ToImmutableArray();

        if (fixable.Length == 0)
            return new CodeFixResult(diagnostics, source, Applied: false);

        var document = solution.GetDocument(documentId)!;
        var actions = new List<CodeAction>();

        // One diagnostic, matching what Roslyn does: CodeFixContext validates that every
        // diagnostic handed to it shares the requested span, so a provider can never see
        // diagnostics from elsewhere in the file. Anything document-wide has to come from the
        // FixAll provider instead.
        await codeFix.RegisterCodeFixesAsync(new CodeFixContext(
            document, fixable[0], (action, _) => actions.Add(action), default));

        if (actions.Count == 0)
            return new CodeFixResult(diagnostics, source, Applied: false);

        var operations = await actions[0].GetOperationsAsync(default);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;

        var fixedText = (await changed.GetDocument(documentId)!.GetTextAsync()).ToString();

        return new CodeFixResult(diagnostics, fixedText, Applied: true, actions[0].Title);
    }

    /// <summary>Diagnostics only, without applying any fix.</summary>
    public static async Task<IReadOnlyList<Diagnostic>> DiagnoseAsync(
        string analyzerTypeName,
        string source,
        string? analyzerAssemblyName = null,
        IEnumerable<Type>? referenceTypes = null)
        => (await ApplyAsync(analyzerTypeName, analyzerTypeName, source, analyzerAssemblyName, referenceTypes))
            .Diagnostics;

    private static T Instantiate<T>(string typeName, string? assemblyName) where T : class
    {
        string[] candidates = assemblyName is null
            ? ["MintPlayer.SourceGenerators", "MintPlayer.Mapper", "MintPlayer.CliGenerator", "MintPlayer.ValueComparerGenerator"]
            : [assemblyName];

        foreach (var candidate in candidates)
        {
            var assembly = Assembly.Load(new AssemblyName(candidate));

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray(); }

            var type = types.FirstOrDefault(t => t.Name == typeName && typeof(T).IsAssignableFrom(t));
            if (type is not null) return (T)Activator.CreateInstance(type)!;
        }

        throw new InvalidOperationException($"'{typeName}' not found as a {typeof(T).Name}.");
    }
}

internal sealed record CodeFixResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    string FixedSource,
    bool Applied,
    string? ActionTitle = null)
{
    public IReadOnlyList<Diagnostic> Of(string id)
        => Diagnostics.Where(d => d.Id == id).ToList();
}
