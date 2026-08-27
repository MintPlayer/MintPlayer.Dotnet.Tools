# MintPlayer.Assertions analyzers

Roslyn analyzers and code fixes that ship inside the `MintPlayer.Assertions` package
(`analyzers/dotnet/cs`). They catch the assertion mistakes that make a test silently pass, and
offer a one-click migration from FluentAssertions.

There is no analyzer test infrastructure in this repo; the diagnostics are validated live —
the test project references this analyzer project, so MPA0001/MPA0002/MPA0003 run on the real
test code at integration time.

## Diagnostics

### MPA0001 — Assertion is not awaited (Error)

**Triggers when** an invocation of a method declared in a `MintPlayer.Assertions*` namespace that
returns `Task`/`Task<T>` (e.g. `ThrowAsync`, `NotThrowAsync`, `CompleteWithinAsync`) is used as a
bare expression statement — the returned Task is discarded instead of awaited, returned or
assigned. A discarded assertion Task means the test can pass even when the assertion fails.

**Code fix: "Await the assertion"** — prepends `await`. When the containing method, local function
or lambda is not `async`, the fix also adds the `async` modifier (best-effort) and converts a
`void` return type to `System.Threading.Tasks.Task`. Anonymous `delegate` bodies only get the
`await` inserted.

```csharp
// Before
public void Throws()
{
    action.Should().ThrowAsync<InvalidOperationException>();
}

// After
public async System.Threading.Tasks.Task Throws()
{
    await action.Should().ThrowAsync<InvalidOperationException>();
}
```

### MPA0002 — Vacuous Should() (Warning)

**Triggers when** a call to a `Should()` method declared in a `MintPlayer.Assertions*` namespace is
itself the whole expression statement — its result is discarded, so no assertion method is ever
called and nothing is verified.

**No code fix** (only the author knows which assertion was intended).

```csharp
// Reported: does nothing
result.Should();

// Intended
result.Should().Be(42);
```

### MPA0003 — AssertionScope not disposed (Warning)

**Triggers when** a `new MintPlayer.Assertions.AssertionScope(...)` is never disposed.
An undisposed scope swallows every failure collected inside it. Two high-precision shapes are
detected:

- `new AssertionScope();` as a bare expression statement
- `var scope = new AssertionScope();` without `using`, where the local is never referenced again
  (a later reference — `scope.Dispose()`, passing it along — suppresses the diagnostic)

**Code fix** — converts the local declaration to a `using` declaration; the bare statement becomes
`using var scope = new AssertionScope(...);`.

```csharp
// Before
var scope = new AssertionScope("the response");

// After
using var scope = new AssertionScope("the response");
```

### MPA0100 — FluentAssertions migration available (Info)

**Triggers on** a plain `using FluentAssertions;` (or `using FluentAssertions.*;`) directive.
Alias and `using static` directives are skipped. Purely syntax-driven by design — the
FluentAssertions package is typically not referenced when migrating, so no semantic verification
of FluentAssertions symbols is attempted.

**Code fix: "Migrate file to MintPlayer.Assertions"** (Fix All capable, document-wide):

1. Replaces every FluentAssertions using with `using MintPlayer.Assertions;`, deduplicated
   against existing usings. No second using is added: `AssertionScope` — the reason
   `FluentAssertions.Execution` was usually imported — lives in the root namespace here.
2. Renames the known-renamed calls (table in
   `FluentAssertionsMigrationCodeFixProvider.RenameTable`, extend it there):

   | FluentAssertions | MintPlayer.Assertions |
   |---|---|
   | `HaveCountGreaterOrEqualTo(` | `HaveCountGreaterThanOrEqualTo(` |
   | `BeGreaterOrEqualTo(` | `BeGreaterThanOrEqualTo(` |
   | `BeLessOrEqualTo(` | `BeLessThanOrEqualTo(` |
   | `WithInnerExceptionExactly<` | `WithInnerExactly<` |

   `Invoking`/`Awaiting` are shape-compatible and stay as-is; `NotThrowAfter(` has no direct
   equivalent and is deliberately left untouched (it will surface as a compile error to resolve
   by hand).

```csharp
// Before
using FluentAssertions;
using FluentAssertions.Execution;
...
list.Should().HaveCountGreaterOrEqualTo(3);
value.Should().BeGreaterOrEqualTo(1);

// After
using MintPlayer.Assertions;
...
list.Should().HaveCountGreaterThanOrEqualTo(3);
value.Should().BeGreaterThanOrEqualTo(1);
```

## Release tracking

`AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md` follow the
[Roslyn release-tracking format](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md).
All four rules are currently unshipped; move them to Shipped.md when the first package version
ships.
