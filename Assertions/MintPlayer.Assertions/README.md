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
**Choosing members**

| Option | Effect |
|---|---|
| `Excluding(x => x.Id)` | Skip a member, by expression — refactor-safe, no magic strings |
| `ExcludingNested<T>(x => x.CreatedOn)` | Skip a member on every `T` in the graph, including inside collections |
| `ExcludingPath("Items[0].Name")` | Skip by path, wildcards allowed |
| `ExcludingMembersNamed("ETag")` | Skip a name wherever it appears, whatever declares it |
| `Excluding<DateTime>()` / `Excluding(type)` | Skip every member of that declared type |
| `Including(x => x.Home.City)` | Compare only the listed paths — nested paths included |
| `IncludingPath("Items[*].Id")` | Compare only the paths matching a wildcard |
| `WithMapping("FullName", "Name")` | Match an expectation member onto a differently named subject member |
| `ExcludingMissingMembers()` / `ThrowingOnMissingMembers()` | Pass or fail when the subject lacks a member (default: fail) |

**Which kinds of member**

| Option | Effect |
|---|---|
| `IncludingFields()` / `ExcludingFields()` | Fields take part (default: yes) |
| `IncludingProperties()` / `ExcludingProperties()` | Properties take part (default: yes) |
| `IncludingInternalMembers()` | `internal` members take part (default: no) |
| `IncludingNonBrowsableMembers()` / `ExcludingNonBrowsableMembers()` | `[EditorBrowsable(Never)]` members take part (default: **yes** — see below) |
| `IncludingExplicitInterfaceMembers()` | Explicitly implemented interface members take part (default: no) |

**How values compare**

| Option | Effect |
|---|---|
| `Using<T>((actual, expected) => …)` | Custom comparison for members of type `T` |
| `Using<T>(IEqualityComparer<T>)` | The same, with a comparer you already have |
| `ComparingByValue<T>()` / `ComparingByMembers<T>()` | Force `Equals` or member-wise comparison for a type |
| `ComparingEnumsByName()` / `ComparingEnumsByValue()` | Match two enum types by member name (default: by value) |
| `ComparingRecordsByValue()` / `ComparingRecordsByMembers()` | Use a record's generated equality (default: member-wise) |
| `ComparingStringsWith(StringMatchOptions…)` | Casing, whitespace and newline handling for every string in the graph |
| `TreatingNullAsEmptyString()` | A null string equals an empty one |
| `WithStrictOrdering()` | Compare collections positionally (default matches unordered) |
| `WithStrictOrderingFor(path)` / `WithoutStrictOrderingFor(path)` | Ordering for one wildcard path, against the default |

**Depth, recursion and types**

| Option | Effect |
|---|---|
| `WithMaxDepth(n)` / `AllowingInfiniteRecursion()` | Bound or unbound recursion (default depth 10) |
| `ThrowingOnCyclicReferences()` / `IgnoringCyclicReferences()` | Report a cycle as a difference, or treat it as equal (default) |
| `RespectingRuntimeTypes()` | Resolve members from runtime types instead of declared ones |
| `AllowingVacuousComparison()` | Permit a comparison that compares no members at all (see below) |

`Excluding` and `Including` take a path relative to the **comparison root**. On a collection
subject the root is the collection, so an element's member lives at `[0].Name` — reach it with
`ExcludingNested<T>` or a wildcard path, not `Excluding(x => x.Name)`.

**Exceeding the depth limit is a failure, not a pass.** A graph deeper than `MaxDepth` reports that
the walk stopped and names the two options that let it continue. Treating what lies below the cut as
equal — which is what it used to do — means a difference at the bottom of a deep graph never
surfaces, and nothing says so.

**`[EditorBrowsable(Never)]` members are compared by default**, which is a deliberate divergence
from FluentAssertions. Hiding a member from IntelliSense is a statement about tooling, not about
correctness, and an assertion library that silently compares *less* than you wrote is the failure
mode this one takes most seriously. `ExcludingNonBrowsableMembers()` gives you FA's behaviour.

