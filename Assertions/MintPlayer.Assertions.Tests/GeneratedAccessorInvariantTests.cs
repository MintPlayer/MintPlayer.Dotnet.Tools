using MintPlayer.Assertions;
using MintPlayer.Assertions.Equivalency;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The two invariants MintPlayer.Assertions.Benchmarks asserts in its Verification.Run() before
/// it will benchmark anything: that the source generator actually emitted accessors (so
/// BeEquivalentTo runs reflection-free), and that equivalency genuinely walks the whole graph
/// rather than short-circuiting.
/// </summary>
/// <remarks>
/// The invariants are brought over rather than the code. Verification.Run() needs
/// BenchmarkDotNet's DTO graph and FluentAssertions as a comparison baseline, neither of which
/// belongs in this project — so this declares its own graph and asserts the same two things.
/// Until now these invariants were only checked by running the benchmark project by hand.
/// </remarks>
public class GeneratedAccessorInvariantTests
{
    [AssertEquivalency]
    public class Invoice
    {
        public int Id { get; set; }
        public string Reference { get; set; } = "";
        public DateTime IssuedOn { get; set; }
        public Party Recipient { get; set; } = null!;
        public List<InvoiceLine> Lines { get; set; } = [];
    }

    public class Party
    {
        public string Name { get; set; } = "";
        public PostalAddress Address { get; set; } = null!;
    }

    public class PostalAddress
    {
        public string Street { get; set; } = "";
        public string City { get; set; } = "";
        public string PostalCode { get; set; } = "";
    }

    public class InvoiceLine
    {
        public int Number { get; set; }
        public Item Item { get; set; } = null!;
        public int Quantity { get; set; }
    }

    public class Item
    {
        public string Sku { get; set; } = "";
        public decimal Price { get; set; }
    }

    private static Invoice CreateInvoice() => new()
    {
        Id = 42,
        Reference = "INV-2026-000042",
        IssuedOn = new DateTime(2026, 8, 27, 10, 30, 0, DateTimeKind.Utc),
        Recipient = new Party
        {
            Name = "Jane Doe",
            Address = new PostalAddress { Street = "Main Street 1", City = "Ghent", PostalCode = "9000" },
        },
        Lines =
        [
            .. Enumerable.Range(1, 20).Select(i => new InvoiceLine
            {
                Number = i,
                Item = new Item { Sku = $"SKU-{i:D4}", Price = 9.99m * i },
                Quantity = i % 5 + 1,
            }),
        ],
    };

    /// <summary>
    /// Invariant 1: the generator emitted accessors for the decorated type AND every type
    /// reachable from it. If any is missing, equivalency silently falls back to reflection —
    /// which still works, but loses the AOT-safety and the ~15x speed that are the library's
    /// entire selling point over FluentAssertions.
    /// </summary>
    [Theory]
    [InlineData(typeof(Invoice))]
    [InlineData(typeof(Party))]
    [InlineData(typeof(PostalAddress))]
    [InlineData(typeof(InvoiceLine))]
    [InlineData(typeof(Item))]
    public void TheGeneratorEmitsAccessorsForTheWholeReachableGraph(Type type)
        => EquivalencyRegistry.TryGetAccessors(type, out _)
            .Should().BeTrue($"no generated accessors for {type.Name}: equivalency would fall back to reflection");

    [Fact]
    public void TheGeneratedAccessorsCoverEveryPublicProperty()
    {
        EquivalencyRegistry.TryGetAccessors(typeof(PostalAddress), out var accessors).Should().BeTrue();

        accessors.Should().NotBeNull();
        accessors!.Should().HaveCount(typeof(PostalAddress).GetProperties().Length);
    }

    /// <summary>
    /// Invariant 2: a difference buried at the deepest level of the graph is detected. Only a
    /// full traversal finds it, so this catches a comparison that short-circuits into a fast
    /// "equal" — the failure mode that would make the benchmark meaningless and, worse, make
    /// BeEquivalentTo silently pass on unequal objects.
    /// </summary>
    [Fact]
    public void ADifferenceAtTheDeepestLevelIsDetected()
    {
        var actual = CreateInvoice();
        var mutated = CreateInvoice();
        mutated.Lines[^1].Item.Price += 0.01m;

        Action act = () => AssertionExtensions.Should((object)actual).BeEquivalentTo(mutated);

        act.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void ADifferenceInAMidLevelPropertyIsDetected()
    {
        var actual = CreateInvoice();
        var mutated = CreateInvoice();
        mutated.Recipient.Address.City = "Antwerp";

        Action act = () => AssertionExtensions.Should((object)actual).BeEquivalentTo(mutated);

        act.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void AnIdenticalGraphIsEquivalent()
        => AssertionExtensions.Should((object)CreateInvoice()).BeEquivalentTo(CreateInvoice());

    [Fact]
    public void ADifferenceInCollectionLengthIsDetected()
    {
        var actual = CreateInvoice();
        var mutated = CreateInvoice();
        mutated.Lines.RemoveAt(mutated.Lines.Count - 1);

        Action act = () => AssertionExtensions.Should((object)actual).BeEquivalentTo(mutated);

        act.Should().Throw<AssertionFailedException>();
    }

    [Fact]
    public void TheFailureMessageNamesThePathToTheDifference()
    {
        var actual = CreateInvoice();
        var mutated = CreateInvoice();
        mutated.Recipient.Name = "Someone Else";

        Action act = () => AssertionExtensions.Should((object)actual).BeEquivalentTo(mutated);

        // Without a path the message is useless on a 4-level graph.
        act.Should().Throw<AssertionFailedException>().WithMessage("*Recipient*");
    }
}
