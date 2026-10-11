# Changelog — source generators

This changelog covers every package under `SourceGenerators/`. They are versioned in lockstep, so one entry applies
to all of them.

## 12.2.0

Fixes [#193](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/193) and
[#194](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/194), plus every finding of the same bug classes in
the other generators: a crash reachable from unrelated or half-typed code, and an assumption that the user's type is a
`class`. The audit, the spikes and the design are in
[`docs/PRD-TypeKindRobustness.md`](../docs/PRD-TypeKindRobustness.md). Wherever a generator emits a `partial` of a user
type, it now works for `class`, `record class`, `record`, `struct` and `record struct`, or reports a diagnostic; it no
longer does nothing silently.

A minor release: the Attributes package and Tools gain public API, and new diagnostics can turn code that compiled
silently into warnings or errors. Read **Behaviour changes** before upgrading.

### Behaviour changes

- **GenericMethod: no arity-1 overload any more.** `[GenericMethod]` used to emit `Add<T1>(T1 t1)`, which hijacked a
  call such as `Add(list)` (it received one element: the list), also from inside the class. Bare `[GenericMethod]`
  now emits arities 2 to 4; `[GenericMethod(n)]` emits 2 to n. There is no opt-in for arity 1.
- **INTF001 now runs on structs and record structs.** It reported only on classes. Measured on about 2,400 files
  (Spark, AspNetCore.Tools, this repository) it added no warnings, but a struct whose public members are missing from
  its interface now gets one, and the code fix applies to it.
- **The Warning-severity MINT002 is now MINT008.** "Only one of `Equals(object)` and `GetHashCode()` is declared" was
  reported as MINT002 with Warning severity, beside the Info-severity MINT002 for an already-declared member. It is
  MINT008 now; MINT002 is Info only. A `#pragma warning disable MINT002`, `<NoWarn>` or `.editorconfig` entry meant
  for the warning must move to MINT008.
- **A `Task`-returning `[PostConstruct]` is an error (INJECT005).** The generated constructor called it without
  awaiting, so it ran fire-and-forget and its exceptions were lost. Return `void`, and start asynchronous work some
  other way.
- **`[Inject]` on a property with a setter is a warning (INJECT008).** The generated constructor assigns fields and
  get-only or init-only properties; a `{ get; set; }` property was skipped without a word and stayed null. It is still
  not assigned, but now reported.
- **CONFIG005 and OPTIONS004 are reported.** Both were declared and shipped but never reported. A `DefaultValue` the
  field cannot hold (`"3"` or `3.5` for an `int`, `300` for a `byte`, any value for a `DateTime`, `TimeSpan`, `Guid`,
  POCO or collection) is CONFIG005; it used to produce generated code that did not compile, or a different value (a
  non-string default on a `string` became `null`). `[Options]` together with `[Config]` is OPTIONS004; the field used to
  take the `[Config]` path silently.
- **`[Options]` together with `[ConnectionString]` is an error (OPTIONS005).** It was the one pair of config markers
  without a diagnostic; the field silently took the `[ConnectionString]` path.
- **`[Config]` defaults apply on nullable fields too.** `[Config("Retries", DefaultValue = 3)] int? retries` used to be
  `null` when the key was missing (a `string?` already got its default). A `Uri` field now uses a string default (it
  was ignored), an enum default works for every underlying type (`Small.B` on a `byte` enum emitted `Small.7`), and
  a floating-point default is written in the invariant culture (a build under a decimal-comma culture emitted
  `3,5d`).
- **Class-level mappers between public types are `public`.** The Mapper emitted every extension method `internal`.
  It is `internal` only when either type is.
- **The Mapper's assembly-level method-name prefix is honoured.**
  `[assembly: GenerateMapper(typeof(A), typeof(ADto), "Convert")]` generates `ConvertADto()` and `ConvertA()`; it used
  to ignore the prefix and generate `MapToADto()`. Callers of the old names must rename. A nested property of such a
  type calls the prefixed method.
- **An open generic `[Register]` on a non-generic class registers the closed interface.**
  `[Register(typeof(IRepository<>), ServiceLifetime.Scoped)]` on `class PersonRepository : IRepository<string>` used to
  register nothing, silently. It now registers `IRepository<string>`.
- **The test harness is strict.** `MintPlayer.SourceGenerators.Testing` fails every `Run*` and `ApplyCodeFixAsync`
  call with `ComponentCrashedException` when the component crashes: CS8785 (a generator threw), MPSG001 (a Producer
  caught its own exception), MPSG002 (a diagnostic could not be reported) or AD0001 (an analyzer threw). Before, a
  crash read as a component with nothing to say. A downstream suite whose fixtures crash silently starts failing; to
  assert on a crash deliberately, opt out per harness with `.AllowComponentCrashes()`.

### Added

**MintPlayer.SourceGenerators.Attributes**
- `GenericMethodAttribute` (namespace `MintPlayer.SourceGenerators.Attributes`). It used to live only inside the
  analyzer assembly, so consumer code could not reference it and `[GenericMethod]` was unusable. Shape:
  `GenericMethodAttribute(int count = 4)` plus a settable `int Count`, range 2..16.

**MintPlayer.SourceGenerators**
- GENMETHOD001 (the type, or a type containing it, cannot be extended), GENMETHOD002 (the method is not private),
  GENMETHOD003 (unsupported method shape), GENMETHOD004 (`Count` out of 2..16).
- INJECT005 (non-void `[PostConstruct]`), INJECT006 (type or containing type not partial; was CS0260), INJECT007
  (positional record or primary constructor; was CS8862), INJECT008 (Warning: `[Inject]` on a property with a setter),
  OPTIONS005 (`[Options]` with `[ConnectionString]`).
- README sections for the Description and ClassNames generators.
- REGISTER003 (inaccessible type), REGISTER004 (undefined lifetime), REGISTER005 (inexpressible service type). They
  replace CS8785/MPSG001 and CS0122, CS0246, CS0305 and CS0311 in the generated code.

**MintPlayer.Mapper**
- MAPC005 (a mapped type cannot be instantiated), MAPC006 (not accessible from the generated mapper), MAPC007 (a
  `[MapperConversion]` method cannot be called).

**MintPlayer.CliGenerator**
- MINTCLI002 (not partial), 003 (generic), 004 (static), 005 (non-partial containing type), 006 (file-local), 007 (root
  command less accessible than internal), 008 (subcommand inaccessible from its parent), 009 (two root commands with
  the same type name).

**MintPlayer.ValueComparerGenerator**
- MINT007 (Error): `[GenerateEquality]` on a ref struct, a static class, a C# 15 union or a file-local type, or a type
  nested in a union or a file-local type. Before: CS8121, CS0261, a phantom partial, or nothing.
- MINT008 (Warning): see Behaviour changes.

**MintPlayer.SourceGenerators.Tools**
- `GetPathSpec`/`OpenPathSpec` extended: `PathSpec.Target`, interface parents, `record`/`record struct`, interface
  variance, `@`-escaping, `UnsupportedReason`, `includeTarget`, `UniqueTypeParameterName`/`UniqueTypeParameterPrefix`.
- Attribute helpers: `IsAttribute`, `FindAttribute(s)`, `HasAttribute`, `TryGetArgument`/`GetArgument` (constants,
  arithmetic, casts, enums, `typeof`).
- `Producer.WriteHeader(IndentedTextWriter)` and `IndentedTextWriterExtensions.WriteLines(string)`: write a
  multi-line text one line at a time, so its line breaks are the writer's (see Fixed).
- Discovery plumbing: `DiagnosticInfo` (keyed by rule id, holds no Roslyn objects; a second, different descriptor
  under an id already seen throws), `Discovered<T>`, `ForAttribute`, `Models()`/`Diagnostics()`, a
  `ReportDiagnostics` overload for `DiagnosticInfo`, `Producer.WritePreamble`/`NullableEnable`, `OpenNamespace`.
- MPSG002 (Warning): reported instead of a diagnostic that could not be reported, so it is neither lost nor a crash.
- `StringExtensions.ToSafeNamespace` (see Fixed).
- Release tracking (`AnalyzerReleases.*.md`) for MPSG001/MPSG002.

**MintPlayer.SourceGenerators.Testing**
- `GeneratorResult.GeneratorExceptions`, `CrashDiagnostics` and `EnsureNoCrash()`; `CodeFixResult.AnalyzerExceptions`;
  `RunAnalyzerAsync` keeps AD0001. `AllowComponentCrashes()` opts out of the strict mode.

### Changed

**MintPlayer.SourceGenerators**
- `[GenericMethod]`: `Count` accepts constants, expressions and casts; overloads return the method's result (`bool`,
  `Task`, `ValueTask<T>`); works on class, record, struct, record struct and interface, generic, nested and in the
  global namespace, at any accessibility; `@`-escaped keyword names. Global-namespace types stay in the global
  namespace (they were wrapped in the root namespace).
- `[Inject]`/`[Config]`/`[ConnectionString]`/`[Options]`/`[PostConstruct]` work on records, structs and record structs,
  nested and nested in an interface (a struct's constructor chains `: this()`). A split partial class gets one
  constructor; a split base is forwarded whole, including its config and options parameters. Nullable `[Inject]`
  parameters come after the required ones (was CS1737). The constructor is `internal`/`protected` when a dependency is
  less accessible (was CS0051). `[Config]`/`[ConnectionString]`/`[Options]` accept any constant expression.
- `[Options("Section")]`: the section is documentation only, as the attribute's XML doc already said; the README and
  the XML doc now show `BindConfiguration`. OPTIONS002, which was never reported, is removed.
- `[Register]` works on records; a split partial is registered once; open generics follow the implemented interface's
  type arguments (reordered, partially closed, renamed); a generic class registered against itself or a non-generic
  service gets a generic `Add<T>()`.
- `BaseConstructorParameterAttribute` is `[Obsolete]`; it is removed in 13.0.
- Attributes are matched by metadata name, so a same-named attribute in another namespace no longer triggers a
  generator.
- `Inject.g.cs`, `ServiceMethods.g.cs`, `GenericMethods.g.cs` and the Description and ClassNames files are
  `#nullable enable` and compile warning-free; `Inject.g.cs` also gets the `<auto-generated>` header.

**MintPlayer.CliGenerator**
- Commands can be records (`record`/`record class`), and can be nested in a partial class, record, struct, record
  struct or interface.
- `Execute` receives the invocation's cancellation token (Ctrl+C) instead of `CancellationToken.None`.
- The generated file is nullable-enabled and warning-free; attributes are matched by metadata name.

**MintPlayer.Mapper**
- Mapping to and from a struct or record struct compiles; the null guard is emitted only for reference types; the copy
  overload `MapToX(input, output)` is not generated for a value-type destination (it wrote into a copy) or when there
  is nothing to copy (init-only or positional record).
- Reference create overloads are `[return: NotNullIfNotNull("input")] D? MapToD(this S? input)` (when the attribute
  exists in the compilation). The generated code is free of nullable warnings and CS0162, list and array properties
  included.

**MintPlayer.ValueComparerGenerator**
- MINT003 is one descriptor (same messages). Containing interfaces are re-declared with their `in`/`out` variance.
  `JoinMethodGenerator` finds `[GenerateJoinMethods]` by metadata name.

**Every generator package**
- `AnalyzerReleases.Shipped.md` records a 12.1.1 baseline of the rules each package shipped (earlier releases were
  never tracked), and MINTCLI001 moved to it from Unshipped. The build is free of RS2xxx warnings.
- **Install the `*.Attributes` package next to the generator.** The Attributes READMEs and the index READMEs said
  the generator brings its attributes along. It does only for a bare `<PackageReference>`: the generators are
  development dependencies, so `dotnet add package` and the Visual Studio package manager write an `IncludeAssets` list
  without `compile`, which also filters the dependency, and the attribute is CS0246. Measured on packed packages; every
  README now says to install both.

### Fixed

**MintPlayer.SourceGenerators**
- **#193:** `GenericMethodSourceGenerator` threw `InvalidCastException` on any attributed method in a record, record
  struct, struct or interface (a `[GeneratedRegex]` method was enough), reported CS8785 and emitted nothing for the
  whole compilation.
- **#194:** `[GenericMethod]` works as documented (see Added and Changed), warning-free under `#nullable enable`.
- `[Inject]` on a record, struct or record struct generated nothing (#193 in Inject).
- **CONFIG001 named the wrong type and hid INJECT006.** Every type that cannot receive a generated constructor got
  "Class '{0}' must be partial" on its `[Config]`/`[ConnectionString]`/`[Options]` members, naming the type itself.
  For a file-local partial type, a partial type nested in a non-partial type and an interface, that was wrong. CONFIG001
  is now reported only when the type itself is not partial; the other cases get INJECT006, which names the offender
  and the reason. Its message now reads "Type '{0}' must be partial to use [Config], [ConnectionString] or
  [Options]", since it also fires for records, structs and the other two markers.
- **`[Inject]` with a config marker still injected the member.** `[Inject]` together with `[Config]`,
  `[ConnectionString]` or `[Options]` reported CONFIG008, CONNSTR003 or OPTIONS003 and still added a constructor
  parameter for the member. Like OPTIONS004 and OPTIONS005, a conflict now emits neither binding; the error stays.
- Description: generic types and types nested in an interface are correct; a summary on two parts or an explicit
  `[Description]` no longer gives CS0579; `file` types are skipped.
- ClassNames: keyword names are `@`-escaped; a class named `ClassNames` gets no constant; a user `ClassNames`/
  `ClassNameList` in the root namespace suppresses that file.
- MP001 no longer crashes on type aliases (AD0001); its fix keeps `#nullable`, `#region` and the file header above the
  first `using`.
- INTF001 reports an added overload of an interface method, and the fix adds that overload; the fix declines members
  that use the class's own type parameter.

**MintPlayer.CliGenerator**
- `[CliOption(null)]` crashed the generator (CS8785).
- The generated partial repeated the accessibility (CS0262 for `private protected`).
- README: the example called `AddDemoCommandTree()` and `app.Services.InvokeDemoCommandAsync(args)`; the generated
  methods are `AddDemoCommand()` and `InvokeDemoCommandAsync(args)` on the `IHost`.

**MintPlayer.Mapper**
- An erroneous `[GenerateMapper]` broke `MapperEntrypoint.g.cs`; base and derived mapped together gave CS8120; a
  `[MapperConversion]` in a partial static class was registered once per part.
- README: the examples used `[MapperAlias]` and non-generic `[MapperState]`/`[MapperConversion]` overloads, which do
  not exist; they use `[MapTo]`, `[MapperState<TEnum>]` and `[MapperConversion<TEnum>]`.

**MintPlayer.ValueComparerGenerator**
- MINT001 overflowed the stack (taking down the compiler) on an expanding generic such as
  `class Foo<T> { Foo<Foo<T>> Next { get; } }`.
- Models with an unresolved base or property type are skipped instead of generating broken code.

**MintPlayer.SourceGenerators.Tools**
- **A `RootNamespace` that is not a valid namespace name broke every generator.** The SDK passes the project name
  through with only spaces replaced, so a project named `My-App` got `namespace My-App` in every generated file
  (CS0246, CS0102, CS0548). `Settings.RootNamespace` and `Producer.RootNamespace` are now sanitized once, by the new
  `StringExtensions.ToSafeNamespace`: an invalid character becomes `_`, a segment starting with a digit gets a `_`
  prefix, and a keyword segment is `@`-escaped (`My-App.1st.class` becomes `My_App._1st.@class`). This fixes ServiceRegistrations, ClassNames, Mapper,
  CliGenerator and the MintPlayer.Assertions equivalency registration at once.
- `OpenPathSpec` wrote `record class`, which is CS8773 at C# 9; it writes `record`.
- `LocationKey.ToLocation` is bounds-checked, and reporting a diagnostic never throws (a stale location used to crash
  the generator).
- **Generated files mixed line endings.** `Producer.Header` is a raw string literal, so its line breaks are those of
  the checkout the package was built from, while the rest of every file uses `Environment.NewLine`. A package built on
  Linux and used on Windows emitted an LF header above a CRLF body. Every generator in this repository (and
  `Producer`'s own preamble) now writes the header with the new `Producer.WriteHeader(writer)`, which writes it one
  line at a time. **A producer in another package that calls `writer.WriteLine(Header)` should switch to
  `WriteHeader(writer)`**; `IndentedTextWriterExtensions.WriteLines` does the same for any other multi-line literal.

### Other packages in this repository
- **MintPlayer.Assertions** (`11.0.0-rc.6`): it ships `MintPlayer.SourceGenerators.Tools` inside its analyzer payload,
  so it picks up two Tools fixes above. Its equivalency registration compiles in a project whose `RootNamespace` is
  not a valid namespace name (`My-App` gave `namespace My-App`, CS0246/CS0102/CS0548), and both of its generators
  write the `<auto-generated>` header with the file's own line endings.

## 12.1.1

Fixes [#191](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/191). The investigation and design are in
[`docs/PRD-INTF001-IgnoreOverrides.md`](../docs/PRD-INTF001-IgnoreOverrides.md).

### Fixed

**MintPlayer.SourceGenerators**
- **INTF001 no longer reports `override` members.** An override (including `sealed override`) re-implements a member
  whose contract the base class already declares, so it is never missing from an interface. Before, every class
  implementing an interface was flagged for overriding `ToString`, `Equals` or `GetHashCode`, and every class that
  also derived from an abstract or virtual base was flagged for each override. The code fix offered to copy those
  members onto the interface, and the result compiled. Both stopped. `new` (hiding) members are still reported:
  hiding is genuinely new public surface. `[NoInterfaceMember]` on an override is now redundant but harmless.

## 12.1.0

Fixes the findings reported downstream in [#187](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/187),
plus a version leak found while investigating them. The investigation, the spikes and the design are in
[`docs/PRD-ValueComparerGenerator-DownstreamFindings.md`](../docs/PRD-ValueComparerGenerator-DownstreamFindings.md).

### Fixed

**Every package**
- **Assemblies carry their own version again.** 37 published versions (SourceGenerators 10.20.2–12.0.1, and
  MintPlayer.Assertions; listed per package in the PRD's F4 audit) shipped assemblies with
  AssemblyVersion **99.9.9.0**. The packaging tests packed the repository's own projects in place with
  `-p:Version=99.9.9-packtest`, and the release's `dotnet pack --no-build` after the test run packed those rebuilt
  dlls. The packaging tests now pack into their own folder, and CI fails any PR or release whose packed assemblies
  don't match the package version (`eng/Assert-PackageVersions.ps1`). Those releases are deprecated on nuget.org.
- **A plain `ProjectReference` to a generator built on these packages builds.** The packages' `build/*.targets` added
  their attributes dll to the referencing generator's target path with `IncludeRuntimeDependency="true"`. A project
  referencing that generator normally, such as a test project running it in-process, then failed in
  `GenerateDepsFile` with MSB4018 "An item with the same key has already been added". It is now `false`, as for every
  other analyzer-folder dependency. Such a project gets the attributes dll in bin but not in its `deps.json`. If it
  reflects over the models' attributes and doesn't run under the VSTest testhost, it needs a `PackageReference` to the
  attributes package (see the ValueComparerGenerator README).
- **No generated file raises CS1591.** A consumer building with `GenerateDocumentationFile` got a missing-XML-comment
  warning for every public member a generator emitted. Generated code can't be documented or suppressed by the
  consumer. Every generator now documents what it emits. A new guard (`DocumentedOutputGuardTests`, in both generator
  test projects) compiles each generator's output with documentation diagnostics on.

**MintPlayer.ValueComparerGenerator**
- **`JoinMethods.g.cs` is emitted only when it has something in it**: `[assembly: GenerateJoinMethods(n)]` with
  `n >= 6`, in a project that references Roslyn. Every generator project without the attribute used to get an empty
  **public** class `Microsoft.CodeAnalysis.IncrementalValueProviderAdditionalEx`.
- **The generated class is `internal static partial`.** A public copy in two assemblies made calls in the referencing
  one ambiguous (CS0121), and it put a public type in the consumer's `Microsoft.CodeAnalysis` namespace.
- **The overloads for 6 providers and up chain like the ones in Tools**: `previous.Join(next)`. They took `previous`
  plus five more providers and ignored all but the last, so `a.Join(b)` didn't compile past arity 5.

### Breaking changes

- **Arity 6+ `Join` callers:** the old six-argument form no longer exists. Write `a.Join(b)`. A library built with
  12.0.1 or earlier still exposes its public copy. Extension calls in either shape stay unambiguous, so a referencing
  project and its library don't have to upgrade together. Naming the class directly
  (`IncrementalValueProviderAdditionalEx.Join(...)`) across such a pair gives CS0436.
- **AssemblyVersion drops from 99.9.9.0 to 12.1.0.0.** This matters when a project mixes generator packages from
  before and after the fix, measured in the PRD's spike V2. Each generator package bundles
  `MintPlayer.SourceGenerators.Tools.dll`. The SDK's `ResolvePackageFileConflicts` keeps only the copy with the
  highest AssemblyVersion, and that is the bogus **99.9.9.0** from the older package. Every generator in the project
  then runs against the older Tools. It builds cleanly today, because the Tools API is compatible. A future generator
  that needs a newer Tools API won't load next to a 10.20.2–12.0.1 package, so upgrade every MintPlayer generator
  package together.

### Changed

- The docs, the `eng/valuecomparergenerator.targets` error text and `CLAUDE.md` gave the wrong reason why
  `MintPlayer.ValueComparerGenerator.Attributes.dll` ships beside a generator that uses `[GenerateEquality]`. Measured:
  Roslyn loads and runs the generator without it, but a host that discovers generators by reflection throws
  `FileNotFoundException` on the model types. The dll still ships, and packing without it is still an error.

## 12.0.1

The design and spikes are in [`docs/PRD-EqualitySingleFile.md`](../docs/PRD-EqualitySingleFile.md). Not a breaking
change: generated file names are not API.

### Changed

**MintPlayer.ValueComparerGenerator**
- **All generated equality members now go into one file, `GeneratedEquality.g.cs`,** instead of one
  `<Namespace>.<Type>.Equality.g.cs` per model. A per-model name grows with namespace depth, nesting and generic
  arity. Once it passed 255 characters, a consumer building with `EmitCompilerGeneratedFiles=true` failed with
  `CS0016: Could not write to output file`. The members generated for each model are unchanged, byte for byte. The
  models are ordered by fully qualified name (ordinal), so the file is identical across machines and runs. A
  compilation with no models gets no file.

### Added

- **A guard that every generator emits a fixed set of files** (`FixedFileSetGuardTests`, in the SourceGenerators
  and the Assertions generator test projects). It runs each generator over one decorated input and over five, and
  fails if the set of hint names differs.

### Fixed

**MintPlayer.SourceGenerators**
- **`DescriptionSourceGenerator` no longer drops a summary at column 0 directly below the previous line** (for
  example `namespace Demo;` or `{` followed by `/// <summary>` with no indentation or blank line). It read the leading
  trivia with `ToString()`, which leaves out the doc comment's `///` exterior when the comment is the first trivia,
  so no `[Description]` was generated.
- **`GenericMethodSourceGenerator` no longer marks the overloads it generates `partial`** when the decorated method
  is. An overload has another signature and can never be that method's implementation, so each one failed with
  `CS0759`.

**MintPlayer.CliGenerator**
- **A subcommand declared in another namespace than its parent now compiles.** Every command's partial half was
  written inside its root command's namespace block, so a subcommand elsewhere got a second, unrelated class in the
  root's namespace, and the parent's `RegisterCliCommandTree` and `BuildCliCommand` calls failed with `CS0117`. Each
  command is now emitted in its own namespace. Output for a tree in one namespace is unchanged.

## 12.0.0

Issue [#184](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/184), PR
[#185](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/pull/185). The design, spikes and measurements are in
[`docs/PRD-GeneratedEquality.md`](../docs/PRD-GeneratedEquality.md).

`[GenerateEquality]` (formerly `[AutoValueComparer]`) now generates `IEquatable<T>`, `Equals(object)` and `GetHashCode()` **on the model type
itself**. Models are value-equal under `EqualityComparer<T>.Default`, which Roslyn uses for every incremental step,
so `.WithComparer()` is no longer needed anywhere. The value-comparer runtime is removed, with **no backward
compatibility**.

### Breaking changes

**MintPlayer.SourceGenerators.Tools**
- **Removed:**
  - `ValueComparer<T>`, `ComparerRegistry` and `[ValueComparer]`.
  - The whole `MintPlayer.SourceGenerators.Tools.ValueComparers` namespace: every structural, tuple, dictionary,
    key-value-pair, Symbol, Syntax and SourceText comparer, plus `ReferenceEqualityComparer<T>`.
  - `ICompilationCache`, `ComparerCacheHub`, `PerCompilationCache` and `SymbolPair`.
  - `HashCodeCompat`, the `ModuleInitializerAttribute` polyfill, and the public `SettingsValueComparer`.
- **`IncrementalGenerator.Initialize` loses its third parameter** (the unused `ICompilationCache` provider). The
  new signature is `Initialize(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<Settings> settingsProvider)`.
- **`LocationKey` is now `sealed`.**

**MintPlayer.ValueComparerGenerator** / **.Attributes**
- **`AutoValueComparerAttribute` is renamed to `GenerateEqualityAttribute`**: write `[GenerateEquality]` instead of
  `[AutoValueComparer]`. The attribute no longer generates a comparer, so the old name was wrong.
- **`ComparerIgnoreAttribute` is renamed to `EqualityIgnoreAttribute`**: write `[EqualityIgnore]` instead of
  `[ComparerIgnore]`. There are no `[Obsolete]` aliases for either attribute. `[UseEqualityComparer]`,
  `[GenerateJoinMethods]`, the package ids and the diagnostic ids `MINT001`–`MINT006` are unchanged.
- **No more comparer output.** The generator no longer emits `XValueComparer` classes, the `[ValueComparer]` tag, or
  the `.WithComparer()` / `.WithNullableComparer()` extension methods.
- **Every `partial` type deriving from a `[GenerateEquality]` type gets equality members**, whether or not it
  carries the attribute. That covers a transitive grandchild too. A non-`partial` derived type is an error
  (`MINT003`), and so is a non-`partial` containing type (`MINT006`).
- **Dictionaries compare order-insensitively.** They used to compare order-sensitively over boxed pairs, by accident.
- **Runtime registration is replaced.** `ComparerRegistry.Register` becomes `[UseEqualityComparer(typeof(C))]` on
  the property.
- **`MINT001` has a new trigger.** It inspects the properties of `[GenerateEquality]` models, instead of
  `.WithComparer()` calls.

**MintPlayer.ValueComparers.NewtonsoftJson**
- **`JObjectValueComparer` is a plain comparer.** It is a sealed `IEqualityComparer<JObject?>` with a static
  `Instance`, and no longer derives from `ValueComparer<JObject>`.
- **`JObjectValueComparer.Register()` is removed.** Put `[UseEqualityComparer(typeof(JObjectValueComparer))]` on
  each `JObject` property instead.
- **The package no longer depends on MintPlayer.SourceGenerators.Tools.**

### Added
- **In Tools: `ValueEquality`,** reflection-free, allocation-free structural equality and hashing for arrays,
  `ImmutableArray<T>`, lists, sequences and dictionaries. It also has composable comparer instances for nested
  collections.
- **In Tools: `EquatableArray<T>`,** returned from a pipeline step that builds a new collection.
- **In Tools: `ProduceCode(params IncrementalValuesProvider<Producer>[])`.** Several multi-value providers can be
  registered in one call, as single-value providers already could. Each provider keeps its own output step.
- **Model shapes:** records, sealed and derived records, structs, record structs, generic models, and models nested
  in any of these. `[GenerateEquality]` is now allowed on structs.
- **Model hierarchies:** equality uses an exact-type check plus `protected virtual EqualsCore`/`HashCore`. That
  replaces the derived-type `switch`, which could overflow the stack.
- **`[UseEqualityComparer(typeof(C))]`** for property types without value equality, such as `JObject`.
- **Diagnostics:**
  - `MINT002`: a user-declared equality member was skipped.
  - `MINT004`: invalid `[UseEqualityComparer]` type.
  - `MINT005`: a property type compares by reference only.
- **`IEquatable<T>`:** `LocationKey`, `PathSpec`, `PathSpecElement`, `AnalyzerInfo`, `LangVersion` and `Settings`
  implement it, with consistent hashes.

### Fixed
- **Strings no longer go through reflection.** String properties were compared char by char through reflection:
  about 5 µs and 10 KB per compare for a model with 3 strings. Generated equality takes about 30 ns and allocates
  nothing.
- **`[EqualityIgnore]` properties are no longer hashed.** `[ComparerIgnore]` properties were, which made equal models hash differently.
- **No stack overflow in hierarchies.** Comparing two instances of a derived type without its own attribute used to
  overflow the stack.
- **Consistent hashes.** Several hand-written comparers had no hash override, so equal values had different hashes.
- **`PathSpecElement` equality now includes `GenericTypeParameters`.**
- **Records and structs are generated.** `[AutoValueComparer]` on a record or struct used to produce nothing.
- **Generic models compile.** They used to produce uncompilable code.

### Migrating a generator
1. Bump all MintPlayer source-generator packages to 12.0.0.
2. Replace `[AutoValueComparer]` with `[GenerateEquality]` and `[ComparerIgnore]` with `[EqualityIgnore]`.
3. Delete every `.WithComparer(...)`, `.WithNullableComparer()` and `ComparerRegistry.For<T>()` call.
4. Where a `Select` builds a new collection (a filter or projection after `Collect()`, or `ToArray()`), return
   `EquatableArray<T>` (`.ToEquatableArray()`). A `Collect()` of equatable models can stay an `ImmutableArray<T>`.
5. Remove the `ICompilationCache` parameter from your `Initialize` override, and delete
   `using MintPlayer.SourceGenerators.Tools.ValueComparers;`.
6. Replace hand-written `ValueComparer<T>` subclasses with `[GenerateEquality]`, or with hand-written
   `IEquatable<T>` using `ValueEquality`. Replace comparer registrations with `[UseEqualityComparer]`.
7. Make every type deriving from a `[GenerateEquality]` type `partial`, and every containing type of a model too.
8. Fix any `MINT001`/`MINT005` the build reports. Either property can otherwise never compare as unchanged, so the
   step never caches.

### Other packages in this repository
- **MintPlayer.Assertions** (`11.0.0-rc.4`): its source generator's models use `[GenerateEquality]`, so its
  analyzer payload now also ships `MintPlayer.ValueComparerGenerator.Attributes.dll`.
