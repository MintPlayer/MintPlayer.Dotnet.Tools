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

One package reference brings three things: the assertion library (`net8.0`, `net9.0`,
`net10.0`), the source generator that makes object-graph comparison reflection-free, and the
analyzers that catch assertions which cannot fail.

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
using MintPlayer.Assertions.Execution;

using (new AssertionScope("the response"))
{
    response.Status.Should().Be(200);
    response.Body.Should().NotBeEmpty();
    response.Headers.Should().ContainKey("ETag");
}   // throws once, listing every failure, each tagged [the response]
```

Scopes nest, and a nested scope folds its failures into its parent. Forgetting to dispose one
would swallow everything it collected, so [MPA0003](#analyzers) warns when you do.

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
| FluentAssertions 7.2.2 | 201.08 µs | 409.14 KB |
| MintPlayer.Assertions | **13.08 µs** | **20.34 KB** |

<sub>BenchmarkDotNet 0.14.0, .NET 10.0.11, X64 RyuJIT AVX-512, Windows 11. Reproduce with
`dotnet run -c Release --project Assertions/MintPlayer.Assertions.Benchmarks -- --filter '*'`.
The benchmark verifies both libraries traverse the entire graph before it will report.</sub>

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
`NotBeSubsetOf` `IntersectWith` `NotIntersectWith` `AllSatisfy` `SatisfyRespectively`
`AllBeOfType<T>` `AllBeAssignableTo<T>`

```csharp
orders.Should().BeInAscendingOrder(o => o.PlacedOn);
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
`ContainValues` `NotContainValue` `Contain` `NotContain`

```csharp
versions.Should().ContainKey("Newtonsoft.Json").Which.Should().Be("13.0.3");
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

On the thrown exception: `WithMessage` (glob, case-insensitive) `WithInnerException<T>`
`WithInnerExactly<T>` `WithParameterName` `Where(predicate)`, plus `Which` for the exception
itself.

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
         .WithInnerException<HttpRequestException, SocketException>();
```

**Every one of these must be awaited.** Skipping the `await` makes the assertion meaningless,
so it is a compile error rather than a green test — see [MPA0001](#analyzers).

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