**`private` and `protected` members are never compared**, even with `IncludingInternalMembers()`.
The source generator cannot emit an accessor for one, so including them would make the same option
mean two different things depending on whether a type happened to be scanned.

#### What `BeEquivalentTo` deliberately will not do

These are the boundary of a generator-first design, not a to-do list:

- **No plug-in comparison steps.** There is no `IEquivalencyStep` or `IMemberSelectionRule`. The
  walk is a closed, inlineable loop over generated accessors; an extension point in the middle of it
  is a virtual call per node on the passing path, which is precisely the cost this library exists to
  avoid. `Using<T>(…)` covers the case that actually comes up.
- **Runtime types the compilation never sees.** The generated accessors are emitted for the types
  the compiler could find at the call sites. A type loaded by reflection at run time falls back to
  the reflection provider — correct, just not fast.
- **Anonymous types as *subjects*, and open generics.** Both work as expectations; neither can have
  accessors generated for it.

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

`Be` `NotBe` `StartWith` `NotStartWith` `EndWith` `NotEndWith` `Contain` and `NotContain` also take
`StringMatchOptions`, for the comparisons the `EquivalentOf` variants do not cover:

```csharp
generated.Should().Be(expected,
    StringMatchOptions.IgnoringCase | StringMatchOptions.IgnoringNewlineStyle);
```

Flags: `IgnoringCase`, `IgnoringLeadingWhitespace`, `IgnoringTrailingWhitespace`,
`IgnoringSurroundingWhitespace`, `IgnoringAllWhitespace`, `IgnoringNewlineStyle`. It is a flags enum
rather than an options-building lambda so it stays a compile-time constant at the call site;
`IgnoringCase` is free, the rest rewrite both sides and allocate. The failure message names the
options, because a comparison that fails while whitespace is supposedly being ignored is otherwise
baffling to read.

### Numbers

One implementation over `INumber<T>` covers every numeric type, including `Half`, `Int128` and
`BigInteger`:

`Be` `NotBe` `BePositive` `BeNegative` `BeGreaterThan` `BeGreaterThanOrEqualTo` `BeLessThan`
`BeLessThanOrEqualTo` `BeInRange` `NotBeInRange` `BeOneOf` `BeApproximately` `NotBeApproximately` `BeNull` `NotBeNull` `Match`
`HaveValue` `NotHaveValue`

```csharp
temperature.Should().BeApproximately(21.5, 0.1);
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
`HaveCountLessThanOrEqualTo` `HaveSameCount` `NotHaveSameCount` `ContainSingle` `Contain`
`NotContain` `ContainInOrder` `OnlyContain` `OnlyHaveUniqueItems` `NotContainNulls` `Equal`
`NotEqual` `StartWith` `EndWith` `BeInAscendingOrder` `BeInDescendingOrder` `BeSubsetOf`
`NotBeSubsetOf` `BeProperSubsetOf` `BeSupersetOf` `NotBeSupersetOf` `BeProperSupersetOf`
`IntersectWith` `NotIntersectWith` `AllSatisfy` `SatisfyRespectively`
`AllBeOfType<T>` `AllBeAssignableTo<T>` `BeEquivalentTo` `NotBeEquivalentTo`

`Equal`, `StartWith` and `EndWith` also take a comparison lambda, which is how two differently
shaped sequences get compared without projecting one into the other first:

```csharp
orders.Should().Equal(dtos, (order, dto) => order.Id == dto.Id);
```

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

On the thrown exception: `WithMessage` `WithInnerException<T>` `WithInnerExceptionExactly<T>`
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

A `TaskCompletionSource` — the shape a test reaches for when it has to observe a signal raised by
code it does not control — has its own assertions, because the task already exists and is already
running:

```csharp
await tcs.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
await tcs.Should().NotCompleteWithinAsync(TimeSpan.FromMilliseconds(50));

