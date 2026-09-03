using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace MintPlayer.SourceGenerators.Testing;

/// <summary>
/// Runs a source generator, analyzer or code-fix provider against in-memory C# and hands back what
/// it produced — from the assembly sitting in the <em>test host's own output directory</em>, so a
/// coverage collector attributes the component's code.
/// </summary>
/// <remarks>
/// <para>
/// The load path is the entire reason this type exists. Coverlet and friends rewrite IL on disk in
/// the test project's output directory. Roslyn, given an <c>OutputItemType="Analyzer"</c> reference,
/// loads the component through <c>AnalyzerFileReference</c> from the <em>generator's</em> bin —
/// which the collector never touched. The generator runs, the tests pass, and coverage is exactly
/// zero no matter how many tests there are. Every approach that goes through the real compiler has
/// this property: MSBuild, <c>MSBuildWorkspace</c>, and building a fixture project all report
/// nothing.
/// </para>
/// <para>
/// So the component must be copied next to the test assembly and loaded from there, by name, into
/// the DEFAULT load context. The <c>CopyComponentUnderTest</c> MSBuild target shipped in this
/// package does the copying; <see cref="ForAssembly"/> does the loading.
/// </para>
/// <para>
/// Configure once per test class and reuse — the reference set and the loaded assembly are the
/// expensive parts:
/// <code>
/// private static readonly GeneratorHarness Harness = GeneratorHarness
///     .ForAssembly("Acme.Generators")
///     .AddReference&lt;JsonSerializer&gt;();
/// </code>
/// </para>
/// </remarks>
public sealed class GeneratorHarness
{
    private readonly string _assemblyName;
    private readonly ImmutableArray<Type> _referenceTypes;
    private readonly string? _rootNamespace;

    private static readonly object _loadLock = new();
    private static readonly Dictionary<string, Assembly> _loaded = new(StringComparer.Ordinal);

    private GeneratorHarness(string assemblyName, ImmutableArray<Type> referenceTypes, string? rootNamespace)
    {
        _assemblyName = assemblyName;
        _referenceTypes = referenceTypes;
        _rootNamespace = rootNamespace;
    }

    /// <summary>
    /// Targets the component assembly with this simple name, resolved from the test host's output
    /// directory.
    /// </summary>
    /// <remarks>
    /// The assembly is not loaded until it is first needed, so a typo surfaces on the first run
    /// with a message naming the copy target rather than as a cryptic <see cref="FileNotFoundException"/>
    /// during class construction.
    /// </remarks>
    public static GeneratorHarness ForAssembly(string assemblyName)
        => new(assemblyName, [], "TestRoot");

    /// <summary>
    /// Adds the assembly containing <typeparamref name="T"/> to every compilation this harness
    /// builds, so fixtures can reference that API.
    /// </summary>
    /// <remarks>
    /// The BCL, <c>netstandard</c> and everything already loaded from <c>System.*</c> is included
    /// automatically. Use this for the libraries your fixtures actually name — the attribute
    /// package a generator triggers on, most commonly. A generator that finds no candidates in a
    /// fixture that looks correct is nearly always a missing reference here.
    /// </remarks>
    public GeneratorHarness AddReference<T>() => AddReferences(typeof(T));

    /// <summary>
    /// Adds the assemblies containing <paramref name="types"/>.
    /// </summary>
    /// <remarks>
    /// Use this rather than <see cref="AddReference{T}"/> when the obvious type to name is static —
    /// C# forbids a static class as a type argument, and the natural anchor for a library is very
    /// often its extension class.
    /// </remarks>
    public GeneratorHarness AddReferences(params Type[] types)
        => new(_assemblyName, _referenceTypes.AddRange(types), _rootNamespace);

    /// <summary>
    /// Sets <c>build_property.rootnamespace</c>, which producers typically use to place generated
    /// code. Defaults to <c>TestRoot</c>; pass <see langword="null"/> to test what a generator does
    /// when the host supplies nothing.
    /// </summary>
    public GeneratorHarness WithRootNamespace(string? rootNamespace)
        => new(_assemblyName, _referenceTypes, rootNamespace);

    #region Generators

