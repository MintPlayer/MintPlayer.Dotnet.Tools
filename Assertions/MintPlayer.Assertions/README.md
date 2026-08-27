# MintPlayer.Assertions

A modern fluent assertion library for .NET.

> **License pledge:** MintPlayer.Assertions is Apache-2.0 and **will never change license**.
> Not to a commercial license, not to a source-available license — never.

```csharp
using MintPlayer.Assertions;

order.Total.Should().Be(120m, because: "the order was paid");

items.Should().HaveCount(3)
     .And.ContainSingle(i => i.IsPrimary)
     .Which.Name.Should().Be("main");

actualDto.Should().BeEquivalentTo(expectedDto, opt => opt
    .Excluding(x => x.Id)
    .WithStrictOrdering());

var act = () => service.Process(null!);
act.Should().Throw<ArgumentNullException>().WithParameterName("order");
await asyncAct.Should().ThrowAsync<TimeoutException>().WithMessage("*timed out*");

using (new AssertionScope("the response"))
{
    response.Status.Should().Be(200);
    response.Body.Should().NotBeEmpty();
} // one combined failure listing everything
```

## Why another assertion library?

- **AOT/trimming-safe.** No reflection on the hot path. Object-graph equivalency
  (`BeEquivalentTo`) runs on **source-generated member accessors** registered at module load —
  faster than reflection-walking libraries, and it works under Native AOT.
- **Failure messages with your expression in them**, via `[CallerArgumentExpression]` — no PDB
  tricks: `Expected order.Total to be 120M because the order was paid, but found 90M.`
- **Analyzers in the box.** Forgetting to `await` an async assertion is a **build error**, not
  a silently-green test. Vacuous assertions produce warnings. A code fix migrates
  FluentAssertions/AwesomeAssertions call sites automatically.
- **Framework-agnostic.** Throws its own `AssertionFailedException`; works with xUnit, NUnit,
  MSTest, TUnit — no framework detection, no adapter packages.
- **Stable extensibility.** Custom assertions are one `Assertion.For(...).ForCondition(...)
  .BecauseOf(...).FailWith(...)` chain, or a `[GenerateAssertion]` attribute on a static bool
  method. This surface will not break across versions.

## Features

Scalars, strings (with diffs), numerics (generic-math based), dates/times (`DateOnly`/`TimeOnly`
included), Guids, enums, nullable types, collections and dictionaries (with `.Which` drilling),
`Span<T>`/`ReadOnlySpan<T>`, JSON (`JsonElement`/`JsonNode`), exceptions (sync + async),
`CompleteWithinAsync`, event monitoring (`subject.Monitor()`), execution-time assertions,
object-graph equivalency with rich options, and `AssertionScope` soft assertions.

One `PackageReference` brings the library, the source generator and the analyzers.
