# Working in SourceGenerators/

Hard-won notes for this folder. Most of what goes wrong here **fails silently** — the package
restores, the build is green, and the generator simply never runs. Assume any mistake you make will
be invisible unless you go looking.

## Read `eng/` before changing any packaging

Four files, organised by concern. They already do the right thing; do not reinvent them in a csproj.

| File | Owns | Import when |
|---|---|---|
| `eng/sourcegenerator.targets` | the generator DLL, `Tools.dll`, and the generator's own `$(AssemblyName).Attributes.dll`; sets `netstandard2.0`, `IsRoslynComponent`, `IncludeBuildOutput=false` | always, in a standalone generator project |
| `eng/valuecomparergenerator.targets` | `MintPlayer.ValueComparerGenerator.Attributes.dll` + the analyzer reference | the project uses `[AutoValueComparer]` |
| `eng/newtonsoftjson.targets` | `Newtonsoft.Json.dll` + `MintPlayer.ValueComparers.NewtonsoftJson.dll` | the models need Newtonsoft comparers |
| `eng/filenesting.targets` | IDE file nesting | always |

Need value comparers? Import `valuecomparergenerator.targets`. Need Newtonsoft in your models?
Import `newtonsoftjson.targets`. A dependency that only *some* generators need belongs in its own
eng file, **not** in `sourcegenerator.targets` — needing the value comparer is a property of using
it, not of being a generator.

### Who imports `sourcegenerator.targets`, and who cannot

Every standalone generator package — `MintPlayer.SourceGenerators`, `MintPlayer.Mapper`,
`MintPlayer.CliGenerator`, `MintPlayer.ValueComparerGenerator` — imports it, and should. Those
packages ship **no assembly the consumer compiles or links against**: their whole payload sits under
`analyzers/`, loaded by Roslyn at build time and never referenced by consumer code.

The attributes are the apparent exception and are worth being precise about. A consumer does write
`[Inject]` or `[AutoValueComparer]`, but those types come from a separate `*.Attributes` package
that the generator package takes a **NuGet dependency** on — that is the one with the `lib/`. The
copies under `analyzers/dotnet/cs` exist purely so Roslyn can resolve the attributes while loading
the generator; the consumer never binds to them.

So `netstandard2.0`, `IsRoslynComponent`, `DevelopmentDependency=true` and
`IncludeBuildOutput=false` are all exactly right here, and a `lib/` folder would be wrong.

`MintPlayer.Assertions` is a different kind of package: it ships **consumer code and a generator in
the same nupkg**, so that one `PackageReference` gives you the assertion API *and* its analyzers. It
therefore must **not** import `sourcegenerator.targets` — `IncludeBuildOutput=false` would strip the
`lib/` the whole library lives in, and `DevelopmentDependency=true` would mark a runtime dependency
as build-only. It carries its own small pack target instead. That asymmetry is deliberate; do not
"tidy" it into an import.

The test: **does anything in this package run in the consumer's process?** No → import the shared
targets. Yes → hand-roll the analyzer payload alongside the library.

## Package layout

The canonical shape, verifiable against any published package:

```
analyzers/dotnet/cs/               attribute assemblies (shared by both Roslyn versions)
analyzers/dotnet/roslyn4.0/cs/     the generator + MintPlayer.SourceGenerators.Tools.dll
analyzers/dotnet/roslyn4.9/cs/     the same
build/<PackageId>.props/.targets
(no lib/ — a generator is a build-time component)
```

Roslyn loads analyzers **only** from those paths. A DLL one folder away is restored and never run.

### Every generator here needs `MintPlayer.ValueComparerGenerator.Attributes.dll`

All four generators decorate their own pipeline models with `[AutoValueComparer]`. Roslyn resolves
that attribute when **loading** the generator, so the assembly must be in `analyzers/dotnet/cs`.
Leave it out and the package does not degrade — it stops working, with
"cannot find `[AutoValueComparer]`" naming an assembly the consumer never referenced.

Before removing anything from an analyzer payload, ask **"does the generator need this at load
time?"** and check with `grep`. A file being absent from a sibling's `ProjectReference` list proves
nothing about whether the generator needs it.

## The evaluation-time glob trap

**Never collect pack assets with a static `ItemGroup` that points at another project's output.**

```xml
<!-- WRONG: expanded at evaluation time, before the ProjectReference is built. -->
<None Include="..\X\bin\$(Configuration)\netstandard2.0\*.dll" Pack="true" PackagePath="analyzers/dotnet/cs" />
```

