# Spike S3: closure lowering with interceptors (issue #189)

**Verdict: FAIL for interceptors. An interceptor cannot remove the closure.** The rewrite itself (a static
lambda plus a state tuple) works and gets to 0 B, but it has to happen in the user's source, so it
belongs in an **analyzer with a code fix**, not in a generator.

Environment: .NET SDK 11.0.100-rc.1 (compiler 5.11), generator built against Microsoft.CodeAnalysis.CSharp 5.9.0,
runtimes .NET 10.0.12 and .NET 11.0.0-rc.1, Windows 11 x64. Measured allocations only; no timing runs.

## Layout

| Path | What |
|---|---|
| `S3.Generator/` | netstandard2.0 incremental generator (`IsRoslynComponent`, `EnforceExtendedAnalyzerRules`, warnings as errors). `SiteAnalyzer.cs` classifies each call site and builds the rewrite; `InterceptorGenerator.cs` emits the interceptors and a `S3SiteReport` table. |
| `S3.Generator/build/S3.Generator.props`, `.targets` | Package-style `InterceptorsNamespaces` injection, tested below. |
| `S3.Consumer/` | net10.0;net11.0 console app. `Pipeline.cs` holds both `ExecuteAsync` overloads, `Shapes.cs` has 31 capture shapes, `Rewritten.cs` has the generator's rewrites pasted in, `Bench.cs` has the allocation cases, and `NonCompiling.cs` has the shapes the compiler rejects. |
| `S3.CacheCheck/` | A `GeneratorDriver` run with `trackIncrementalGeneratorSteps`, to check caching. |

Run with `dotnet run -c Release -f net10.0` or `-f net11.0` in `S3.Consumer`. Both print `ALL CHECKS PASSED`.

## Why an interceptor cannot do it

1. An interceptor only swaps the call target. The arguments are still bound and evaluated by the caller
   exactly as for the original method. So before the interceptor runs, `ct => Foo(id, ct)` has already been
   converted to a `Func<CancellationToken, ValueTask<T>>`: the display class and the delegate are both
   allocated.
2. The interceptor's signature must match the intercepted method. When the generator emitted an
   interceptor that takes `Func<object?, CancellationToken, ValueTask<T>>` (`-p:S3MismatchProbe=true`), the
   build failed with **CS9144** ("signatures do not match"). So the lambda cannot be made to bind to a
   state-taking delegate type.
3. Inside the interceptor, the captured values are reachable only through `callback.Target`, which is the
   already-allocated, compiler-named display class. So there is no state to pass except the delegate
   itself. The emitted interceptor therefore forwards
   `ExecuteAsync(static (cb, c) => cb(c), callback, ct)` and saves nothing.
4. Rejected workarounds:
   - Reading display-class fields with `UnsafeAccessorType`: the closure is still allocated, and the
     approach depends on compiler implementation details.
   - Relying on JIT escape analysis: it did not remove the closure even when the pipeline was
     `AggressiveInlining` (the last row of the table below).

## Allocations (bytes per call, 200,000 calls after 100,000 warm-up, sync-completing callback)

| Case | net10.0 plain | net10.0 **intercepted** | net10.0 rewritten | net11.0 plain | net11.0 **intercepted** | net11.0 rewritten |
|---|---|---|---|---|---|---|
| Non-capturing lambda | 0 | n/a | n/a | 0 | n/a | n/a |
| Captured local | 88 | **88** | **0** | 80 | **80** | **0** |
| `this` only | 64 | **64** | **0** | 56 | **56** | **0** |
| 2 locals + `this` (tuple state) | 104 | **104** | **0** | 96 | **96** | **0** |
| Async lambda (completes synchronously) | 88 | **88** | **0** | 80 | **80** | **0** |
| Call site in an `async` method | 88 | **88** | **0** | 80 | **80** | **0** |
| Local shared with another closure | 152 | n/a | 88 | 136 | n/a | 80 |
| Inlinable pipeline, closure (escape analysis?) | 88 | n/a | n/a | 80 | n/a | n/a |

- The "intercepted" rows are real: the interceptor ran 200,000 times.
- The allocation is the display class (24 B) plus the delegate. The delegate is 64 B on .NET 10 and 56 B on .NET 11.

## Capture shapes

"Lowered" means the analyzer produced a rewrite. Every Lowered rewrite was pasted into `Rewritten.cs`,
compiled without warnings, and returned the same result as the closure on both TFMs. Every Fallback
shape was left untouched, and its result matched the closure (no behaviour change).

