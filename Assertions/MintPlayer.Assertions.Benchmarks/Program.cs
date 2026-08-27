using BenchmarkDotNet.Running;
using MintPlayer.Assertions;
using MintPlayer.Assertions.Benchmarks;
using MintPlayer.Assertions.Equivalency;

// A benchmark that compares two libraries is only meaningful if both do the same work, so the
// run is gated on proving it: the generated (reflection-free) path must actually be active, and
// both libraries must genuinely walk the graph rather than short-circuit into a fast "equal".
// Pass --verify-only to run just these checks.
Verification.Run();
if (args.Contains("--verify-only")) return;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

static class Verification
{
    public static void Run()
    {
        // 1. The source generator emitted accessors, so BeEquivalentTo is on the fast path.
        //    Without this the benchmark would quietly measure the reflection fallback instead.
        foreach (var type in new[] { typeof(Order), typeof(Customer), typeof(Address), typeof(OrderLine), typeof(Product) })
        {
            if (!EquivalencyRegistry.TryGetAccessors(type, out _))
                throw new InvalidOperationException($"No generated accessors for {type.Name}: the benchmark would measure the reflection fallback.");
        }

        // 2. Both libraries detect a difference buried at the deepest level of the graph, which
        //    only a full traversal can find.
        var actual = EquivalencyBenchmarks.CreateOrder();
        var mutated = EquivalencyBenchmarks.CreateOrder();
        mutated.Lines[^1].Product.Price += 0.01m;

        AssertDetects("MintPlayer.Assertions",
            () => AssertionExtensions.Should((object)actual).BeEquivalentTo(mutated));
        AssertDetects("FluentAssertions",
            () => global::FluentAssertions.AssertionExtensions.Should(actual).BeEquivalentTo(mutated));

        Console.WriteLine("Fairness checks passed: generated accessors active, both libraries traverse the full graph.");
    }

    private static void AssertDetects(string library, Action comparison)
    {
        try
        {
            comparison();
        }
        catch (Exception)
        {
            return; // Reported the difference, as it should.
        }

        throw new InvalidOperationException($"{library} did not detect a deep difference; the comparison is not doing equivalent work.");
    }
}
