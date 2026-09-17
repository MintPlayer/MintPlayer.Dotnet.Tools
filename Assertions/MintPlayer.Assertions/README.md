# MintPlayer.Assertions

A modern fluent assertion library for .NET — AOT-safe, analyzer-backed, and free forever.

> **License pledge:** MintPlayer.Assertions is Apache-2.0 and **will never change license.**
> Not to a commercial license, not to a source-available license — never.

```csharp
using MintPlayer.Assertions;

order.Total.Should().Be(120m, because: "the order was paid");
```

```
Expected order.Total to be 120M because the order was paid, but found 90M.
```

Works with xUnit, NUnit, MSTest and TUnit — the library throws its own
`AssertionFailedException`, which every runner renders as a failure. There is no framework
detection and no adapter package to install.

---

## Install

```shell
dotnet add package MintPlayer.Assertions
```

One package reference brings three things: the assertion library (`net10.0`, `net11.0`), the source
generator that makes object-graph comparison reflection-free, and the analyzers that catch
assertions which cannot fail.

> **Upgrading from 1.x?** This version drops `net8.0` and `net9.0`. NuGet does not roll *forward*
> across target frameworks, so a `net8.0` or `net9.0` test project will report `NU1202` rather than
> silently resolving something older — stay on `1.1.0` until you move the test project up.

And one using covers everything you write in a test:

```csharp
using MintPlayer.Assertions;
```

