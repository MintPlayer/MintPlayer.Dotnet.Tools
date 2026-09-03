# MintPlayer.SourceGenerators.Testing

Test Roslyn source generators, analyzers and code fixes **in-process**, so your coverage collector
actually attributes the component's own code.

## The problem it solves

Reference a generator the normal way and its coverage is **zero**, however many tests you write:

```xml
<!-- Tests pass. Coverage: 0%. -->
<ProjectReference Include="..\Acme.Generators\Acme.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

Coverlet rewrites IL on disk in the *test project's* output directory. Roslyn loads analyzers
through `AnalyzerFileReference` from the *generator's own* `bin/`, which the collector never
touched. The generator runs, the tests pass, and nothing is measured.

Every approach that goes through the real compiler has this property — MSBuild, `MSBuildWorkspace`,
and building a fixture project all report nothing. Coverage appears only when the component sits in
the **test host's own output directory** and is loaded from there.

That is what this package does.

## Setup

```xml
<PackageReference Include="MintPlayer.SourceGenerators.Testing" Version="10.0.0" />

<ItemGroup>
  <!-- Build ordering only: keeps the netstandard2.0 component out of the compile graph, where
       its BCL polyfills would collide with the real types (CS0433). -->
  <ProjectReference Include="..\Acme.Generators\Acme.Generators.csproj"
                    ReferenceOutputAssembly="false"
                    SkipGetTargetFrameworkProperties="true" />

  <!-- The package's CopyComponentUnderTest target puts this, and the PDB beside it, in the
       output ROOT. Not a subfolder: the collector does not recurse. -->
  <ComponentUnderTest Include="..\Acme.Generators\bin\$(Configuration)\netstandard2.0\Acme.Generators.dll" />
</ItemGroup>
```

If the DLL is missing the build **fails** rather than warns — the failure it guards is silent, and a
green run reporting 0% looks exactly like a component nobody tested. Set
`FailOnMissingComponentUnderTest=false` to downgrade it.

## Usage

Configure once per test class; the loaded assembly and the reference set are the expensive parts.

```csharp
private static readonly GeneratorHarness Harness = GeneratorHarness
    .ForAssembly("Acme.Generators")
    .AddReference<Acme.Attributes.GenerateAttribute>();
```

### Generators

```csharp
var result = Harness.RunGenerator("MyGenerator", """
    using Acme.Attributes;
    [Generate] public partial class Widget { }
    """);

result.Errors.Should().BeEmpty(result.ErrorText);
result.SourceFor("Widget.g.cs").Should().Contain("public partial class Widget");
```

`Errors` is the union of the generator's own diagnostics **and** compile errors in the code it
produced — a generator emitting uncompilable code reports nothing itself, so asserting on its
diagnostics alone passes while the consumer's build breaks.

### Analyzers

```csharp
var diagnostics = await Harness.RunAnalyzerAsync("MyAnalyzer", source);
diagnostics.Should().ContainSingle().Which.Id.Should().Be("ACME001");
```

Filtered to the analyzer's own `SupportedDiagnostics`, so an unrelated compile error in a fixture
cannot masquerade as a finding.

### Code fixes

```csharp
var result = await Harness.ApplyCodeFixAsync("MyAnalyzer", "MyCodeFixProvider", source);

result.Applied.Should().BeTrue();
result.FixedSource.Should().Contain("await");
```

A fix that declines to offer an action is a normal outcome, not an exception: `Applied` is `false`
and the source comes back unchanged, so "offers nothing here" is directly assertable.

### Incrementality

```csharp
var run = Harness.RunGeneratorTwice("MyGenerator", [before], [afterUnrelatedEdit]);

run.OutputUnchanged.Should().BeTrue();
run.CachedSteps.Should().NotBeEmpty();
```

A single run cannot distinguish a correctly-cached pipeline from one that recomputes everything on
every keystroke — both emit identical output. Only a second run exercises the pipeline's equality
comparers, so only a second run catches a comparer that reports "changed" for an edit the generator
does not care about. That is a real performance defect in every consuming IDE and invisible to
every other kind of test.

## Why not Microsoft.CodeAnalysis.Testing?

It works, and its `{|ACME001:...|}` markup is nicer. It also pulls in NuGet.Common/Packaging/
Protocol/Resolver, Microsoft.VisualStudio.Composition and DiffPlex, resolves targeting packs over
the network at *test* time, and runs several times slower per test. One harness covering
generators, analyzers, fixes and incrementality beats two libraries covering three of the four.

## License

Apache-2.0