| Shape | Outcome | Notes |
|---|---|---|
| Read-only captured locals | Lowered, 0 B | `(id: id, name: name)` → `__s.id`. |
| Captured parameters | Lowered, 0 B | Same as locals. |
| `this`, implicit (`_field`, `Helper()`) | Lowered, 0 B | State is `this`; the body uses `__s._offset`. Private members stay reachable because the rewrite is in place. |
| `this`, explicit, plus locals | Lowered, 0 B | `(id: id, self: this)`. |
| `this` in a struct | Not possible | Compiler error **CS1673**. Copying `this` to a local first is fine and lowers. |
| Local mutated after the call | **Fallback** | The naive rewrite returned 1 where the closure returned 2 (a write lands before the deferred callback runs). |
| Local mutated inside the lambda (`count++`, `ref counter`) | **Fallback** | The naive rewrite gave 10 instead of 11. |
| Local written only before the call | Fallback (conservative) | Safe in principle, but it needs flow analysis (loops, `goto`); not done. |
| Mutable struct, non-readonly member call | **Fallback** | Treated as a write. The naive rewrite gave 11 instead of 12. A readonly struct lowers. |
| `ref` local, `Span`/ref struct local or parameter, `ref`/`in`/`out` parameter | Not possible | Compiler errors **CS8175**, **CS9108**, **CS1628**. |
| Nested lambda that uses captured state | Fallback | Lowering would allocate a closure over `__s` on every invocation: no gain. |
| Nested lambda that does not use captured state (static) | Lowered, 0 B | |
| Async lambda | Lowered, 0 B when it completes synchronously | `static async (__s, c) => …`. The state-machine box on suspension is intrinsic. |
| Generic method (`TItem` in captured and result types) | Lowered | In place, so the type parameters are in scope. The interceptor, when used, was generic `<T>` and worked. |
| Static method group | Not needed | The delegate is cached (C# 11+): 0 B already. |
| Instance method group | Fallback | Lowerable to `(static (s, c) => s.M(c), this)`, not done. |
| `out var` inside the lambda | Lowered | A declaration inside the lambda is not a capture. |
| Capture shared with another closure | Lowered, with a note | Saves the delegate only, because the scope still allocates the display class (152 → 88 B). |
| Loop variable, captures inside an outer lambda | Lowered | |
| Typed lambda parameter `(CancellationToken c) =>` | Lowered | The state parameter is typed as `(int id, string name) __s`. |
| Anonymous-type or tuple projections of captured values | Lowered | Explicit names are kept (`id = __s.id`). |
| `base.M()` | Fallback | Non-virtual dispatch cannot go through a value. |
| Non-static local function of the enclosing method | Fallback | A static local function lowers. |
| `delegate { }` anonymous method | Fallback | Not handled. |
| Non-capturing or `static` lambda | Not needed | 0 B already. |
| Captured names that clash with tuple members (`Item1`, `Rest`, …) | Fallback | |

**The exact supported boundary** (these rules work unchanged for an analyzer and code fix):

- **Lower** when the lambda is a non-static lambda, and:
  - it captures only locals, value parameters, and/or `this` of a class;
  - no captured variable is written anywhere in the enclosing member. A write means assignment,
    compound assignment, `++`/`--`, `ref`/`out` argument, deconstruction target, `&`, a `ref` alias,
    a struct field write, or a non-readonly member call on a mutable struct;
  - no captured variable or `this` is used inside a nested lambda or local function;
  - there is no `base.` access and no call to a non-static local function from the enclosing method.
- **Everything else stays as written.**

## Other checks

**`InterceptorsNamespaces`** (net11.0 build, 24 intercepted sites):

| Source | Result |
|---|---|
| Set in the csproj (default) | OK |
| Package `build/<Id>.props` appending `$(InterceptorsNamespaces);X` | OK |
| `.props`, plus a csproj that assigns the property without appending | **CS9137** × 48: clobbered |
| Package `build/<Id>.targets` appending (with or without the clobbering csproj) | OK |
| Not set at all | **CS9137** × 48 |

So a package should ship it in `buildTransitive/<Id>.targets`. That is evaluated after the project body,
which is the pattern the SDK itself uses in `Microsoft.NET.Sdk.FrameworkReferenceResolution.targets`.

**Compiler floor:**

- A generator built against Roslyn 5.9.0 fails on SDK 10.0.112 with **CS9057** (its compiler is too old).
- It works on SDK 10.0.401 and 11.0.100-rc.1.
- The TFM does not matter; the SDK's compiler version does.

**Build:**

- 0 warnings, with warnings as errors on both the generator and the consumer.
- Gotcha: a primary-constructor `InterceptsLocationAttribute(int version, string data)` produces
  **CS9113** (unread parameter) even under `<auto-generated/>`. Use a normal constructor.

**Incremental caching** (`S3.CacheCheck`, passes):

- The model is a record of strings and ints only (`Version`, `Data`), and the collected array is wrapped
  in an equatable array.
- Editing an unrelated file gives `Cached`, and the output step is skipped.
- Re-parsing the same text gives `Cached`.
- Appending a comment after the call sites in the same file gives `Modified`: `Data` embeds the file's
  content checksum, so any edit to a file re-emits every site in that file. This is inherent to
  interceptors.
- `InterceptableLocation.Equals` is value-equal across compilations, but storing `Version` + `Data` keeps
  the model free of Roslyn objects.

## Against the plan's Pass column

| Pass criterion | Result |
|---|---|
| Common captures (read-only locals, `this`) lowered to 0 B | **FAIL** through interceptors: 88/64 B on net10, 80/56 B on net11, identical to not intercepting. It is 0 B only as a source rewrite. |
| Unsupported shapes fall back with no behaviour change | PASS: 31 shapes checked, and the four naive rewrites of mutated captures demonstrably change results. |
| Exact boundary documented | PASS (above). |

## Recommendation

1. **Drop call-site interception from the plan.** It adds `InterceptorsNamespaces` plumbing, a compiler
   floor, and per-file cache churn, and it saves 0 B.
2. **PRD §2.1 is wrong** on the `r2` line ("closure → intercepted, 0 B"). The honest statement is:
   - capturing lambdas cost one closure plus one delegate per call (80–104 B);
   - the `static (state, ct)` overload is 0 B;
   - an analyzer offers the rewrite.
3. **Replace it with an analyzer and code fix:** "capturing lambda passed to a pipeline `ExecuteAsync`
   allocates per call". Info or suggestion severity, using the classifier above:
   - the code fix applies the rewrite in place, so accessibility and type parameters are preserved;
   - no diagnostic, or a no-fix variant, for the Fallback shapes;
   - `SiteAnalyzer.cs` is most of the implementation.
