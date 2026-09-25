# PRD: generated `IEquatable<T>` replaces the value-comparer runtime

Issue: [#184](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/issues/184). Line numbers are against master
`840456d`; paths are relative to `SourceGenerators/` unless stated otherwise.

## Overview

`[AutoValueComparer]` generates a separate `XValueComparer : ValueComparer<X>` and leaves `X` itself with reference
equality. Roslyn compares every incremental step's output with `EqualityComparer<T>.Default`, so the comparer only
works on the steps that remember `.WithComparer()`. Issue #184 proposes emitting `IEquatable<T>` members on the type
that **delegate to** the generated comparer.

The investigation found that delegating keeps a runtime that is slow, partly broken, and unnecessary. This PRD
therefore goes one step further than the issue:

- **The generator emits the `Equals`/`GetHashCode` bodies directly on the type.** They compare properties inline,
  with a small reflection-free helper for collections.
- **The comparer runtime is deleted outright, with no backward compatibility.** That covers `ValueComparer<T>`,
  `ComparerRegistry`, the reflection helpers, `[ValueComparer]`, the generated `XValueComparer` classes, the
  generated `.WithComparer()` extensions and `ICompilationCache`.
- **This is a major version, 12.0.0,** for `MintPlayer.SourceGenerators.Tools`, `MintPlayer.ValueComparerGenerator`
  and `.Attributes`. The downstream migrations (Spark, AspNetCore.Tools) are part of the same unit of work.

The main goal is **no functional regressions**. Every behaviour the comparer runtime provides today is listed under
[Behaviours to preserve](#behaviours-to-preserve), and spike S4 checks the new code against the old comparers as an
oracle before they are deleted.

## Problem statement: what the investigation found

### P1: every string is compared char by char through reflection (measured)

`ValueComparer<T>.IsEquals<string>` (`MintPlayer.SourceGenerators.Tools/ValueComparers/ValueComparer.cs:43-70`)
first misses the registry, because nothing registers `string`. It then takes the `IEnumerable` branch, since
`string is IEnumerable`, and reaches `TryListEquals` (`ValueComparer.Helpers.cs:74-100`):

- `TryListEquals` enumerates non-generically and boxes every `char`.
- It compares each element through `ObjectEqualsDynamic`, a cached delegate around `MethodInfo.Invoke`
  (`Helpers.cs:124-175`).
- `AddHash<string>` goes down the same path.

A throwaway probe measured the cost against the built Tools dll:

| Operation (64-char string) | 100k calls | Allocated per call |
| --- | --- | --- |
| `ValueComparer<T>.IsEquals<string>` | **1088 ms** | **7,232 B** |
| `string.Equals(a, b, Ordinal)` | ~0 ms | 0 B |

Strings are the most common property type in the models: Spark alone has 52 `string` properties. `IReadOnlyList<T>`
and `IEnumerable<T>` properties take the same reflection path, element by element. Only `T[]`, `List<T>`,
`ImmutableArray<T>` and `ValueTuple` with 2 to 6 items get typed comparers (`ValueComparer.Registry.cs:120-137`).
The design in #184 would keep all of this cost.

### P2: a derived type without its own attribute overflows the stack (reproduced)

`[ValueComparer]` is inherited (`GetCustomAttribute(inherit: true)`, `Registry.cs:135-152`). Take an
`[AutoValueComparer] abstract Shape` with a derived `Circle` that has no attribute:

1. `ComparerRegistry.For<Circle>()` resolves to `ShapeValueComparer`, through the inherited attribute.
2. The `switch` arm `(Circle a, Circle b) => IsEquals(a, b)` calls `ShapeValueComparer.Equals`.
3. That dispatches to the same switch arm again, forever.

The probe crashed with "Stack overflow. Repeated 492 times". This is **exactly the fixture shape** of
`Snapshots/ProducerSnapshotTests.ValueComparers.verified.txt`. It goes unnoticed because no test ever executes a
generated comparer; they only check that the output compiles.

### P3: `Equals` and `GetHashCode` disagree

- **The `[ComparerIgnore]` bug from #184.** `AddHash` iterates `AllProperties` unfiltered
  (`ValueComparerGenerator/MintPlayer.ValueComparerGenerator/Generators/ValueComparerGenerator.Producer.cs:144`),
  while `AreEqual` skips ignored properties.
- **Hand-written comparers with no hash.** `SettingsValueComparer`, `LangVersionComparer`, `AnalyzerInfoComparer`,
  `PathSpecValueComparer` and `PathSpecElementValueComparer` don't override `AddHash`, so they fall back to the
  reference hash.

This is latent today, because Roslyn's driver only calls `Equals`. It becomes live the moment models go into a
`HashSet`, a `Dictionary`, or a tuple's hash.

### P4: comparers only work where they are applied

- **Repo call sites.** There are 26 production `.WithComparer()`/`.WithNullableComparer()` calls plus 10 direct
  `ComparerRegistry.For<>` calls in this repo.
- **Spark.** Spark has 8 of these calls. About 7 of its generators `.Collect()` `[AutoValueComparer]` models
  **without** one, so they get reference equality and never cache.
- **AspNetCore.Tools.** Its `Models.cs:625-678` comments explicitly that Tools' `[ValueComparer]` on `LocationKey`
  and `PathSpec` is useless under `EqualityComparer<T>.Default`, and names #184 as the fix. It hand-writes 14
  `IEquatable<T>` models and a `SequenceComparer<T>` instead.

### P5: dead and half-dead surface

- **Dead:** `ICompilationCache` is a parameter of the abstract `IncrementalGenerator.Initialize` (`IncrementalGenerator.cs:78`) that every generator
  overrides: 11 here, 12 in Spark, 1 in AspNetCore.Tools. Nothing ever calls `GetOrCreate`.
- **Unused anywhere:** `IEnumerableValueComparer`, `IReadOnlyCollectionValueComparer`, `SourceTextValueComparer`,
  `SymbolValueComparer`, `SyntaxValueComparer`.
- **Used only by tests:** `DictionaryValueComparer`, `KeyValuePairValueComparer`, and the nullable-tuple comparers.
- **Wasted scanning:** the generator scans **every** `ClassDeclarationSyntax` (`ValueComparerGenerator.cs:22`), not
  `ForAttributeWithMetadataName`.

### P6: shapes that don't work at all

- **Records and structs are never seen.** `RecordDeclarationSyntax` is not a `ClassDeclarationSyntax`.
- **Generic models emit uncompilable code.** The comparer name is concatenated as `Foo<T>ValueComparer`.

## Goals

1. **Value equality under the default comparer.** Every `[AutoValueComparer]` type is value-equal under
   `EqualityComparer<T>.Default`: in any pipeline step, inside a `Combine` tuple, as a property of another model, in
   a `HashSet`, and in `Assert.Equal`. **No `.WithComparer()` exists anymore.**
2. **No functional regression.** Everything under [Behaviours to preserve](#behaviours-to-preserve) holds, and S4
   proves it against the old comparers.
3. **Faster and allocation-free.** Equality on typed collections allocates zero bytes, and string-heavy models are
   at least 5x faster (benchmark B1).
4. **Less code for generator authors.** A model is the attribute plus its properties. Nothing else is written: no
   comparer, no `.WithComparer()`, no hand-written `IEquatable`.
5. **All shapes supported.** Sealed and non-sealed classes, abstract hierarchies, records (sealed or derived),
   structs, record structs, generics, and types nested in any of these.
6. **Contract correct by construction.** `GetHashCode` hashes exactly what `Equals` compares.

## Non-goals

- **Backward compatibility** with `ValueComparer<T>`, `ComparerRegistry` or `ICompilationCache`. The user
  explicitly opted out.
- **`==`/`!=` operators.** They would silently change existing reference comparisons in user code.
- **Equality for undecorated types.** Equality stays opt-in through the attribute.

## Design

### D1: what gets generated

The generator emits one file per model (`<Type>.Equality.g.cs`), keeping the `#nullable enable` header and
`global::` qualification of every name. The model's partial gets these members:

- `IEquatable<T>` in the base list.
- `Equals(T?)`, which begins with the null and `ReferenceEquals` shortcuts.
- `Equals(object?)`, except for records and record structs, where the compiler synthesizes it.
- `GetHashCode()`, computed over the same property list as `Equals` (P3 fixed by construction).

**Sealed class** with `string`, `IReadOnlyList<string>`, `ImmutableArray<Child>` and an ignored property:

```csharp
partial class CliOption : global::System.IEquatable<global::Ns.CliOption>
{
    public bool Equals(global::Ns.CliOption? other)
    {
        if (other is null) return false;
        if (global::System.Object.ReferenceEquals(this, other)) return true;
        return global::System.String.Equals(Name, other.Name, global::System.StringComparison.Ordinal)
            && global::MintPlayer.SourceGenerators.Tools.ValueEquality.List(Aliases, other.Aliases)
            && global::MintPlayer.SourceGenerators.Tools.ValueEquality.ImmutableArray(Children, other.Children);
        // CachedText: [ComparerIgnore]
    }

    public override bool Equals(object? obj) => Equals(obj as global::Ns.CliOption);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + (Name is null ? 0 : global::System.StringComparer.Ordinal.GetHashCode(Name));
            h = h * 31 + global::MintPlayer.SourceGenerators.Tools.ValueEquality.ListHash(Aliases);
            h = h * 31 + global::MintPlayer.SourceGenerators.Tools.ValueEquality.ImmutableArrayHash(Children);
            return h;
        }
    }
}
```

The comparison and hash for each property are chosen **from its type symbol at generation time**, never by runtime
lookup:

| Declared property type | Equality | Hash |
| --- | --- | --- |
| `string` | `string.Equals(a, b, Ordinal)` | `StringComparer.Ordinal` |
| primitive, enum | `==` | `GetHashCode()` (enum: cast to underlying) |
| `Nullable<T>`, generic `T`, any other type | `EqualityComparer<T>.Default` | same |
| `T[]` (rank 1) | `ValueEquality.Array` | `ArrayHash` |
| `ImmutableArray<T>` | `ValueEquality.ImmutableArray` (default == default, default != empty) | `ImmutableArrayHash` |
| `List<T>`, `IList<T>`, `IReadOnlyList<T>` | `ValueEquality.List` (indexed loop) | `ListHash` |
| `IEnumerable<T>`, `IReadOnlyCollection<T>`, other sequences | `ValueEquality.Sequence` (count fast path, then enumerate) | `SequenceHash` |
| `IDictionary<K,V>`, `IReadOnlyDictionary<K,V>` | `ValueEquality.Dictionary` (order-insensitive, see D6) | sum of entry hashes |
| `ValueTuple<…>` | item by item, recursively applying this table | combined |
| property with `[UseEqualityComparer(typeof(C))]` | `C.Instance.Equals` | `C.Instance.GetHashCode` |

Nested element types compose. `ImmutableArray<ImmutableArray<Model>>`, or a tuple holding a list, passes an element
comparer: `ValueEquality.ImmutableArray(a, b, ValueEquality.ImmutableArrayComparer<Model>.Instance)`. The helper
exposes a sealed comparer type with a static `Instance` for each collection shape. A comparer tree that is
reflection-built at runtime today is chosen at compile time instead.

### D2: hierarchies use an exact-type check plus a virtual core

This replaces the derived-type `switch`, and with it the source of P2. It follows the shape the compiler uses for
records:

```csharp
partial class Node : global::System.IEquatable<global::Ns.Node>
{
    public bool Equals(global::Ns.Node? other)
        => other is not null
        && (global::System.Object.ReferenceEquals(this, other)
            || (other.GetType() == GetType() && EqualsCore(other)));
    public override bool Equals(object? obj) => Equals(obj as global::Ns.Node);
    public override int GetHashCode() => HashCore();
    protected virtual bool EqualsCore(global::Ns.Node other) => /* Node's properties */;
    protected virtual int HashCore() => /* Node's properties */;
}

partial class Leaf : global::System.IEquatable<global::Ns.Leaf>
{
    public bool Equals(global::Ns.Leaf? other) => base.Equals(other);
    protected override bool EqualsCore(global::Ns.Node other)
        => base.EqualsCore(other) && /* Leaf's own properties, on (Leaf)other */;
    protected override int HashCore() => unchecked(base.HashCore() * 31 + /* Leaf's own */);
}
```

- **Symmetric and non-recursive.** The relation is symmetric, because the exact-type check comes first. It can't
  recurse, because a property's `Equals` never re-enters its declaring type.
- **Non-sealed concrete types** get the same shape. Their `GetType()` check replaces #184's shape 2.
- **Derived types must be covered.** A derived type that is left out would inherit `EqualsCore` and compare only the
  base's properties. That is stale caching, worse than today's `_ => false`. So:
  - the generator emits members for **every** derived type in the compilation whose base is decorated, whether or
    not the derived type carries the attribute;
  - a derived type that is not `partial` gets a new **Error**, `MINT003`: *"'Leaf' derives from [AutoValueComparer]
    type 'Node' and must be partial."*

### D3: records, structs, generics, nesting

- **Record.**
  - Emit `public virtual bool Equals(R? other)` and `GetHashCode()`. A sealed record gets `public bool` instead
    (CS8872).
  - The exact-type check uses the compiler's `EqualityContract`.
  - A derived record calls `base.Equals((Base?)other)` and then compares its own properties.
  - `Equals(object)` is synthesized by the compiler and not emitted.
- **Struct and record struct.** Use `readonly bool Equals(S other)`, with no null path. `Equals(object)` pattern-matches, so
  the typed path doesn't box. For a record struct, `Equals(object)` is omitted.
- **Declaration keyword.** The model and every reopened parent use their own declared keyword (`class`, `record`,
  `record class`, `struct`, `record struct`), not a hard-coded `partial class`.
- **Discovery.** Switch to `ForAttributeWithMetadataName`, which handles records and structs and stops scanning
  every class (P5, P6). The hierarchy lookup of D2 is built from the attributed roots; spike S5 finds the cheapest
  incremental way to do that.
- **Generics.** The type parameter list appears on every mention, for example `IEquatable<global::Ns.Box<T>>`.
  Values of type `T` compare through `EqualityComparer<T>.Default`.

### D4: equality the type already declares

For each of `Equals(T)`, `Equals(object)` and `GetHashCode()` that the user has already declared, that member is not
emitted. `IEquatable<T>` is still added to the base list if the user's `Equals(T)` exists.

- **`MINT002`, Info:** names each skipped member. *"'X' already declares Equals(object); [AutoValueComparer] did not
  generate it."*
- **Warning instead:** when the user declared only one of `Equals(object)` and `GetHashCode()`, because the pair
  would disagree.

### D5: the helper lives in Tools as `ValueEquality`

`MintPlayer.SourceGenerators.Tools.ValueEquality` is a public static class targeting netstandard2.0. It uses no
reflection, has no caches, and allocates nothing on typed paths. It contains the methods in the D1 table, their
`*Hash` counterparts, and a sealed `IEqualityComparer<…>` with a static `Instance` for each collection shape.

**Why Tools, and not a file-local helper emitted into each assembly:**

- **The dependency stays anyway.** Every consumer of generated code already references Tools for the
  `IncrementalGenerator` base and `ProduceCode`, so dropping the reference from generated code gains nothing.
- **One implementation.** The helper is tested once, not emitted N times.
- **No language-version fallback.** It has no dependency on C# 11 `file` types, and no CS0436 clashes through
  `InternalsVisibleTo`.

Spike S6 checks the one argument against this choice: a VCG-only consumer that doesn't reference Tools. If one
turns up, the fallback is to emit the same class as `internal` into the consuming assembly.

Hashing is an inline `h * 31 + x` fold, so no `System.HashCode` polyfill is needed. **`HashCodeCompat` is deleted**
unless something outside the comparers still uses it; M1 checks that.

**`EquatableArray<T>`** is also added to Tools. It is a readonly struct over `T[]` implementing `IEquatable`,
generalising the one in `Assertions/MintPlayer.Assertions.SourceGenerator/Models/EquatableArray.cs`. It is for
steps whose output is a freshly built collection: `.Collect()` followed by a `Select` that filters or projects, and
`ServiceRegistration[]`. Those are the only places where `.WithComparer()` still does real work today (see S2).
Those steps return `EquatableArray<T>`, and no comparer call remains anywhere.

### D6: behaviour decisions (where today's behaviour is accidental)

- **Dictionaries become order-insensitive:** equal counts, and every key found with an equal value. Today they fall
  into `TryListEquals`, which is order-sensitive over boxed `KeyValuePair`s. No model in any repo has a dictionary
  property, so nothing depends on the old behaviour.
- **Collection hashes include the elements**, not just their count. AspNetCore.Tools' `SequenceComparer` hashes only
  the length, which is legal but makes collisions certain.
- **A declared `IReadOnlyList<string>` holding a `string[]`** equals one holding a `List<string>`, exactly as today.
  Comparison is by the declared type, never the runtime type.

### D7: extension point replacing `ComparerRegistry.Register`

There is one new attribute in `.Attributes`:

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class UseEqualityComparerAttribute(Type comparerType) : Attribute;
```

- **The comparer type:** any `IEqualityComparer<TProp>` with a static `Instance` or a parameterless constructor. The
  generator validates that at compile time with a new **Error**, `MINT004`.
- **Package impact:** the `MintPlayer.ValueComparers.NewtonsoftJson` package changes `JObjectValueComparer` into a
  plain `IEqualityComparer<JObject>` with an `Instance`.
- **What it replaces:** the property attribute replaces runtime registration, and the Tools module initializer goes.

### D8: analyzers

- **`MINT001`, retargeted.** Today `WithComparerRoslynTypeAnalyzer` triggers on `WithComparer`/`WithNullableComparer`
  calls. It now inspects the properties of `[AutoValueComparer]` types. The rule stays the same: no `ISymbol`,
  `SyntaxNode`, `Compilation` or `SemanticModel` in models. Its 16 tests are rewritten against the new trigger.
- **`MINT005`, Warning, new.** It fires when a property type has only reference equality:
  - a class that neither overrides `Equals` nor implements `IEquatable<T>`;
  - not a collection handled by D1;
  - not a model;
  - with no `[UseEqualityComparer]`.

  Today this case fails silently, which is the kind of never-caching bug #183 was about.

### D9: what gets deleted, and what replaces it in this repo

**Deleted from Tools:**
- `ValueComparer<T>` and its `Helpers`, `Registry` and `ModuleInitializer` partials, `ComparerRegistry` and
  `ValueComparerAttribute`.
- Every class under `ValueComparers/`: the structural comparers, the tuple comparers, and the
  Symbol/Syntax/SourceText comparers.
- `ReferenceEqualityComparer<T>`, if its one call site (`IncrementalGenerator.cs:15`) goes away with the cache
  provider.
- `ComparerCacheHub`, `ICompilationCache`, `PerCompilationCache` and `SymbolPair`.
- The `ICompilationCache` parameter of `IncrementalGenerator.Initialize`.

**Deleted from the generator:** the generated `XValueComparer` classes, the `[ValueComparer]` tag, and the five
`ValueComparerExtensions` methods per model.

**Hand-written `IEquatable<T>` is required where the generator can't run** (the bootstrap problem):

| Type | Where | Why hand-written |
| --- | --- | --- |
| `LocationKey`, `PathSpec`, `PathSpecElement`, `AnalyzerInfo`, `LangVersion`, `Settings` | Tools | the generator references Tools, so Tools can't use it |
| `ClassDeclaration`, `PropertyDeclaration`, `TypeCandidate`, `TypeTreeDeclaration`, `DerivedType`, `BaseType` | ValueComparerGenerator | a generator can't generate its own models |

These implementations also fix P3's missing hashes. They use `ValueEquality`, so they stay short.

**Call sites and tests in this repo:**
- **Call sites:** remove all 26 `.WithComparer()`/`.WithNullableComparer()` calls and the 10
  `ComparerRegistry.For<>` calls in production code. Steps that output a freshly built array switch to
  `EquatableArray<T>`.
- **Tests:** rewrite `ValueComparerTests` (43 tests), `ComparerRegistryResolutionTests` (15) and
  `TupleValueComparerTests` (12) as `ValueEqualityTests` over the same cases. They move over case by case, and none
  is dropped without a named replacement. Also rewrite `JObjectValueComparerTests` (15).

## Behaviours to preserve

Each item gets a `ValueEqualityTests` or generated-model test case, and S4 checks it against the old comparer:

1. **Null and reference shortcuts:** both null is equal, one null is unequal, and the same reference is equal
   without comparing properties.
2. **`[ComparerIgnore]`** is excluded from equality, and now from the hash as well.
3. **`ImmutableArray`:** default == default, default != empty, and element-wise otherwise.
4. **Order-sensitive element-wise comparison** for arrays, `List`, `ImmutableArray`, `IReadOnlyList` and
   `IEnumerable`, independent of the collection's runtime type.
5. **Tuples** compare item by item, with structural items.
6. **Strings** compare ordinally.
7. **Nested models** compare by value, at any depth, including `List<Model>` and nullable nested models (Spark
   uses both).
8. **Derived types from outside the known tree** are never equal to a base instance.
9. **JObject** equality in the NewtonsoftJson package keeps its current semantics. Its 15 tests are ported one to
   one.
10. **Incremental caching:** every `IncrementalOutputCachingTests` theory from #183 stays green **with all
    `.WithComparer()` calls removed**.

## Spikes

Each spike has a pass/fail criterion. Results go into a *Spike results* section appended to this file, as
`PRD-IncrementalCaching.md` does.

| # | Question | Method | Pass |
| --- | --- | --- | --- |
| **S1** | Is P1's cost real in the pipeline, not only in a microbenchmark? | Turn the scratchpad probe into a test: measure `IsEquals<string>` allocation with `GC.GetAllocatedBytesForCurrentThread`, and time one `RunGenerators` on the MintPlayer.SourceGenerators test corpus. | Numbers recorded, used as the baseline for B2. |
| **S2** | Once `T : IEquatable<T>`, does Roslyn report `Cached`/`Unchanged` for `Collect()` and `Combine` without a comparer? Does it compare collected arrays element-wise when an input changed? Estimated confidence is 85% for the first question and 50% for the second. | Build a tracked pipeline `Select → Collect → Select(filter) → Combine` without `WithComparer`. Make an unrelated edit, then change one item. Read the `IncrementalGeneratorRunStep.Outputs` reasons. | The unrelated edit gives Cached/Unchanged at every step. If the filtered `Select` reports Modified, that confirms `EquatableArray<T>` is needed there (D5). |
| **S3** | Does every shape compile cleanly? | A compile matrix with warnings as errors, over class, sealed, non-sealed concrete, abstract tree, record, sealed record, derived record, struct, record struct, generic, nested in a record or struct, and user-declared equality. Run it at LangVersion 9, 11 and latest. | Zero diagnostics, in particular no CS8851, CS8872, CS0659, CS0661 or CS0436. |
| **S4** | Does the new code agree with the old comparers? | Before deleting anything, property-test all 25 repo models, the 11 `ValueComparerDebugging` models and the ported hand-written ones. Use randomly generated equal and unequal pairs, with the old `XValueComparer` as the oracle. | `a.Equals(b) == oracle.Equals(a, b)` in every case, except the documented D6 changes and the P2 overflow case. Equal instances hash equally. Equality is symmetric. |
| **S5** | What is the cheapest incremental way to find the derived types of a decorated base? | Compare `ForAttributeWithMetadataName` for roots plus a syntax filter on `BaseListSyntax` for derived types, against today's scan of every class. Measure with the #183 caching tests. | An unrelated edit keeps every step Cached. It is no slower than today on B2. |
| **S6** | Does any `[AutoValueComparer]` consumer lack a Tools reference? | Search the downstream survey list and the nupkg dependency graphs. | None found, so D5 stands. Otherwise switch to emitting an `internal` helper. |

## Benchmarks

**New project:** `SourceGenerators/MintPlayer.SourceGenerators.Tools.Benchmarks`, cloned from
`Assertions/MintPlayer.Assertions.Benchmarks`.
- It targets net11.0 and uses BenchmarkDotNet with `[MemoryDiagnoser]`.
- A `Verification.Run()` fairness gate runs before the `BenchmarkSwitcher`.
- It adds a **net481 job**, because Visual Studio runs analyzers in-process on .NET Framework.

**B1, per-call equality.**
- **Variants:** Today (`XValueComparer.Instance`), design A (#184 as written: delegation), and this design.
- **Shapes:**
  - a flat model with 3 strings of ~30 chars;
  - `IReadOnlyList<string>` ×20;
  - `ImmutableArray<Model>` ×50;
  - a `(Model, Model)` tuple;
  - a 3-level abstract tree;
  - a Spark-like `List<Model>`.
- **Operations:** an equal pair, a pair differing in the last property, and `GetHashCode`.
- **Gate:** all variants agree on the answer.
- **Pass criteria:**
  - allocates 0 B on typed collection shapes (at most 1 enumerator on `IEnumerable<T>`);
  - at least 5x faster than Today on string-heavy shapes;
  - design A lands within 5% of Today, which confirms that delegating wouldn't have fixed P1.

**B2, pipeline.**
- **Setup:** a `GeneratorDriver` over 500 classes plus 50 models.
- **Measurement:** time one `RunGenerators` after an unrelated method-body edit, and after a relevant edit, for
  ValueComparerGenerator, MapperGenerator and ServiceRegistrationsGenerator, before and after the change.
- **Pass:** no slower, and fewer bytes allocated, in both cases.

## Milestones

Tests are batched at the end, per the house rule. Intermediate milestones are verified by building.

1. **M0, spikes S1, S2, S5 and S6.** Findings are recorded here. If S2 contradicts D5, revise the design before
   writing code.
   - *Status: done. S2 confirmed D5; see [Spike results](#spike-results).*
2. **M1, `ValueEquality` and `EquatableArray<T>` in Tools**, with the old runtime still present, so that S4 can use
   it as the oracle.
   - *Status: done.*
3. **M2, the new producer.** Covers D1 to D4, `ForAttributeWithMetadataName` discovery, `[UseEqualityComparer]`, and
   `MINT002` to `MINT005`. Run S3 here.
   - *Status: done. `MINT006` (non-partial containing type) was added, following S5. S3 passed in all 37 cases.*
4. **M3, S4 oracle run and benchmarks B1/B2.** Results are recorded here.
   - *Status: S4 done, with 0 deviations. B1/B2: see [B1/B2](#b1b2-benchmarks-measured).*
5. **M4, deletion.**
   - Everything under D9, plus hand-written `IEquatable` for the 12 bootstrap types.
   - Remove the 26 + 10 comparer call sites, and switch collection-valued steps to `EquatableArray<T>`.
   - Change the `IncrementalGenerator.Initialize` signature, and fix all 11 overrides.
   - Retarget `MINT001`, and port the NewtonsoftJson package.
   - *Status: done. `MINT001` is now `RoslynTypeInModelAnalyzer`.*
6. **M5, tests and docs.**
   - Snapshot tests of the generated file for every S3 shape.
   - Port the comparer tests, and add runtime-equality tests for generated models, which would have caught P2.
   - Run the whole suite with no `.WithComparer()` left.
   - Update the ValueComparerGenerator README (generated members, no `.WithComparer()`, `[UseEqualityComparer]`)
     and add a Tools CHANGELOG/breaking-change note for 12.0.0.
   - Bump the versions to 12.0.0.
   - *Status: done.*
     - *Versions: every SourceGenerators package moves to 12.0.0 in lockstep, as in #183.
       MintPlayer.Assertions, which ships Tools in its analyzer payload, moves to 11.0.0-rc.4.*
     - *Changelog: the breaking changes and a migration guide are in `SourceGenerators/CHANGELOG.md`. The
       Tools, ValueComparerGenerator and NewtonsoftJson READMEs link to it.*
     - *Tests: the full solution passes locally in Release, and CI passes on #185.*
7. **M5b, the Assertions source generator.** Added at the owner's request. It moves from hand-written
   `IEquatable<T>` models to `[AutoValueComparer]`, and its analyzer payload ships
   `MintPlayer.ValueComparerGenerator.Attributes.dll`, which the owner accepted.
8. **M6, downstream**, after 12.0.0 is published (see below). *Status: not started, since it depends on the
   publish.*

## Downstream migration

Only two real consumers exist; the others were checked and have no reference. The demo repos `SourceGeneratorDemo`
and `ClassListGenerator` pin old versions and are left alone unless asked.

**MintPlayer.Spark** (Tools 10.21.0, VCG 10.20.2):
- Bump the packages to 12.0.0.
- Delete the 8 `.WithComparer(...)` calls. The 2 over `ImmutableArray<…>` become `EquatableArray<T>` if S2 says so.
- Drop the `ICompilationCache` parameter from 12 `Initialize` overrides, and the stale
  `using …Tools.ValueComparers;` from 12 files.
- **Nothing to change in the 25 models.** Their `List<string>`, `List<Model>`, `LocationKey?` and `PathSpec?`
  properties are covered by D1 and D9.
- **Side effect:** the ~7 generators that `.Collect()` models without a comparer start caching.
- **Check:** the published AllFeatures nupkg doesn't appear to pack Tools.dll or the Attributes dll
  (`MintPlayer.Spark.AllFeatures.csproj:54-65`). Confirm this on the actual nupkg.

**MintPlayer.AspNetCore.Tools** (Tools 11.0.0, no VCG):
- Add the VCG packages.
- Replace the 14 hand-written `IEquatable<T>` models (in `Models.cs`, `ClientModels.cs` and `BoundProperty.cs`) with
  `[AutoValueComparer]` partials. That removes roughly 300 lines.
- Delete `SequenceComparer<T>` and its 3 `.WithComparer` calls, which become `EquatableArray<T>` or nothing.
- Delete the `LocationKeys.AreEqual`/`PathSpecs.AreEqual` helpers, since Tools' types are now `IEquatable`.
- Drop the `ICompilationCache` parameter from `EndpointGenerator`.
- Pack `MintPlayer.ValueComparerGenerator.Attributes.dll` next to Tools.dll in the `GetDependencyTargetPaths` target
  (`Generator.csproj:184`) and in the `analyzers/dotnet/roslyn5.9/cs` pack path. Extend the existing pack guards to
  require it.

Each downstream repository gets its own single PR, since a repository can't share a PR. Sequencing: publish 12.0.0
from this repo first, then open both downstream PRs.

## Acceptance criteria

1. **Default comparer works:** every model shape from S3 is value-equal under `EqualityComparer<T>.Default`. A
   pipeline step without any comparer reports `Cached`/`Unchanged` after an unrelated edit.
2. **Nesting works:** a model holding a model, an `ImmutableArray<Model>`, a `List<Model>` and a `(Model, Model)`
   tuple is equal with no comparer anywhere.
3. **The old APIs are gone:** no `ValueComparer<T>`, `ComparerRegistry`, `[ValueComparer]`, `ICompilationCache` or
   `.WithComparer(` exists in the repo. A grep test enforces this.
4. **S4 oracle agreement:** recorded, with every deviation traced to D6 or P2.
5. **Benchmarks:** B1 and B2 pass, and the numbers are recorded in this file.
6. **Contract:** equal instances hash equally for every model in the repo. There are regression tests for the
   `[ComparerIgnore]` hash bug and the P2 stack overflow.
7. **Snapshot tests** exist for every generated shape.
8. **Diagnostics:** `MINT002` to `MINT005` have tests, and `MINT001` is retargeted with its cases ported.
9. **Docs:** the README and the breaking-change notes are updated.
10. **Downstream:** Spark and AspNetCore.Tools build and pass their test suites on 12.0.0.

## Open questions for the owner

1. **Where does `ValueEquality` live?** *Resolved: in Tools (D5).* S6 found no consumer without a Tools reference.
2. **Dictionary semantics.** *Resolved: order-insensitive (D6).* No model in any repo has a dictionary property, so
   nothing depended on the old, accidental behaviour.
3. **Package name.** *Open.* `[AutoValueComparer]` no longer generates a comparer. Should the attribute be renamed
   to `[AutoEquatable]`? The name is kept for now, since renaming is a second breaking change on top of this one.
   The owner confirmed that the generator itself stays: it is what writes the equality members.

## Implementation notes: where the code deviates from the design above

None of these changes behaviour that any existing model relies on; S4 found 0 differences.

- **`IList<T>` and `ICollection<T>` use `ValueEquality.Sequence`, not `List`.** `IList<T>` doesn't implement
  `IReadOnlyList<T>`, and an extra overload would be ambiguous for `List<T>`. `Sequence` still takes the indexed
  path at runtime.
- **`float`/`double` compare with `.Equals`, not `==`,** so `NaN` equals itself, as it did with the old comparer.
- **`[UseEqualityComparer]` goes through a cached `static readonly IEqualityComparer<T>` field** (`C.Instance` or
  `new C()`). That handles explicit interface implementations.
- **One descriptor for both MINT002 severities.** The "only one of `Equals(object)`/`GetHashCode()`" case uses the
  same descriptor raised at Warning severity: a second descriptor with the same id trips RS2001.
- **`MINT003` also covers a non-partial attributed type,** not only a non-partial derived type.
- **Records and record structs don't list `IEquatable<T>`,** because the compiler already adds it.
- **Struct members are `readonly` only when that causes no defensive copy** (CS8656).
- **Base classes:**
  - A class root also compares properties inherited from non-model base classes.
  - A record root with a plain base record delegates to `base.Equals`.
  - Derived types skip `override` properties.
- **Global-namespace models get no namespace block.** The old generator put them in `RootNamespace`.
- **`MINT001`:**
  - It is now `RoslynTypeInModelAnalyzer`. It looks through arrays, nullables, tuples, generic arguments and plain
    classes, but not into nested models, so each bad property is reported once.
  - It still fires on `[ComparerIgnore]` properties, because holding a symbol pins the compilation either way.
  - `MINT005` skips Roslyn types, so the two don't report the same property.
- **Equality changes for Tools types:**
  - `PathSpecElement` equality now includes `GenericTypeParameters`, which the old comparer left out.
  - `LocationKey` is sealed.
- **`newtonsoftjson.targets` is no longer imported by MintPlayer.SourceGenerators.** The import only existed to
  register the JObject comparer.
- **The AC3 guard test skips `*.Tests` projects,** because their assertions legitimately name the removed APIs.

## Spike results

The spike projects live outside the repo, in the session scratchpad; they are throwaway.

**Setup:** net10.0, Release, Microsoft.CodeAnalysis.CSharp 5.9.0.

### S1: today's per-call and pipeline cost (measured)

"Today" is a copy of what the producer emits, deriving from the real Tools `ValueComparer<T>`. "IEquatable" is
hand-written, with ordinal string compares and indexed loops, which is what D1 generates.

| Shape | Op | Today ns / B | IEquatable ns / B |
| --- | --- | --- | --- |
| 3 strings of 30 chars | equal | 5,248 / 10,272 | 30 / 0 |
| 3 strings of 30 chars | `GetHashCode` | 4,322 / 7,296 | 72 / 0 |
| `IReadOnlyList<string>` ×20 | equal | 1,620 / 1,872 | 113 / 0 |
| `ImmutableArray<Child>` ×50 | equal | 90,617 / 171,600 | 181 / 0 |
| `ImmutableArray<Child>` ×50 | last element differs | 127,076 / 171,600 | 248 / 0 |
| bare 64-char string (P1) | equal | 6,155 / 7,232 | 5.5 / 0 |

**P1 is confirmed.** The new code is 50x to 500x faster and allocates nothing. A pair that differs only in its last
property costs Today as much as an equal pair.

**Pipeline baseline for B2.** The 5 generators in MintPlayer.SourceGenerators ran over 500 files, 50 of them
`[Register]`/`[Inject]` classes. Allocation is the stable metric; wall time varies by ±30%.

| Edit | Allocated per `RunGenerators` | Median |
| --- | --- | --- |
| unrelated method body | 6.16 MB | 22.7 ms |
| relevant (lifetime toggle) | 6.30 MB | 19.8 ms |

### S2: Roslyn with `IEquatable` models and no `.WithComparer()` (measured)

Pipeline: `ForAttributeWithMetadataName → Collect → Select(filter).ToImmutableArray() → Combine`, plus a per-item
`Combine` into a `(M, string)` tuple.

- **Unrelated edits leave every step Cached or Unchanged.** Covered: an edit elsewhere, a method-body edit in an
  attributed file, and a comment edit. `Collect()` needs no comparer: the transforms re-run, `M.Equals` marks them
  Unchanged, and `Collect` reports Cached.
- **Tuples need nothing.** A per-item `Combine` into `(M, string)` stays Cached on its own.
- **A step that builds a new collection is the one exception.** A `Select` that filters after `Collect` re-runs
  whenever any item changes, even one it filters out. An `ImmutableArray` result then reports **Modified** although
  its contents are equal, and the output is regenerated for nothing. An `EquatableArray<T>` result reports
  **Unchanged**, and everything downstream stays Cached.

**The design is confirmed:** no `.WithComparer()` is needed anywhere. Steps that build a collection return
`EquatableArray<T>` (D5).

### S5: discovering derived types (measured)

**Strategy B is adopted.** It combines two providers:
- `ForAttributeWithMetadataName` for the attributed roots;
- `CreateSyntaxProvider` over class and record declarations that have a base list. Its transform walks
  `BaseType.OriginalDefinition` up to the nearest attributed ancestor and returns an equatable model, or null.

It never collects over all classes.

**Corpus:** 500 plain classes, 40 roots (5 records, 3 structs) and 3 abstract trees. The trees include these derived
types:
- one without the attribute, one in a different file, and a transitive grandchild;
- one with a generic base, one whose base is named only in its second partial part, and a nested type;
- a derived record, one declared through a `using` alias, and one that is not partial.

Each strategy was checked against a walk over every source type symbol.

| Strategy | Correct in all 8 scenarios | Unrelated edit: output steps | Unrelated edit: time / allocated |
| --- | --- | --- | --- |
| A: today's shape (all classes, Collect) | no: misses records and structs | Cached | 16–22 ms / 5.0 MB |
| **B** | **yes**, and MINT003 fires on the non-partial type | **Cached** | **6.3–6.7 ms / 1.4 MB** |
| C: syntax-only name closure | no: misses `using`-alias bases and bases in referenced assemblies | Cached | 5.3–7.7 ms / 1.2 MB |
| Today's real generator (Debug dll) | n/a | n/a | 57 ms / 16.8 MB |

**Implementation consequences:**
- **`AutoValueComparerAttribute`** gains `AttributeTargets.Struct`.
- **Models carry no `Location`,** since a span-bearing model turns Modified when anything above it moves. MINT003
  gets an equatable file/line span, carried only by non-partial types.
- **A derived type whose containing types aren't all partial** is reported as well (this is `MINT006`).
- **Record structs** are roots only, since they can't be derived from.
- **Heavy work goes after the null filter.** Property reading and the like belong in a later step.

**A local type deriving from an attributed base in a referenced assembly** gets
`override EqualsCore`/`HashCore`. That assumes the base was built with a 12.x generator: the protected virtual
members are part of the generated contract.

### S6: consumers without a Tools reference (checked)

- **Today:** generated code already depends on Tools types (`ValueComparer<T>`, `ComparerRegistry`), so every current
  consumer references Tools. The generator package doesn't bring Tools in itself; consumers add it next to it.
- **Scratch folders:** the only exceptions are pre-10.0 projects there, which are irrelevant.

**D5 stands:** `ValueEquality` lives in Tools.

### S3: compile matrix (measured, M2)

`EqualityCompileMatrixTests` runs the generator over each of the 13 fixtures in `Snapshots/EqualityShapes.cs` at
LangVersion 9, 11 and latest. The fixtures cover sealed, non-sealed, abstract tree, record, sealed record, derived
record, struct, record struct, generic, nested, user-declared, `[UseEqualityComparer]` and nested collections.
Nullable is on and warnings are errors. The record-struct and `record class` fixtures skip C# 9, which leaves 37
cases.

- **Result:** zero compiler diagnostics in all 37 cases, so no CS8851, CS8872, CS0659, CS0661, CS0436 or CS8656.
- **Generator diagnostics:** the only one is MINT002, and only on the user-declared fixture.
- **Real models:** the generator also ran over the repo's own models in MintPlayer.SourceGenerators, Mapper and
  CliGenerator. The generated files compiled there with no errors.

### S4: oracle agreement with the old comparers (measured, M2)

The harness is two scratch console processes that share one source file (it lives in the session scratchpad and is
not in the repo).

- **Models:** the property shapes of all 25 repo models. `PathSpec`, `PathSpecElement` and `LocationKey` are
  replaced by local `[AutoValueComparer]` copies, which makes 28 types.
- **OLD process:** built with master's generator and Tools, and compares through `ComparerRegistry.For<T>()`, which
  is the generated `XValueComparer`.
- **NEW process:** built with this branch, and compares through `EqualityComparer<T>.Default`.
- **Pairs:** a seeded reflection populator makes 300 pairs per type. Each seed gives an equal pair (built twice),
  a pair with one property changed, and a comparison against null.
- **Edge cases the populator covers:** null strings, null arrays, default vs empty vs populated `ImmutableArray`,
  and `IReadOnlyList`/`IList` backed alternately by `T[]` and `List<T>`.

| | OLD | NEW |
| --- | --- | --- |
| Comparisons | 25,200 | 25,200 |
| Equal pairs equal (and equal hashes) | 8,400 / 8,400 | 8,400 / 8,400 |
| One-property pairs unequal | 7,037 | 7,037 |
| Symmetry or hash-contract violations | 0 | 0 |
| **Lines that differ** | | **0** |

No deviation was found. The expected ones are not exercised by these models:
- **D6:** no model has a dictionary property.
- **P2:** no repo model is a hierarchy.
- **`[ComparerIgnore]` hash:** no repo model uses the attribute.

The runtime tests cover all three instead:
- `ADerivedTypeWithoutTheAttribute_…` (P2);
- `AComparerIgnoreProperty_IsLeftOutOfEqualsAndOutOfTheHash` (the `[ComparerIgnore]` hash);
- `Dictionaries_CompareOrderInsensitively` (D6).
