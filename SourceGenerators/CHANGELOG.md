# Changelog — source generators

This changelog covers every package under `SourceGenerators/`. They are versioned in lockstep, so one entry applies
to all of them.

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