var result = (await typedTcs.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5))).Which;
```

A faulted source is reported as a fault, not as a timeout.

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
silently ignored. `MonitoredEvents` is the other half of that pair — worth checking when an event
you expected simply is not there, because "never raised" and "never watched" produce the same
failure and mean opposite things. `OccurredEvents` exposes the raw recordings, `GetRecordingFor(name)`
narrows them to one event without asserting, and `Clear()` resets them. Asserting on an event name
that does not exist throws rather than passing vacuously.

```csharp
monitor.NotRaiseAnyEvents();                         // covers events added to the type later, too
monitor.Raise(nameof(Subject.Renamed)).Times(2);
monitor.Raise(nameof(Subject.Renamed))               // ONE occurrence must satisfy both
       .WithArgs<RenamedEventArgs>(e => e.OldName == "a", e => e.NewName == "b");
```

`monitor.Should().Raise(...)` is accepted as an alias of `monitor.Raise(...)`, for code ported from
FluentAssertions. The terse form is the one used here: the monitor *is* the subject.

Options change what gets watched:

```csharp
using var monitor = subject.Monitor(EventMonitorOptions.Default with
{
    IncludeInterfaceEvents = true,      // reaches an explicitly implemented PropertyChanged
    ThrowOnUnmonitoredEvents = true,    // fail rather than silently skip
    EventFilter = e => e.Name != "Noisy",
});
```

`IncludeInterfaceEvents` is the one to reach for when a class implements `INotifyPropertyChanged`
*explicitly*: `typeof(T).GetEvents()` does not list the event, so the monitor records nothing and
every `RaisePropertyChangeFor` fails with "does not expose a public event named PropertyChanged".

The monitor holds its subject **weakly**, so keeping a monitor in a fixture field does not keep the
subject alive.

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

### Types, members and assemblies

`Be<T>` `NotBe<T>` `BeAssignableTo<T>` `BeDerivedFrom<T>` `Implement<TInterface>`
`BeDecoratedWith<TAttribute>` (optionally with a predicate; returns the attribute via `Which`)
`BeDecoratedWithOrInherit<TAttribute>` `NotBeDecoratedWith<TAttribute>` `BeAbstract` `BeSealed`
`BeStatic` `BeAnInterface` `BeAClass` `BeInNamespace` `BeUnderNamespace`

The member checks return the `MemberInfo` via `Which`, so they chain:

```csharp
typeof(Order).Should().HaveProperty<int>("Id");
typeof(Order).Should().HaveMethod("Total", [typeof(decimal)]).Which.Should().Return<decimal>();
typeof(Order).Should().HaveDefaultConstructor();
typeof(Order).Should().HaveIndexer([typeof(int)]);
```

`MethodInfo`: `BeVirtual` `NotBeVirtual` `BeStatic` `Return<T>` `ReturnVoid` `BeAsync` `HaveName`
`BeDeclaredOn<T>` `BeDecoratedWith<TAttribute>`.
`PropertyInfo`: `BeOfType<T>` `BeReadOnly` `BeWritable` `BeVirtual`, plus the shared member checks.

`Assembly`: `Reference` `NotReference` `DefineType` `BeSigned` `NotBeSigned`.

**Architecture rules over a set of types**, which report *every* offender rather than the first:

```csharp
AllTypes.FromAssemblyContaining<Order>()
    .ThatImplement<IHandler>()
    .Should().NotBeEmpty()          // see below
        .And.BeSealed()
        .And.BeUnderNamespace("Shop.Handlers");