That includes `AssertionScope`, `Monitor()`, `BeEquivalentTo` and the async assertions — no
second namespace, and no `ConfigureAwait` or other ceremony at the call site. (Authoring a
*custom* assertion additionally needs `MintPlayer.Assertions.Execution` and
`MintPlayer.Assertions.Primitives`; see [Extending](#extending).) A test that imports nothing
else is part of the test suite, so this stays true.

---

## Why another assertion library

FluentAssertions v8 became a commercial product in January 2025. The forks that followed kept
its original architecture; this library is a rewrite around four ideas.

**AOT- and trimming-safe.** `BeEquivalentTo` compares object graphs using **source-generated
member accessors**, not a runtime reflection walker. A generator finds the types you compare,
emits typed getters for them, and registers them at module load. Reflection remains only as an
annotated fallback for types the generator cannot name, so behaviour never changes — only
speed, and whether the code survives trimming.

**Assertions that cannot silently pass.** The classic failure mode of async assertions is a
forgotten `await`: the test goes green while asserting nothing. Here that is a **compile
error** ([MPA0001](#analyzers)), not a lucky catch in review.

**Your expression in the message.** `[CallerArgumentExpression]` captures the subject's source
text at zero runtime cost — no PDB reading, no expression trees.

**A stable extension surface.** Custom assertions are one method chain, or one attribute. That
surface will not break underneath you.

### How it compares

| | License | Object-graph equivalency | AOT / trimming | Analyzer safety net |
|---|---|---|---|---|
| **MintPlayer.Assertions** | Apache-2.0, pledged permanent | Rich options, source-generated | **Clean** | **In the box**, un-awaited = error |
| FluentAssertions 8+ | Commercial (Xceed) | Rich options, reflection | Reflection-heavy | Separate package |
| FluentAssertions 7 | Apache-2.0, maintenance-only | Rich options, reflection | Reflection-heavy | Separate package |
| AwesomeAssertions | Apache-2.0 | Rich options, reflection (FA 7 fork) | Reflection-heavy | Separate package |
| Shouldly | MIT | Basic | Reads call site from source/PDB | — |
| TUnit.Assertions | MIT | Basic-to-moderate | Designed for AOT | Yes (TUnit's own) |
| xUnit `Assert.Equivalent` | Apache-2.0 | Shallow, no options | Fine | — |

The short version: several libraries give you a free licence, and a couple give you AOT
support, but the combination of **FluentAssertions-grade equivalency options, no reflection on
that path, and analyzers that turn a silently-passing assertion into a build error** is what
this library exists for.

<sub>Comparison reflects these libraries as of 2026; all of them are actively developed, so check
before treating any row as current.</sub>

### When another library is the better choice

Honest guidance, because the wrong tool wastes more time than a missing feature:

- **You have a large existing FluentAssertions codebase and want the smallest possible diff.**
  Use [AwesomeAssertions](https://awesomeassertions.org/) — it is a fork of FA 7 that keeps the
  original namespaces and type names, so migration is a package swap. This library changes the
  namespace and a handful of method names (there's a [code fix](#migrating-from-fluentassertions),
  but it is still a real diff).
- **You already use TUnit.** Its assertions are integrated with its runner and worth staying with.
- **You prefer terse over fluent.** Shouldly's `value.ShouldBe(expected)` is a smaller surface to
  learn, if you don't need deep object-graph comparison.
- **You are comparing big object graphs and mostly want to eyeball the diff.** Snapshot testing
  with [Verify](https://github.com/VerifyTests/Verify) suits that better than any assertion
  library, this one included; the two compose fine.
- **You need battle-tested maturity today.** This library is new. FluentAssertions and Shouldly
  have years of accumulated edge cases behind them; here, a bug you hit may be one nobody has hit
  yet. (Everything documented on this page is covered by tests, including the samples above — but
  that is not the same as years in the field.)

---

## The basics

Every assertion accepts an optional `because` reason, which is woven into the failure message:

```csharp
count.Should().Be(3, because: "the importer skips duplicates");
// Expected count to be 3 because the importer skips duplicates, but found 4.
```

Assertions chain with `.And`, and drill into a value with `.Which`:

```csharp
items.Should().HaveCount(3)
     .And.ContainSingle(i => i.IsPrimary)
     .Which.Name.Should().Be("main");
```

### Soft assertions

Inside an `AssertionScope`, failures are collected instead of throwing at the first one, then
reported together. Without it you fix one failure only to discover the next.

```csharp
using (new AssertionScope("the response"))
{
    response.Status.Should().Be(200);
    response.Body.Should().NotBeEmpty();
    response.Headers.Should().ContainKey("ETag");
}   // throws once, listing every failure, each tagged [the response]
```

Scopes nest, and a nested scope folds its failures into its parent. Forgetting to dispose one
would swallow everything it collected, so [MPA0003](#analyzers) warns when you do.

A scope can also be inspected and steered:

```csharp
using var scope = new AssertionScope();

scope.AddReportable("correlationId", () => request.CorrelationId);  // shown only if something fails

response.Status.Should().Be(200);

if (scope.HasFailures) { /* scope.Failures is a snapshot of what has been collected */ }

scope.AddPreFormattedFailure(renderedDiff);   // your own message, verbatim, no substitution
scope.Discard();                              // take the failures; disposing now throws nothing
```

`AddReportable` takes a `Func<string>` on purpose: the value is built only when a failure is actually
reported, so attaching context to a scope that passes costs nothing.

---

## What you can assert

### Objects

`Be` `NotBe` `BeNull` `NotBeNull` `BeSameAs` `NotBeSameAs` `BeOfType<T>` `NotBeOfType<T>`
`BeAssignableTo<T>` `NotBeAssignableTo<T>` `Match(predicate)`

### Object graphs — `BeEquivalentTo`

Compares structure rather than references, member by member, recursively.

```csharp
actual.Should().BeEquivalentTo(expected);

// Compare against a shape you declare inline — anonymous types work as expectations:
dto.Should().BeEquivalentTo(new { Id = 1, Name = "Ada" });
```

Every difference is reported, not just the first:

```
Expected dto to be equivalent to { Id = 2, Name = "Ada" }, but found the following difference(s):
  - Id: expected 2, but found 1
```

Options (`NotBeEquivalentTo` takes the same):

| Option | Effect |
|---|---|
| `Excluding(x => x.Id)` | Skip a member, by expression — refactor-safe, no magic strings |
| `ExcludingNested<T>(x => x.CreatedOn)` | Skip a member on every `T` in the graph, including inside collections |
| `ExcludingPath("Items[0].Name")` | Skip by path, wildcards allowed |
| `Including(x => x.Name)` | Compare only the listed members |
| `Using<T>((actual, expected) => …)` | Custom comparison for members of type `T` |
| `WithStrictOrdering()` | Compare collections positionally (default matches unordered) |
| `ComparingByValue<T>()` / `ComparingByMembers<T>()` | Force `Equals` or member-wise comparison for a type |
| `WithMaxDepth(n)` / `AllowingInfiniteRecursion()` | Bound or unbound recursion (default depth 10) |
| `RespectingRuntimeTypes()` | Resolve members from runtime types instead of declared ones |
| `AllowingVacuousComparison()` | Permit a comparison that compares no members at all (see below) |

`Excluding` and `Including` take a path relative to the **comparison root**. On a collection
subject the root is the collection, so an element's member lives at `[0].Name` — reach it with
`ExcludingNested<T>` or a wildcard path, not `Excluding(x => x.Name)`.

#### Assertions that cannot fail are refused

The comparison is driven by the *expectation's* members, and extra subject members are ignored —
that is what makes comparing a DTO against an anonymous object work. The degenerate consequence is
that an expectation contributing **no** members yields no differences, so `BeEquivalentTo` would
pass for any two values and `NotBeEquivalentTo` would fail for any two values. Silently.

That is refused with an `InvalidOperationException` naming both types and the cause:

```
No members were compared, so this assertion can never fail: the expectation type 'Object'
exposes no public properties or fields, while the subject 'Invoice' has 2. Compare against a
concrete type, or against an anonymous object listing the members you care about — or call
AllowingVacuousComparison() if comparing nothing is intended.
```

It is thrown rather than reported as a failure because a failure is exactly what would make
`NotBeEquivalentTo` *succeed* — hiding the mistake where it is easiest to make.

An expectation type with **no comparable members** is refused wherever it appears, including on a
collection element or a nested member, since it is never a way to express intent. Options that
**removed every member** are refused only at the root, where nothing is left to assert — deeper
down, excluding every member of a subtree is the normal way to skip it, exactly as
`ExcludingNested<AuditInfo>(a => a.ModifiedOn)` does above. Two values that both expose no members
are genuinely equivalent and still compare equal; so do two empty collections. Use
`AllowingVacuousComparison()` for a generic harness where some instantiations legitimately have
nothing to compare.

Casting to `object` does **not** disable the comparison — members are resolved from the runtime
type. It does cost the generated accessors and make the options lambda untypeable, which is what
[MPA0004](#analyzers) points out.

```csharp
actual.Should().BeEquivalentTo(expected, opt => opt
    .Excluding(x => x.Id)
    .ExcludingNested((AuditInfo a) => a.ModifiedOn)
    .Using<DateTime>((a, e) => a.Should().BeCloseTo(e, TimeSpan.FromSeconds(1)))
    .WithStrictOrdering());
```

Cycles are handled — a self-referencing graph compares without hanging.

Because the comparison runs on generated accessors instead of reflection, it is also
substantially cheaper. On a 4-level graph of 5 types containing a 20-item collection:

| | Mean | Allocated |
|---|---:|---:|
| FluentAssertions 7.2.2 | 167.82 µs | 397.04 KB |
| MintPlayer.Assertions | **9.58 µs** | **6.59 KB** |

<sub>BenchmarkDotNet 0.14.0, .NET 11.0.0, X64 RyuJIT AVX-512, Windows 11. Reproduce with
`dotnet run -c Release --project Assertions/MintPlayer.Assertions.Benchmarks -- --filter '*'`.
The benchmark verifies both libraries traverse the entire graph, and that the generated accessors
are actually active, before it will report — otherwise it would happily measure the reflection
fallback and call it a result.</sub>

That is **17.5× faster and 60× less memory**. Treat the allocation figures as exact and the timings
as approximate: this library's bytes are a property of the emitted IL and reproduce to the hundredth
of a KB in every run, while wall-clock does not — across four runs on a nominally idle machine this
library measured 9.29–11.04 µs and FluentAssertions measured 150.55–216.45 µs, a 44% spread on
identical code. The row above is one run quoted whole rather than a best figure assembled from
several. That asymmetry is why the regression gates in this repo assert bytes and operation counts
rather than milliseconds.

### Strings

`Be` `NotBe` `BeEquivalentTo` (ignores case) `NotBeEquivalentTo` `BeEmpty` `NotBeEmpty`
`BeNullOrEmpty` `NotBeNullOrEmpty` `BeNullOrWhiteSpace` `NotBeNullOrWhiteSpace` `HaveLength`
`StartWith` `NotStartWith` `StartWithEquivalentOf` `EndWith` `NotEndWith` `EndWithEquivalentOf`
`Contain` `NotContain` `ContainEquivalentOf` `NotContainEquivalentOf` `ContainAll` `ContainAny`
`Match` (wildcards) `NotMatch` `MatchEquivalentOf` `MatchRegex` `NotMatchRegex` `BeUpperCased`
`BeLowerCased`

A failed `Be` points at the first difference instead of leaving you to diff two long strings by
eye:

```
Expected name to be "hello world", but they differ at index 6: "hello w…" vs "hello W…".
```

`Match` uses glob wildcards — `*` for any run of characters, `?` for exactly one.

### Numbers

One implementation over `INumber<T>` covers every numeric type, including `Half`, `Int128` and
`BigInteger`:

`Be` `NotBe` `BePositive` `BeNegative` `BeGreaterThan` `BeGreaterThanOrEqualTo` `BeLessThan`
`BeLessThanOrEqualTo` `BeInRange` `NotBeInRange` `BeOneOf` `BeCloseTo` `NotBeCloseTo`
`HaveValue` `NotHaveValue`

```csharp
temperature.Should().BeCloseTo(21.5, 0.1);
ratio.Should().BeInRange(0, 1);
```

### Booleans, Guids, enums, comparables

- **Boolean** — `BeTrue` `BeFalse` `Be` `NotBe` `HaveValue` `NotHaveValue`
- **Guid** — `Be` (also accepts a string) `NotBe` `BeEmpty` `NotBeEmpty` `HaveValue` `NotHaveValue`
- **Enum** — `Be` `NotBe` `HaveFlag` `NotHaveFlag` `BeDefined` `BeOneOf` `HaveValue` `NotHaveValue`
- **`IComparable<T>`** — `Be` `NotBe` `BeLessThan` `BeLessThanOrEqualTo` `BeGreaterThan` `BeGreaterThanOrEqualTo` `BeInRange`

### Dates and times

- **`DateTime`** — `Be` `NotBe` `BeCloseTo` `NotBeCloseTo` `BeBefore` `BeOnOrBefore` `BeAfter` `BeOnOrAfter` `BeSameDateAs` `BeIn(DateTimeKind)` `HaveYear` `HaveMonth` `HaveDay` `HaveHour` `HaveMinute` `HaveSecond` `BeOneOf`
- **`DateTimeOffset`** — the same, plus `HaveOffset`
- **`DateOnly`** — `Be` `NotBe` `BeBefore` `BeOnOrBefore` `BeAfter` `BeOnOrAfter` `HaveYear` `HaveMonth` `HaveDay` `BeOneOf`
- **`TimeOnly`** — `Be` `NotBe` `BeCloseTo` (wraps around midnight) `BeBefore` `BeOnOrBefore` `BeAfter` `BeOnOrAfter` `HaveHours` `HaveMinutes` `HaveSeconds` `HaveMilliseconds`
- **`TimeSpan`** — `Be` `NotBe` `BePositive` `BeNegative` `BeCloseTo` `BeLessThan` `BeLessThanOrEqualTo` `BeGreaterThan` `BeGreaterThanOrEqualTo`

### Collections

`BeEmpty` `NotBeEmpty` `BeNullOrEmpty` `NotBeNullOrEmpty` `HaveCount` (value or predicate)
`HaveCountGreaterThan` `HaveCountGreaterThanOrEqualTo` `HaveCountLessThan`
`HaveCountLessThanOrEqualTo` `HaveSameCountAs` `NotHaveSameCountAs` `ContainSingle` `Contain`
`NotContain` `ContainInOrder` `OnlyContain` `OnlyHaveUniqueItems` `NotContainNulls` `Equal`
`NotEqual` `StartWith` `EndWith` `BeInAscendingOrder` `BeInDescendingOrder` `BeSubsetOf`
`NotBeSubsetOf` `BeSupersetOf` `NotBeSupersetOf` `BeProperSubsetOf` `BeProperSupersetOf`
`IntersectWith` `NotIntersectWith` `HaveElementAt` `HaveElementPreceding` `HaveElementSucceeding`
`ContainInConsecutiveOrder` `NotContainInConsecutiveOrder` `BeOrderedBy` `BeOrderedByDescending`
`AllSatisfy` `SatisfyRespectively` `AllBeOfType<T>` `AllBeAssignableTo<T>` `BeEquivalentTo`
`NotBeEquivalentTo`

```csharp
orders.Should().BeInAscendingOrder(o => o.PlacedOn);

// Sort by more than one key — ties broken left to right:
people.Should().BeOrderedBy(p => p.Team).ThenBeInAscendingOrder(p => p.Name);

// "In order" allows gaps; "in consecutive order" does not:
new[] { 1, 9, 2 }.Should().ContainInOrder(1, 2);
new[] { 1, 9, 2 }.Should().NotContainInConsecutiveOrder([1, 2]);
orders.Should().AllSatisfy(o => o.Total.Should().BePositive());
orders.Should().SatisfyRespectively(
    first  => first.Id.Should().Be(1),
    second => second.Id.Should().Be(2));
```

`AllSatisfy` and `SatisfyRespectively` report *every* offending item with its index, not just
the first. `Equal` compares order-sensitively and names the first differing index; use
`BeEquivalentTo` for order-insensitive structural comparison.

The subject is enumerated exactly once per assertion, so lazy sequences and one-shot iterators
are safe.

### Dictionaries

`BeEmpty` `NotBeEmpty` `HaveCount` `ContainKey` `ContainKeys` `NotContainKey` `ContainValue`
`ContainValues` `NotContainValue` `Contain` `NotContain` `BeEquivalentTo` `NotBeEquivalentTo`

```csharp
versions.Should().ContainKey("Newtonsoft.Json").Which.Should().Be("13.0.3");
```

`BeEquivalentTo` compares dictionaries structurally: every expected key present with an
equivalent value, and no unexpected keys. Values go through the full object-graph comparison, so
the values can be anonymous objects listing only the members you care about.

```csharp
lanes.Should().BeEquivalentTo(new Dictionary<string, object>
{
    ["left"] = new { Width = 3 },
});
```

Key lookups honour the dictionary's **own** comparer, so a
`Dictionary<string, T>(StringComparer.OrdinalIgnoreCase)` matches keys case-insensitively here
exactly as it does everywhere else in your code.

### Spans

`Span<T>` and `ReadOnlySpan<T>` support `Be` `Equal` `HaveLength` `BeEmpty` `NotBeEmpty`
`Contain` `StartWith` `EndWith`. The span is only materialised when an assertion fails.

Arrays get the full collection surface, not the span one.

### Exceptions

```csharp
var act = () => service.Process(null!);

act.Should().Throw<ArgumentNullException>().WithParameterName("order");
act.Should().ThrowExactly<ArgumentException>().WithMessage("*must not be empty*");
act.Should().NotThrow();
```

On the thrown exception: `WithMessage` `WithInnerException<T>` `WithInnerExactly<T>`
`WithParameterName` `Where(predicate)`, plus `Which` for the exception itself.

`WithMessage` matches a glob **case-sensitively**. When case should not matter, say so at the
call site rather than relying on a hidden default:

```csharp
act.Should().Throw<SomeException>()
   .WithMessage("*not found*", StringComparison.OrdinalIgnoreCase);
```

`Invoking` and `Awaiting` wrap a subject so the call stays inline:

```csharp
sut.Invoking(s => s.Process(null!)).Should().Throw<ArgumentNullException>();
await sut.Awaiting(s => s.LoadAsync()).Should().ThrowAsync<TimeoutException>();
```

A `Func<T>` that must not throw hands back its result:

```csharp
var parsed = parse.Should().NotThrow().Which;
```

### Async

```csharp
await act.Should().ThrowAsync<TimeoutException>().WithMessage("*timed out*");
await act.Should().ThrowExactlyAsync<HttpRequestException>();
await act.Should().NotThrowAsync();
await act.Should().NotThrowAfterAsync(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100));
await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(1));
```

Assertions on the thrown exception chain directly onto the awaited call — no parenthesising the
`await`:

```csharp
await act.Should().ThrowAsync<HttpRequestException>()
         .WithInnerException<SocketException>();
```

Only the genuinely new type is named — the outer exception type is already known from
`ThrowAsync<T>()`, so the async chain reads exactly like the synchronous one.

**Every one of these must be awaited.** Skipping the `await` makes the assertion meaningless,
so it is a compile error rather than a green test — see [MPA0001](#analyzers).

Await them; do not block on them with `.Result` or `.GetAwaiter().GetResult()`. Every `await`
inside the library uses `ConfigureAwait(false)`, so the library itself never deadlocks — that is
covered by tests that reproduce the classic single-threaded `SynchronizationContext` scenario.
But if the *code under test* awaits without `ConfigureAwait(false)`, its own continuation is
queued back to the thread you just blocked, and nothing can complete. That is the ordinary
"sync over async" deadlock, and awaiting the assertion avoids it entirely. `CompleteWithinAsync`
is bounded by its own timeout and so fails rather than hangs.

### Execution time

```csharp
act.Should().ExecutionTime().BeLessThan(TimeSpan.FromMilliseconds(500));
```

Also `BeLessThanOrEqualTo` `BeGreaterThan` `BeGreaterThanOrEqualTo` `BeCloseTo`. The action runs
once per assertion, and the measured duration appears in the failure message.

### Events

```csharp
using var monitor = subject.Monitor();

subject.Rename("new name");

monitor.Raise(nameof(Subject.Renamed))
       .WithSender(subject)
       .WithArgs<RenamedEventArgs>(e => e.NewName == "new name");

monitor.NotRaise(nameof(Subject.Deleted));
```

For `INotifyPropertyChanged`:

```csharp
monitor.RaisePropertyChangeFor(x => x.Name);
monitor.NotRaisePropertyChangeFor(x => x.Id);
```

The monitor subscribes to every public event whose handler is a void delegate taking
`(sender, args)` or no parameters; anything else is listed in `UnmonitoredEvents` rather than
silently ignored. `OccurredEvents` exposes the raw recordings, and `Clear()` resets them.
Asserting on an event name that does not exist throws rather than passing vacuously.

Because binding handlers needs runtime type work, `Monitor()` is annotated
`[RequiresDynamicCode]`; events with reference-type argument types work under Native AOT.

### JSON

Works on `JsonElement`, `JsonNode` and `JsonDocument`:

```csharp
document.Should().BeJsonEquivalentTo("""{ "id": 1, "tags": ["a", "b"] }""");
element.Should().HaveProperty("id").Which.Should().HaveNumberValue(1);
```

Also `NotBeJsonEquivalentTo` `NotHaveProperty` `BeJsonObject` `BeJsonArray` `BeJsonString`
`BeJsonNumber` `BeJsonBoolean` `BeJsonNull` `HaveStringValue` `HaveBooleanValue`
`HaveArrayLength`.

Comparison is property-order-insensitive, order-sensitive for arrays, and numeric-aware
(`1.0` equals `1.00`). Differences are reported with JSON paths:

```
$.tags[1]: expected "b", but found "c"
$.name: property is missing
```

### Types

`Be<T>` `NotBe<T>` `BeAssignableTo<T>` `BeDerivedFrom<T>` `Implement<TInterface>`
`BeDecoratedWith<TAttribute>` (optionally with a predicate; returns the attribute via `Which`)
`NotBeDecoratedWith<TAttribute>` `BeAbstract` `BeSealed` `BeStatic` `BeAnInterface` `BeAClass`

---

## Analyzers

Shipped in the package; no extra reference, no configuration.

| ID | Severity | What it catches |
|---|---|---|
| **MPA0001** | **Error** | An async assertion whose `Task` is discarded. The test would pass no matter what the assertion found. Code fix adds `await` (and makes the method `async`). |
| **MPA0002** | Warning | `Should()` with no assertion called on it — it does nothing. |
| **MPA0003** | Warning | An `AssertionScope` that is never disposed, which silently swallows every failure it collected. Code fix converts it to a `using` declaration. |
| **MPA0004** | Info | An equivalency expectation cast to `object`. The comparison is still correct, but it falls back to reflection instead of the generated accessors and cannot be configured with `Excluding`/`Including`. |
| **MPA0100** | Info | A file still using FluentAssertions, with a code fix that migrates it. |
| **MPAG001** | Warning | A `[GenerateAssertion]` method whose shape is unsupported, so no assertion was generated. |

Adjust any of them through `.editorconfig` as usual:

```ini
dotnet_diagnostic.MPA0100.severity = none
```

---

## Migrating from FluentAssertions

For most call shapes the syntax is unchanged; swap the using directive:

```diff
-using FluentAssertions;
+using MintPlayer.Assertions;
```

MPA0100 offers a code fix that does this across a file, including the handful of renames:

| FluentAssertions | MintPlayer.Assertions |
|---|---|
| `HaveCountGreaterOrEqualTo` | `HaveCountGreaterThanOrEqualTo` |
| `BeGreaterOrEqualTo` | `BeGreaterThanOrEqualTo` |
| `BeLessOrEqualTo` | `BeLessThanOrEqualTo` |
| `WithInnerExceptionExactly<T>` | `WithInnerExactly<T>` |

`AssertionScope` moves to `MintPlayer.Assertions.Execution`. The fixer is deliberately
syntax-driven, so it still works after you have removed the FluentAssertions package.

Worth knowing while migrating: `Assert.Equal(expected, actual)` reverses into
`actual.Should().Be(expected)`. Getting the order wrong does not change whether a test passes,
but it does swap the words "expected" and "found" in the failure message.

---

## Extending

### A custom assertion by hand

```csharp
using MintPlayer.Assertions;
using MintPlayer.Assertions.Execution;
using MintPlayer.Assertions.Primitives;

public static class OrderAssertionExtensions
{
    public static AndConstraint<ObjectAssertions> BeSettled(
        this ObjectAssertions assertions,
        string? because = null, params object?[] becauseArgs)
    {
        var order = assertions.Subject as Order;

        assertions.Assert()
            .ForCondition(order is { IsSettled: true })
            .BecauseOf(because, becauseArgs)
            .FailWith("Expected {subject} to be settled{reason}, but found {0}.", order?.Status);

        return new(assertions);
    }
}
```

`{subject}` is the caller's expression, `{reason}` the `because` clause, and `{0}`, `{1}`, … are
rendered through the library's formatter. This is the stable surface — it will not break across
versions.

### A custom assertion by attribute

Mark a static predicate and the generator writes the assertion for you:

```csharp
[GenerateAssertion]
public static bool IsEven(int value) => value % 2 == 0;

// generated:
number.Should().BeEven();
```

The name is derived from the predicate (`Is…` → `Be…`, `Has…` → `Have…`), or set it yourself
with `[GenerateAssertion(Name = "BeDivisibleByTwo")]`. Extra parameters become parameters of the
generated assertion.

---

## Failure messages

Everything here runs only after an assertion has failed, so none of it costs a passing test.

### Rendering your own types

```csharp
Formatter.Register<Money>(m => $"{m.Amount:0.00} {m.Currency}");
// Expected total to be 120.00 EUR, but found 90.00 EUR.
```

Registration is **explicit**. Nothing scans your assemblies looking for formatter types — that is the
pattern that breaks under trimming and AOT, which is the property this library is built around.
Matching is by exact type first, then up the base-class chain; interfaces are not matched, because a
value implementing two registered interfaces would have no predictable winner.

### How much detail a message carries

```csharp
FormattingOptions.MaxDepth = 5;          // process-wide, set once at start-up
FormattingOptions.UseLineBreaks = true;
```

`MaxDepth`, `MaxCollectionItems`, `MaxStringLength`, `UseLineBreaks` and `MaxLines`. When a message
is cut short it says which knob did it, so you can act on it instead of reaching for a debugger:

```
Expected order to be equivalent to Order {… depth 3 reached; raise FormattingOptions.MaxDepth to see more}
```

⚠️ Those properties are **process-wide**. Setting one from inside a test changes the failure messages
of every test running beside it, which is a flaky-test generator. For anything other than start-up
configuration use the thread-local form:

```csharp
using (FormattingOptions.With(maxDepth: 10, useLineBreaks: true))
{
    deepGraph.Should().BeEquivalentTo(expected);
}
```

(It is thread-local, so it does not survive an `await` — keep the block synchronous around the
assertion.)

### Why did that comparison do all that work?

```csharp
actual.Should().BeEquivalentTo(expected, o => o.WithDiagnostics());
// ...
// Walk: 133 node(s), 112 member lookup(s), 20 collection match probe(s).
```

For the two questions a list of differences does not answer: *did it even look at the member I think
it did*, and *why is this slow*. A node count far larger than the graph means the walk is revisiting;
a probe count near n² on an ordered collection means the matcher is not taking its fast path.

---

## AOT and trimming

The library is `IsAotCompatible` and builds without trim warnings.

`BeEquivalentTo` prefers generated accessors, which the generator emits for every type it sees
in a `BeEquivalentTo` call. If a type is only ever compared through a base type or from another
assembly, opt it in explicitly:

```csharp
[AssertEquivalency]
public class Order { … }
```

Types the generator cannot name from generated code — `file`-local types, private nested types,
anonymous types — fall back to reflection. Results are identical; only trimming-safety and speed
differ. Event monitoring is the one feature that inherently needs runtime type work, and says so
through `[RequiresDynamicCode]`.

---

## Notes

- `Be` uses `Equals`; `BeSameAs` compares references; `BeEquivalentTo` compares structure.
- Negative assertions (`NotContain`, `NotBeCloseTo`, …) treat a null subject as passing; positive
  ones require a value.
- `BeOneOf` and other `params` overloads have a sibling taking `IEnumerable<T>` when you also
  need `because`, since C# allows only one trailing `params`.

Part of [MintPlayer.Dotnet.Tools](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools).
Licensed under Apache-2.0 — permanently.
