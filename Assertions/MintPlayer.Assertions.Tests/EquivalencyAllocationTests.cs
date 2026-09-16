namespace MintPlayer.Assertions.Tests;

/// <summary>
/// An allocation gate on the equivalency walker itself — the thing the headline benchmark measures.
/// </summary>
/// <remarks>
/// <para>
/// The Phase 2 PRD names "the equivalency benchmark is still not gating" as its one unmet success
/// criterion, and that gap let a real regression ship: the unordered collection matcher was changed
/// to build its full candidate matrix up front, so an already-aligned 20-item collection went from
/// ~20 subtree comparisons to 400. Nothing caught it, because
/// <c>PassingPathAllocationTests</c> measures per-assertion overhead, not the walk.
/// </para>
/// <para>
/// Allocation rather than time, for the same reason as every other gate here — bytes are
/// deterministic and survive a loaded machine, where wall-clock does not. The regression that
/// prompted this was 60× in bytes, so a time gate was never needed to catch it.
/// </para>
/// <para>
/// The assertion is <b>relative</b>, not a pinned byte count: matching an already-ordered collection
/// unordered must not cost dramatically more than comparing it pairwise with
/// <c>WithStrictOrdering()</c>. That states the actual rule — the matcher may do more work when
/// items genuinely contend, but not when the very first candidate fits.
/// </para>
/// </remarks>
public class EquivalencyAllocationTests
{
    #region The graph — deliberately the benchmark's shape: 5 types, 4 levels, a 20-item collection

    private sealed class Order
    {
        public int Id { get; set; }
        public string Reference { get; set; } = "";
        public DateTime PlacedOn { get; set; }
        public Customer Customer { get; set; } = null!;
        public List<OrderLine> Lines { get; set; } = [];
    }

    private sealed class Customer
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public Address Address { get; set; } = null!;
    }

    private sealed class Address
    {
        public string Street { get; set; } = "";
        public string City { get; set; } = "";
        public string PostalCode { get; set; } = "";
        public string Country { get; set; } = "";
    }

    private sealed class OrderLine
    {
        public int Quantity { get; set; }
        public Product Product { get; set; } = null!;
    }

    private sealed class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
    }

    private static Order CreateOrder() => new()
    {
        Id = 42,
        Reference = "ORD-2026-000042",
        PlacedOn = new DateTime(2026, 8, 27, 10, 30, 0, DateTimeKind.Utc),
        Customer = new()
        {
            Name = "Jane Doe",
            Email = "jane@example.com",
            Address = new() { Street = "Main Street 1", City = "Ghent", PostalCode = "9000", Country = "BE" },
        },
        Lines = [.. Enumerable.Range(1, 20).Select(i => new OrderLine
        {
            Quantity = i,
            Product = new() { Id = i, Name = $"Product {i}", Price = 9.99m + i },
        })],
    };

    #endregion

    private static long MeasureBytes(Action action, int iterations)
    {
        // Warm up: JIT, and let the module initializer register the generated accessors, so
        // neither is billed to the measurement.
        for (var i = 0; i < 3; i++) action();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++) action();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    /// <summary>
    /// Matching an already-ordered collection unordered must stay in the same league as comparing it
    /// pairwise. The matcher is allowed to do more work when items genuinely contend for the same
    /// candidate — it is not allowed to do it when the first candidate tried already fits.
    /// </summary>
    [Fact]
    public void UnorderedMatchingOfAnAlreadyOrderedCollectionStaysNearStrictOrdering()
    {
        var actual = CreateOrder();
        var expected = CreateOrder();

        var strict = MeasureBytes(
            () => ((object)actual).Should().BeEquivalentTo(expected, o => o.WithStrictOrdering()), 20);
        var unordered = MeasureBytes(
            () => ((object)actual).Should().BeEquivalentTo(expected), 20);

        Assert.True(unordered <= strict * 3,
            $"Unordered matching allocated {unordered:N0} B/op against {strict:N0} B/op for strict ordering "
            + $"({(double)unordered / strict:F1}x). The matcher is doing work proportional to n² on a "
            + "collection whose first candidate already fits — see the candidate-matrix comment in "
            + "EquivalencyValidator.");
    }

    /// <summary>
    /// A second, blunter guard: the whole comparison must stay far below what a reflection walker
    /// costs. FluentAssertions allocates ~404 KB for this graph; the generated accessors are the
    /// reason this library allocates a fraction of that, and a regression that erased the advantage
    /// entirely is exactly what shipped unnoticed once.
    /// </summary>
    [Fact]
    public void TheWalkStaysWellUnderAReflectionWalkersCost()
    {
        var actual = CreateOrder();
        var expected = CreateOrder();

        var bytes = MeasureBytes(() => ((object)actual).Should().BeEquivalentTo(expected), 20);

        // 100 KB is deliberately loose — it is a smoke alarm, not a thermostat. The recorded figure
        // is ~20 KB; FluentAssertions is ~404 KB. Anything that drifts past this has changed the
        // shape of the walk, not tuned it.
        Assert.True(bytes < 100 * 1024,
            $"The equivalency walk allocated {bytes:N0} B/op for the 5-type/4-level/20-item graph. "
            + "The recorded baseline is ~20 KB and a reflection walker is ~404 KB, so this has "
            + "stopped being a fast path.");
    }
}