On a clean tree this matches nothing and the package ships without the assembly. It appears to work
only because a previous full build left the file there — which is why it survived for a long time
and why it cannot be reproduced on a developer machine.

```xml
<!-- RIGHT: runs during pack, after Build. -->
<PropertyGroup>
  <TargetsForTfmSpecificContentInPackage>$(TargetsForTfmSpecificContentInPackage);AddX</TargetsForTfmSpecificContentInPackage>
</PropertyGroup>
<Target Name="AddX">
  <ItemGroup><_X Include="$(MSBuildProjectDirectory)\bin\$(Configuration)\netstandard2.0\Foo.dll" /></ItemGroup>
  <Error Condition="!Exists('%(_X.FullPath)')" Text="... would ship a generator that cannot load." />
  <ItemGroup><TfmSpecificPackageFile Include="@(_X)" PackagePath="analyzers/dotnet/cs" /></ItemGroup>
</Target>
```

Three rules that follow:

1. **Source assets from the producing project's own output directory.** It holds exactly that
   project's runtime closure, and it is populated by the time the target runs.
2. **Name files explicitly; avoid `*.dll` over someone else's bin.** A glob packs whatever an
   earlier build left lying around, so the payload differs between a clean checkout and a
   developer machine.
3. **Guard with `<Error>`, never `<Warning>`.** An empty analyzer payload produces a package that
   installs happily and does nothing. There is no error for the consumer to search for, so the
   failure has to happen at pack time.

Never condition an analyzer payload on `$(Configuration)`. A plain `dotnet pack` defaults to Debug.

## Coverage and testing generators

- **An `OutputItemType="Analyzer"` reference contributes ZERO coverage.** Roslyn loads it via
  `AnalyzerFileReference` from the generator's own `bin/`, which the collector never instrumented.
  So does `MSBuildWorkspace`, and so does building a fixture project.
- Use **`MintPlayer.SourceGenerators.Testing`** (in this folder). It copies the component into the
  test bin **root** and `Assembly.Load`s it into the **default** ALC, which is the instrumented
  copy. `LoadFrom` or a custom `AssemblyLoadContext` runs the code and measures nothing.
- Lambdas *are* covered — they compile to ordinary methods. Do not exclude
  `CompilerGeneratedAttribute`; it would blind the report to every closure and async body.
- A single driver run never exercises the incremental comparers. `RunGeneratorTwice` does, and it
  is the only way to catch a generator that recomputes everything on every keystroke — both
  versions emit identical output.

### Assert that generated code COMPILES

`run.Errors.Should().BeEmpty(run.ErrorText)` catches what substring assertions cannot. In this
repo it found: a `[RegisterFactory]` emitting `AddScoped<T>(method group)` against a
`Func<IServiceProvider,T>` overload (CS1503), and a documented `record` emitting `partial class`
(CS0261). A snapshot test was green for both.

Also cover the diagnostic paths. Every `*.Rules.cs` sat at 0% for a long time, which meant no test
ever drove a generator down a reporting path — a rule that never fired looked identical to one that
worked.

## Verifying a packaging change

Local green means very little here. Verify like this:

1. **Wipe every `bin/`** under `SourceGenerators/` and `Assertions/` first. Stale output is what
   hides these bugs.
2. Pack **each** affected package in **both** Debug and Release.
3. Diff the `analyzers/**` entries between configurations — they must be identical.
4. Compare against a published package (`~/Downloads`, or the NuGet cache) to confirm the shape
   has not regressed.

`PackagingTests` does 1–3 in CI. It is `[Trait("Category","E2E")]` and packs six projects, so it is
slow by design.

## Mistakes made in this folder — do not repeat

- **Inferring instead of checking.** Every wrong call here came from reasoning about the code when
  the answer was one command away: the `eng/` folder, a published `.nupkg`, or
  `grep AutoValueComparer`.
- **Removing a payload entry as "stale residue"** without checking whether the generator loads
  without it. It did not.
- **Putting a value-comparer concern in `sourcegenerator.targets`** instead of the eng file that
  owns it.
- **Leaving the old mechanism in place** beside the new one. NuGet de-duplicates identical pack
  paths, so the output looked right while both a broken and a correct include were live.
  Output-shaped verification cannot see duplicated inputs.
- **Weakening a test because it kept failing.** It was failing on build residue caused by the globs;
  the fix was to make the payload deterministic, after which strict set equality worked.
- **Windows-only path literals.** `Path.Combine` does not translate separators, so a
  backslash-separated relative path in `InlineData` resolves here and silently does not on CI.
