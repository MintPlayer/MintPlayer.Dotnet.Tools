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
        return RunTwice(generator, BuildCompilation(before), BuildCompilation(after));
    }

    /// <summary>
    /// Runs one generator over <paramref name="sources"/>, then applies <paramref name="edit"/> to
    /// the source at <paramref name="editIndex"/> the way an IDE applies a keystroke, runs again on
    /// the same driver, and reports what the second run reused.
    /// </summary>
    /// <param name="generatorTypeName">Simple name of the <see cref="IIncrementalGenerator"/> to run.</param>
    /// <param name="sources">The files of the first compilation, named <c>Source0.cs</c>, <c>Source1.cs</c>, ….</param>
    /// <param name="editIndex">Which of <paramref name="sources"/> the edit applies to.</param>
    /// <param name="edit">Maps that file's text to its text after the edit.</param>
    /// <remarks>
    /// <para>
    /// Prefer this over <see cref="RunGeneratorTwice"/> for incrementality tests.
    /// <see cref="RunGeneratorTwice"/> parses the second compilation from scratch, so every syntax
    /// tree is new, including the files that did not change. An IDE never does that: it replaces the
    /// one edited tree with <c>Compilation.ReplaceSyntaxTree(old, old.WithChangedText(text))</c>,
    /// every other tree keeps its identity, and the driver skips the syntax transforms over those
    /// trees altogether. From-scratch parsing therefore both over-reports work (every file looks
    /// edited) and misses a class of defect: a pipeline that caches only because every tree was
    /// re-parsed identically, rather than because its comparers absorbed the change.
    /// </para>
    /// <para>
    /// The edited text is applied as the smallest single <see cref="TextChange"/> that turns the old
    /// text into the new one, not as a whole new document, so the parser reuses the unchanged parts
    /// of the tree exactly as it does for a keystroke.
    /// </para>
    /// </remarks>
    public IncrementalGeneratorResult RunKeystroke(
        string generatorTypeName,
        string[] sources,
        int editIndex,
        Func<string, string> edit)
    {
        if (edit is null) throw new ArgumentNullException(nameof(edit));
        if (editIndex < 0 || editIndex >= sources.Length)
            throw new ArgumentOutOfRangeException(
                nameof(editIndex), $"There are {sources.Length} source(s), so index {editIndex} names none of them.");

        var generator = Instantiate<IIncrementalGenerator>(generatorTypeName);
        var first = BuildCompilation(sources);

        var oldTree = first.SyntaxTrees.ElementAt(editIndex);
        var oldText = oldTree.GetText();
        var newText = oldText.WithChanges(SmallestChange(oldText.ToString(), edit(oldText.ToString())));
        var second = first.ReplaceSyntaxTree(oldTree, oldTree.WithChangedText(newText));

        return RunTwice(generator, first, second);
    }

    /// <summary>The single change that keeps the longest common prefix and suffix of the two texts.</summary>
    private static TextChange SmallestChange(string before, string after)
    {
        var prefix = 0;
        var max = Math.Min(before.Length, after.Length);
        while (prefix < max && before[prefix] == after[prefix]) prefix++;

        // The suffix may not overlap the prefix in either string.
        var suffix = 0;
        while (suffix < max - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix]) suffix++;

        return new TextChange(
            new TextSpan(prefix, before.Length - prefix - suffix),
            after.Substring(prefix, after.Length - prefix - suffix));
    }

    private IncrementalGeneratorResult RunTwice(IIncrementalGenerator generator, Compilation first, Compilation second)
    {
        var driver = CreateDriver(generator, first, trackSteps: true)
            .RunGeneratorsAndUpdateCompilation(first, out _, out _);
        var firstResult = driver.GetRunResult().Results.Single();

        driver = driver.RunGeneratorsAndUpdateCompilation(second, out _, out _);
        var secondResult = driver.GetRunResult().Results.Single();

        return new IncrementalGeneratorResult(firstResult, secondResult);
    }

    /// <summary>
    /// Every concrete incremental generator in the component that Roslyn would load: types carrying
    /// <see cref="GeneratorAttribute"/>.
    /// </summary>
    /// <remarks>
    /// A completeness check, like <see cref="CodeFixProvidersFor"/>: a test that enumerates the
    /// generators and demands a case for each makes it impossible to ship a new generator that no
    /// test ever runs. Types without the attribute are left out, because Roslyn never runs them —
    /// an abstract base or a helper implementing the interface is not a generator.
    /// </remarks>
    public IReadOnlyList<Type> GeneratorTypes()
        => LoadableTypes()
            .Where(t => !t.IsAbstract && typeof(IIncrementalGenerator).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttributes(typeof(GeneratorAttribute), inherit: false).Length > 0)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

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
    public Task<CodeFixResult> ApplyCodeFixAsync(
        string analyzerTypeName,
        string codeFixTypeName,
        string source,
        bool requireCompilableFixture = true)
        => ApplyCodeFixAsync(
            analyzerTypeName, codeFixTypeName, [FixtureProject.Of("FixInput", source)],
            requireCompilableFixture: requireCompilableFixture);

    /// <summary>
    /// Applies a code fix to a fixture of several projects, each referencing the ones before it.
    /// </summary>
    /// <param name="analyzerTypeName">Simple name of the <see cref="DiagnosticAnalyzer"/> to run.</param>
    /// <param name="codeFixTypeName">Simple name of the <see cref="CodeFixProvider"/> to invoke.</param>
    /// <param name="projects">
    /// The fixture, in dependency order. Diagnostics are collected from the <em>last</em> project;
    /// the fix may edit a document in any of them.
    /// </param>
    /// <param name="actionIndex">
    /// Which registered action to invoke. A provider may offer several for one diagnostic — one per
    /// interface a member could be added to, say — and the choice is part of what a test asserts.
    /// </param>
    /// <param name="requireCompilableFixture">
    /// Whether a fixture that does not compile is an error. Leave it on unless the analyzer under
    /// test is <em>purely syntactic</em> and the fixture deliberately names types the harness does
    /// not reference — a migration analyzer that matches <c>using SomeOtherLibrary;</c> as syntax,
    /// say. Turning it off is a claim that the semantic model cannot affect the outcome; make that
    /// claim explicitly at the call site, because the default protects every other test from a
    /// fixture that silently produces no diagnostics.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is the overload that can express what a cross-project code fix exists for. The
    /// single-source overload builds one project holding one document, so a fix whose entire job is
    /// to edit a file in a referenced project has nothing to reach; every such test passes
    /// vacuously.
    /// </para>
    /// <para>
    /// Two failures are raised as <see cref="FixtureNotUsableException"/> rather than reported on
    /// the result, because both are otherwise silent and both have produced green-but-meaningless
    /// tests in this repo: a fixture that does not compile, and a fix that reports success while
    /// changing no document. See that type's remarks.
    /// </para>
    /// </remarks>
    public async Task<CodeFixResult> ApplyCodeFixAsync(
        string analyzerTypeName,
        string codeFixTypeName,
        IEnumerable<FixtureProject> projects,
        int actionIndex = 0,
        bool requireCompilableFixture = true)
    {
        var analyzer = Instantiate<DiagnosticAnalyzer>(analyzerTypeName);
        var codeFix = Instantiate<CodeFixProvider>(codeFixTypeName);

        var fixture = projects.ToArray();
        if (fixture.Length == 0)
            throw new ArgumentException("A code-fix fixture needs at least one project.", nameof(projects));

        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var documentIds = new Dictionary<string, DocumentId>(StringComparer.Ordinal);
        var previous = new List<ProjectId>();
        ProjectId? lastProjectId = null;

        foreach (var project in fixture)
        {
            var projectId = ProjectId.CreateNewId(project.Name);

            solution = solution.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                project.Name,
                project.Name,
                LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: MetadataReferences(),
                projectReferences: previous.Select(p => new ProjectReference(p))));

            foreach (var (fileName, source) in project.Files)
            {
                var documentId = DocumentId.CreateNewId(projectId, fileName);

                // filePath matters, and its absence is not neutral. Without it Document.FilePath is
                // null while the syntax tree's is empty, so a fix that locates a sibling document by
                // `d.FilePath == someLocation.SourceTree?.FilePath` — the normal way to reach the
                // file a symbol is declared in — matches nothing and returns the solution
                // unchanged. It looks exactly like a fix that declined to offer anything, which is
                // a legal outcome, so the test passes and the entire body of the fix stays
                // unreachable. A real workspace always has paths; a harness without them cannot
                // exercise that whole class of code fix.
                solution = solution.AddDocument(
                    documentId, fileName, SourceText.From(source), filePath: $"/{project.Name}/{fileName}");

                documentIds[$"{project.Name}/{fileName}"] = documentId;
            }

            previous.Add(projectId);
            lastProjectId = projectId;
        }

        var reportingProject = solution.GetProject(lastProjectId!)!;
        var compilation = (await reportingProject.GetCompilationAsync())!;

        // Guard 1. A fixture that does not compile yields no analyzer diagnostics, which the
        // harness would otherwise report as "the fix declined" — so a mis-wired ProjectReference or
        // a typo in a fixture reads as a passing test.
        var compileErrors = requireCompilableFixture
            ? compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray()
            : [];

        if (compileErrors.Length > 0)
            throw new FixtureNotUsableException(
                $"The fixture does not compile, so no analyzer diagnostic can be trusted. " +
                $"{compileErrors.Length} error(s) in project '{reportingProject.Name}':" +
                Environment.NewLine +
                string.Join(Environment.NewLine, compileErrors.Select(d => "  " + d)));

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

        var originalSource = fixture[^1].Files[0].Source;

        if (fixable.Length == 0)
            return new CodeFixResult(diagnostics, originalSource, Applied: false);

        // The document the diagnostic was actually reported in — not necessarily the last project's
        // first file, once a fixture has several.
        var reportedTree = fixable[0].Location.SourceTree;
        var reportedDocumentId = reportedTree is null ? null : solution.GetDocumentId(reportedTree);
        var reportedDocument = reportedDocumentId is null
            ? solution.GetDocument(documentIds[$"{fixture[^1].Name}/{fixture[^1].Files[0].FileName}"])!
            : solution.GetDocument(reportedDocumentId)!;

        var reportedText = (await reportedDocument.GetTextAsync()).ToString();

        var actions = new List<CodeAction>();
        await codeFix.RegisterCodeFixesAsync(new CodeFixContext(
            reportedDocument, fixable[0], (action, _) => actions.Add(action), default));

        if (actions.Count == 0)
            return new CodeFixResult(diagnostics, reportedText, Applied: false);

        if (actionIndex >= actions.Count)
            throw new ArgumentOutOfRangeException(
                nameof(actionIndex),
                $"The provider registered {actions.Count} action(s), so index {actionIndex} is out of range. " +
                $"Offered: {string.Join(", ", actions.Select(a => $"'{a.Title}'"))}.");

        var titles = actions.Select(a => a.Title).ToList();
        var operations = await actions[actionIndex].GetOperationsAsync(default);

        // FirstOrDefault, not Single. A CodeAction is not obliged to produce exactly one
        // ApplyChangesOperation — it may produce none (it only opens a document, say) or several.
        // Single() would throw "Sequence contains no matching element" from a method whose
        // documented contract is that "offers nothing here" is a normal, assertable outcome.
        var changed = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (changed is null)
            return new CodeFixResult(
                diagnostics, reportedText, Applied: false, actions[actionIndex].Title, ActionTitles: titles);

        // Guard 2. Roslyn wraps an unmodified solution in a perfectly valid ApplyChangesOperation,
        // so a fix that silently gave up is indistinguishable from one that worked. This is the
        // trap d73d877 documented; asserting on changed documents closes it permanently.
        var changedDocuments = changed.ChangedSolution
            .GetChanges(solution)
            .GetProjectChanges()
            .SelectMany(p => p.GetChangedDocuments())
            .ToImmutableArray();

        if (changedDocuments.Length == 0)
            throw new FixtureNotUsableException(
                $"The fix registered '{actions[actionIndex].Title}' and reported success, but changed no " +
                $"document. That is a silent no-op: Roslyn wraps an unmodified solution in a valid " +
                $"ApplyChangesOperation, so it is indistinguishable from a fix that worked. Either the fix " +
                $"gave up on a path that returns its solution unchanged, or the fixture does not reach it.");

        var documents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, id) in documentIds)
            documents[key] = (await changed.ChangedSolution.GetDocument(id)!.GetTextAsync()).ToString();

        var fixedText = (await changed.ChangedSolution.GetDocument(reportedDocument.Id)!.GetTextAsync()).ToString();

        // Whether the code the fix produced actually compiles. Collected across every project,
        // because a fix that edits an interface in one project breaks the class in another — which
        // is precisely the failure mode a single-project harness cannot see. Reported rather than
        // thrown: "the fix produces a compile error" is a legitimate thing for a test to assert on
        // while the defect is still open.
        var fixedErrors = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var project in changed.ChangedSolution.Projects)
        {
            var fixedCompilation = await project.GetCompilationAsync();
            if (fixedCompilation is null) continue;

            fixedErrors.AddRange(fixedCompilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error));
        }

        return new CodeFixResult(
            diagnostics, fixedText, Applied: true, actions[actionIndex].Title, documents, titles,
            fixedErrors.ToImmutable());
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
