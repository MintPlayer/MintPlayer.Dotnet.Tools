# Plan — MintPlayer.Assertions

Companion to [Assertions-prd.md](./Assertions-prd.md). **All features ship in v1, in a single
pull request.** Milestones below are commit boundaries inside that PR; the test suite runs
once, after the last milestone (repo policy).

## Architecture decisions (made up front, replacing the original spikes)

1. **Equivalency specialization: registry + `[ModuleInitializer]`, no interceptors.** The
   generator finds `BeEquivalentTo` call sites and `[AssertEquivalency]` types, emits typed
   member-accessor maps + comparers, and registers them at module load. Interceptors add
   toolchain fragility for zero additional capability here.
2. **Options are applied at runtime over generated accessors.** No compile-time lambda
   analysis needed; the accessors (not the option evaluation) were the reflection cost.
3. **One exception type** (`AssertionFailedException`) — no test-framework detection.
4. **Messages**: `[CallerArgumentExpression]` + `because` weaving + cycle-safe truncating
   formatter + string/collection diffs. Rendering built on plain `StringBuilder`
   (`MintPlayer.StringBuilder.Extensions` available if indentation helpers are needed).
5. **Async safety is analyzer-enforced**: un-awaited assertion task = **error** (MPA0001)
   with an add-`await` code fix.

## Milestone 1 — Scaffolding ✅/⏳

- Branch off master; create `Assertions/` family: `MintPlayer.Assertions`
  (net8.0;net9.0;net10.0), `MintPlayer.Assertions.SourceGenerator` (netstandard2.0, imports
  `SourceGenerators/eng/sourcegenerator.targets`), `MintPlayer.Assertions.Analyzers`
  (netstandard2.0), `MintPlayer.Assertions.Tests` (xUnit, net10.0), benchmark project
  (non-packable). Add to the solution mirroring disk layout.
- Standard metadata (Apache-2.0, snupkg, README with the license pledge); generator +
  analyzers packed as analyzer assets of the core package.

**Done when:** solution builds; `dotnet pack` produces the packages.

## Milestone 2 — Core primitives ⏳

`AssertionFailedException`, `AssertionScope` (ambient AsyncLocal, nesting, context naming,
combined failure report), the `Assertion` builder (`ForCondition/BecauseOf/FailWith`,
caller-expression capture), `AndConstraint<T>`/`AndWhichConstraint<T,TWhich>`, formatting
pipeline (object formatter, string diff, collection diff, truncation limits).

**Done when:** `Be`/`NotBe` on object works end-to-end with quality messages inside and
outside a scope.

## Milestone 3 — Value assertion categories ⏳

Scalars/booleans/`BeSameAs`/nullability; numerics via `INumber<T>` (`BeGreaterThan`,
`BeInRange`, `BeCloseTo`, `BePositive`, …); strings (`StartWith`, `Contain`, `Match`
wildcard+regex, diff on failure); `DateTime(Offset)`/`DateOnly`/`TimeOnly`/`TimeSpan`;
`Guid`, enums (`HaveFlag`), `Nullable<T>`, `IComparable<T>`, type/reflection assertions
(`BeAssignableTo<T>`, `BeDecoratedWith<TAttribute>`).

## Milestone 4 — Collections, dictionaries, spans, JSON ⏳

`HaveCount`, `Contain`, `ContainSingle` + `.Which`, `OnlyContain`, `Equal` (ordered),
`BeInAscendingOrder(selector)`, `AllSatisfy`, `SatisfyRespectively`, `BeSubsetOf`,
`IntersectWith`; dictionary assertions; `Span<T>`/`ReadOnlySpan<T>` (ref-struct assertion
types); `JsonElement`/`JsonNode` assertions (`BeJsonEquivalentTo`, `HaveProperty`, …).

## Milestone 5 — Exceptions, async, events, execution time ⏳

`Throw<T>`/`NotThrow` + `WithMessage`/`WithInnerException<T>`/`WithParameterName`;
`ThrowAsync`/`NotThrowAsync`/`NotThrowAfterAsync`; `CompleteWithinAsync`; event monitoring
(`Monitor()`, `Raise`, `WithSender`, `WithArgs`, `RaisePropertyChangeFor`);
`ExecutionTime().BeLessThan(...)`.

## Milestone 6 — Source-generated equivalency ⏳

Runtime equivalency engine (options object, recursion, cycle safety, ordered/unordered
collection matching, `Using<T>` member overrides, `Excluding`/`ExcludingNested`/`Including`,
`ComparingByMembers/ByValue`, member-level difference report) over an accessor abstraction;
reflection accessor provider (annotated fallback) + generated accessor provider; the
generator (call-site scan + `[AssertEquivalency]`, `[ModuleInitializer]` registration).

## Milestone 7 — Generators & analyzers ⏳

`[GenerateAssertion]` custom-assertion generator; analyzers: MPA0001 un-awaited assertion
(error + code fix), vacuous-assertion warnings; FluentAssertions/AwesomeAssertions →
MintPlayer.Assertions migration code fix (top-20 call shapes).

## Milestone 8 — Tests, dogfooding, benchmarks, PR ⏳

- Comprehensive self-hosted test suite for every category.
- Migrate this repo's existing test projects (`SlnLaunch.Tests`, `TokenReplacer.Tests`,
  `FolderHasher.Tests`, `Mapping.Tests`) to MintPlayer.Assertions — same PR.
- BenchmarkDotNet project comparing equivalency vs FluentAssertions 7 (present, not gating).
- READMEs + XML docs; single full test sweep; **one pull request** with everything.

**Done when:** `dotnet test` green across the repo; PR open.