    /// <summary>Runs one incremental generator over <paramref name="sources"/>.</summary>
    public GeneratorResult RunGenerator(string generatorTypeName, params string[] sources)
    {
        var generator = Instantiate<IIncrementalGenerator>(generatorTypeName);
        var compilation = BuildCompilation(sources);

        var driver = CreateDriver(generator, compilation, trackSteps: false)
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var diagnostics);

        var generated = driver.GetRunResult().GeneratedTrees
            .Select(t => new GeneratedSource(Path.GetFileName(t.FilePath), t.GetText().ToString()))
            .OrderBy(s => s.HintName, StringComparer.Ordinal)
            .ToImmutableArray();

        return new GeneratorResult(diagnostics, generated, updated);
    }

    /// <summary>
    /// Runs one generator twice over a shared driver — <paramref name="before"/> then
    /// <paramref name="after"/> — and reports what the second run reused.
    /// </summary>
    /// <remarks>
    /// A single run cannot tell a correctly-cached pipeline from one that recomputes everything on
    /// every keystroke: both emit identical output. Only a second run exercises the pipeline's
    /// equality comparers, and therefore only a second run can catch a comparer that reports
    /// "changed" for an edit the generator does not care about — a real performance defect in
    /// every consuming IDE, invisible to every other kind of test.
    /// </remarks>
    public IncrementalGeneratorResult RunGeneratorTwice(
        string generatorTypeName,
        string[] before,
        string[] after)
    {
        var generator = Instantiate<IIncrementalGenerator>(generatorTypeName);
        var first = BuildCompilation(before);
        var second = BuildCompilation(after);

        var driver = CreateDriver(generator, first, trackSteps: true)
            .RunGeneratorsAndUpdateCompilation(first, out _, out _);
        var firstResult = driver.GetRunResult().Results.Single();

        driver = driver.RunGeneratorsAndUpdateCompilation(second, out _, out _);
        var secondResult = driver.GetRunResult().Results.Single();

        return new IncrementalGeneratorResult(firstResult, secondResult);
    }

    private GeneratorDriver CreateDriver(IIncrementalGenerator generator, Compilation compilation, bool trackSteps)
        => CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
            optionsProvider: new StubAnalyzerConfigOptionsProvider(_rootNamespace),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: trackSteps));

    #endregion

    #region Analyzers

    /// <summary>
    /// Runs one analyzer and returns only the diagnostics it declares.
    /// </summary>
    /// <remarks>
    /// Filtering to the analyzer's own <see cref="DiagnosticAnalyzer.SupportedDiagnostics"/> keeps
    /// unrelated compile errors out of the assertion, so a test fails for the reason it names
    /// rather than because a fixture was missing a using.
    /// </remarks>
    public async Task<IReadOnlyList<Diagnostic>> RunAnalyzerAsync(string analyzerTypeName, params string[] sources)
    {
        var analyzer = Instantiate<DiagnosticAnalyzer>(analyzerTypeName);

        var diagnostics = await BuildCompilation(sources)
            .WithAnalyzers([analyzer])
            .GetAnalyzerDiagnosticsAsync(default);

        var ownIds = analyzer.SupportedDiagnostics.Select(d => d.Id).ToImmutableHashSet(StringComparer.Ordinal);
        return diagnostics.Where(d => ownIds.Contains(d.Id)).ToList();
    }

    /// <summary>The descriptors an analyzer advertises, without running it.</summary>
    public ImmutableArray<DiagnosticDescriptor> DescriptorsOf(string analyzerTypeName)
        => Instantiate<DiagnosticAnalyzer>(analyzerTypeName).SupportedDiagnostics;

    #endregion

    #region Code fixes

    /// <summary>
    /// Runs <paramref name="analyzerTypeName"/>, hands its first fixable diagnostic to
    /// <paramref name="codeFixTypeName"/>, applies the resulting change and returns the fixed text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not <c>Microsoft.CodeAnalysis.CSharp.CodeFix.Testing</c>. That package works and
    /// its <c>{|MP001:...|}</c> markup is nicer, but it pulls in NuGet.Common/Packaging/Protocol,
    /// Microsoft.VisualStudio.Composition and DiffPlex, resolves targeting packs over the network at
    /// TEST time, and runs several times slower per test. One harness for generators, analyzers and
    /// fixes beats two.
    /// </para>
    /// <para>
    /// Only the first fixable diagnostic is offered, matching Roslyn: <see cref="CodeFixContext"/>
    /// validates that every diagnostic handed to it shares the requested span, so a provider can
    /// never see diagnostics from elsewhere in the file. Document-wide behaviour belongs to a
    /// FixAll provider and needs a different test.
    /// </para>
    /// <para>
    /// A fix that declines to register an action is a normal outcome, not an error — the result
    /// reports <see cref="CodeFixResult.Applied"/> <see langword="false"/> and returns the source
    /// unchanged, so "offers nothing here" is something a test can assert directly.
    /// </para>
    /// </remarks>
    public async Task<CodeFixResult> ApplyCodeFixAsync(
        string analyzerTypeName,
        string codeFixTypeName,
        string source)
    {
        var analyzer = Instantiate<DiagnosticAnalyzer>(analyzerTypeName);
        var codeFix = Instantiate<CodeFixProvider>(codeFixTypeName);

        using var workspace = new AdhocWorkspace();

        var projectId = ProjectId.CreateNewId("FixInput");
        var documentId = DocumentId.CreateNewId(projectId, "Input.cs");

        // filePath matters, and its absence is not neutral. Without it Document.FilePath is null
        // while the syntax tree's is empty, so a fix that locates a sibling document by
        // `d.FilePath == someLocation.SourceTree?.FilePath` — the normal way to reach the file a
        // symbol is declared in — matches nothing and returns the solution unchanged. It looks
        // exactly like a fix that declined to offer anything, which is a legal outcome, so the
        // test passes and the entire body of the fix stays unreachable. A real workspace always
        // has paths; a harness without them cannot exercise that whole class of code fix.
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "FixInput", "FixInput", LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(projectId, MetadataReferences())
            .AddDocument(documentId, "Input.cs", SourceText.From(source), filePath: "Input.cs");

        var compilation = (await solution.GetProject(projectId)!.GetCompilationAsync())!;

        var diagnostics = await compilation
            .WithAnalyzers([analyzer])
            .GetAnalyzerDiagnosticsAsync(default);

        var fixableIds = codeFix.FixableDiagnosticIds.ToImmutableHashSet(StringComparer.Ordinal);

        // Ordered by position, because GetAnalyzerDiagnosticsAsync does not promise an order and
        // "the first fixable diagnostic" has to mean something stable. Without this the harness
        // picks an arbitrary one, tests pass or fail depending on analyzer internals, and the
        // failure looks like a broken code fix rather than a coin toss.
        var fixable = diagnostics
            .Where(d => fixableIds.Contains(d.Id))
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToImmutableArray();

        if (fixable.Length == 0)
            return new CodeFixResult(diagnostics, source, Applied: false);

        var actions = new List<CodeAction>();
        await codeFix.RegisterCodeFixesAsync(new CodeFixContext(
            solution.GetDocument(documentId)!, fixable[0], (action, _) => actions.Add(action), default));

        if (actions.Count == 0)
            return new CodeFixResult(diagnostics, source, Applied: false);

        var operations = await actions[0].GetOperationsAsync(default);

        // FirstOrDefault, not Single. A CodeAction is not obliged to produce exactly one
        // ApplyChangesOperation — it may produce none (it only opens a document, say) or several.
        // Single() would throw "Sequence contains no matching element" from a method whose
        // documented contract is that "offers nothing here" is a normal, assertable outcome.
        var changed = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (changed is null)
            return new CodeFixResult(diagnostics, source, Applied: false, actions[0].Title);

        var fixedText = (await changed.ChangedSolution.GetDocument(documentId)!.GetTextAsync()).ToString();

        return new CodeFixResult(diagnostics, fixedText, Applied: true, actions[0].Title);
    }

    /// <summary>Every code-fix provider in the component that offers a fix for <paramref name="diagnosticId"/>.</summary>
    /// <remarks>
    /// Useful as a completeness check: a rule that ships without a fix is easy to introduce and
    /// hard to notice.
    /// </remarks>
    public IReadOnlyList<Type> CodeFixProvidersFor(string diagnosticId)
        => LoadableTypes()
            .Where(t => !t.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(t))
            .Where(t => ((CodeFixProvider)Activator.CreateInstance(t)!).FixableDiagnosticIds.Contains(diagnosticId))
            .ToList();

    #endregion

    #region Loading the component

    private T Instantiate<T>(string typeName) where T : class
    {
        // !IsAbstract matters: naming an abstract base otherwise gets past this lookup and dies in
        // Activator.CreateInstance with a bare MissingMethodException, skipping the message below.
        var type = LoadableTypes()
            .FirstOrDefault(t => t.Name == typeName && typeof(T).IsAssignableFrom(t) && !t.IsAbstract);

        if (type is null)
        {
            var candidates = LoadableTypes().Where(t => typeof(T).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

            // "None at all" almost never means a wrong assembly name — the load would have failed
            // outright. It usually means GetTypes partially failed: a type whose BASE class lives
            // in an assembly the test project does not reference is silently dropped, while its
            // siblings that have no such dependency load fine and hide the problem.
            throw new ComponentTypeNotFoundException(
                $"'{typeName}' was not found as a concrete {typeof(T).Name} in '{_assemblyName}'. " +
                (candidates.Count == 0
                    ? $"No {typeof(T).Name} loaded from that assembly at all. If it definitely contains one, " +
                      $"a dependency is missing: types whose base class or interface cannot be resolved are " +
                      $"dropped silently. Reference the assemblies the component is built against — most " +
                      $"often the package providing its generator base class — and try again."
                    : $"Available: {string.Join(", ", candidates)}."));
        }

        return (T)Activator.CreateInstance(type)!;
    }

    /// <remarks>
    /// <see cref="Assembly.GetTypes"/> throws if ANY type fails to load, and one missing optional
    /// dependency loses the whole assembly. Keep whatever did load: a component whose code fixes
    /// cannot be resolved should still allow its generators to be tested.
    /// </remarks>
    private IEnumerable<Type> LoadableTypes()
    {
        try
        {
            return Component.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Cast<Type>();
        }
    }

    private Assembly Component
    {
        get
        {
            lock (_loadLock)
            {
                if (_loaded.TryGetValue(_assemblyName, out var cached)) return cached;

                try
                {
                    // Assembly.Load, NOT LoadFrom or a custom AssemblyLoadContext: this resolves
                    // through normal probing to the copy in the test output directory, which is the
                    // instrumented one. The others would run un-instrumented code.
                    return _loaded[_assemblyName] = Assembly.Load(new AssemblyName(_assemblyName));
                }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    throw new InvalidOperationException(
                        $"Could not load '{_assemblyName}' from the test output directory. It has to be copied " +
                        $"there for coverage to attribute; reference the component project with " +
                        $"ReferenceOutputAssembly=\"false\" and list it in @(ComponentUnderTest) so this " +
                        $"package's CopyComponentUnderTest target copies the DLL and PDB into $(OutputPath). " +
                        $"See the MintPlayer.SourceGenerators.Testing README.", ex);
                }
            }
        }
    }

    #endregion

    private CSharpCompilation BuildCompilation(IReadOnlyList<string> sources)
    {
        // A generator run over nothing is a legitimate test (it should emit nothing and not throw),
        // but Roslyn needs at least one tree to take parse options from.
        var effective = sources.Count == 0 ? ["// intentionally empty"] : sources;

        var trees = effective
            .Select((src, i) => CSharpSyntaxTree.ParseText(src, path: $"Source{i}.cs"))
            .ToList();

        return CSharpCompilation.Create(
            assemblyName: "TestInput",
            syntaxTrees: trees,
            references: MetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private IReadOnlyList<MetadataReference> MetadataReferences()
    {
        var assemblies = new HashSet<Assembly>
        {
            typeof(object).Assembly,
            typeof(List<>).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Task).Assembly,
        };

        // Everything the test host already loaded from the BCL. Cheaper and far more robust than
        // resolving a reference assembly pack, which needs the network on a cold machine.
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.IsDynamic || string.IsNullOrEmpty(a.Location)) continue;
            var name = a.GetName().Name;
            if (name is null) continue;
            if (name.StartsWith("System.", StringComparison.Ordinal) || name is "netstandard" or "mscorlib" or "System")
                assemblies.Add(a);
        }

        foreach (var t in _referenceTypes)
            assemblies.Add(t.Assembly);

        return assemblies.Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)).ToList();
    }
}