```

Selectors: `ThatDeriveFrom<T>` `ThatImplement<T>` `ThatAreDecoratedWith<TAttribute>`
`ThatAreUnderNamespace` `ThatArePublic` `ThatAreClasses` `Where(predicate)`.

`NotBeEmpty()` is worth the extra line: every rule over an empty set holds vacuously, so a selector
whose filter stopped matching — because the types were renamed, moved or deleted — passes the whole
suite while checking nothing.

> ⚠️ This family is reflection by nature and is annotated `[RequiresUnreferencedCode]`. It lives on
> assertion types nothing else touches, so it costs its own caller and no one else — but it belongs
> in a test project, not in trimmed output.

### Streams

`BeReadable` `BeWritable` `BeSeekable` (and the `Not` forms) `BeReadOnly` `BeWriteOnly`
`HaveLength` `NotHaveLength` `HavePosition` `NotHavePosition` `BeAtStart` `BeAtEnd`; on a
`BufferedStream`, also `HaveBufferSize` / `NotHaveBufferSize`.

Asking a non-seekable stream for its length or position fails with that as the reason, rather than
letting the stream's `NotSupportedException` escape from what was supposed to be an assertion.

### XML

`XDocument`: `HaveRoot` `HaveElement` `BeEquivalentTo`.
`XElement`: `HaveName` `HaveValue` `HaveAttribute` `HaveAttributeWithValue` `NotHaveAttribute`
`HaveElement` `HaveElementCount` `BeEmpty` `BeEquivalentTo`.
`XAttribute`: `HaveName` `HaveValue`.

```csharp
document.Should().HaveRoot("order").Which.Should().HaveAttributeWithValue("id", "7");
```

`HaveAttributeWithValue` is spelled out rather than being a second `HaveAttribute` overload:
`HaveAttribute(name, value)` and `HaveAttribute(name, because)` have identical parameter types, so
one silently wins. `BeEquivalentTo` here is `XNode.DeepEquals` — ordered and whitespace-sensitive,
which is XML's own definition rather than a second opinion about it.

---

## Failure messages

Everything in this section runs **only when an assertion fails**, so none of it costs a green suite
anything.

```csharp
Formatter.Options = FormattingOptions.Default with { MaxDepth = 6, UseLineBreaks = true };
```

| Setting | Effect |
|---|---|
| `MaxDepth` | How deep into a graph to render (default 3). The elision names this knob. |
| `MaxStringLength` | Characters before a string is truncated (default 512) |
| `MaxEnumerableItems` | Items before a sequence is truncated (default 32) |
| `MaxLines` | Lines before the rendering is cut (default 100) |
| `UseLineBreaks` | One member or item per line, indented (default off) |

A scope can override them for one block, and register a renderer for one type:

```csharp
using var scope = new AssertionScope()
    .WithFormatting(o => o with { UseLineBreaks = true })
    .Using(new MoneyFormatter());
```

`Formatter.Register(...)` does the same globally. There is **no assembly scan** for attributed
formatters, which is how FluentAssertions discovers them: the types would be reachable only by
reflection, so a trimmer removes them and the scan silently finds nothing. One explicit line cannot
fail that way.

### Scopes can be inspected

| Member | Use |
|---|---|
| `Discard()` | Take the failures so far and clear them — the building block for an assertion that probes and reports its own message |
| `AddPreFormattedFailure(text)` | Add a message verbatim, with no template escaping |
| `AddReportable(key, () => …)` | Attach context that is rendered **only if the scope fails** |
| `HasFailures` | Whether anything has been collected |

### The exception type is yours to choose

```csharp
AssertionConfiguration.ExceptionFactory = message => new Xunit.Sdk.XunitException(message);
```

The default is `AssertionFailedException`. FluentAssertions probes loaded assemblies for the test
framework's own exception type by name; that is a reflective lookup a trimmer defeats silently, so
this library asks instead of guessing. It matters less than it looks — every runner reports an
unexpected exception as a failed test.

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
| `WithInnerExceptionExactly<T>` | `WithInnerExceptionExactly<T>` |

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
- Negative assertions (`NotContain`, `NotBeApproximately`, …) treat a null subject as passing; positive
  ones require a value.
- `BeOneOf` and other `params` overloads have a sibling taking `IEnumerable<T>` when you also
  need `because`, since C# allows only one trailing `params`.

Part of [MintPlayer.Dotnet.Tools](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools).
Licensed under Apache-2.0 — permanently.
