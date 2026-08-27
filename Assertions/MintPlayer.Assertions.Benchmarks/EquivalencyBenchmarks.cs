using BenchmarkDotNet.Attributes;

namespace MintPlayer.Assertions.Benchmarks;

/// <summary>
/// Compares object-graph equivalency: MintPlayer.Assertions (source-generated member accessors)
/// vs FluentAssertions 7 (runtime reflection walker) on a representative 4-level DTO graph.
/// Extension methods are invoked through their static classes because both libraries define
/// Should() and the usings would collide.
/// </summary>
[MemoryDiagnoser]
public class EquivalencyBenchmarks
{
    private Order actual = null!;
    private Order expected = null!;

    [GlobalSetup]
    public void Setup()
    {
        actual = CreateOrder();
        expected = CreateOrder();
    }

    [Benchmark(Baseline = true)]
    public void FluentAssertions_BeEquivalentTo()
        => global::FluentAssertions.AssertionExtensions.Should(actual).BeEquivalentTo(expected);

    [Benchmark]
    public void MintPlayerAssertions_BeEquivalentTo()
        => global::MintPlayer.Assertions.AssertionExtensions.Should((object)actual).BeEquivalentTo(expected);

    internal static Order CreateOrder() => new()
    {
        Id = 42,
        Reference = "ORD-2026-000042",
        PlacedOn = new DateTime(2026, 8, 27, 10, 30, 0, DateTimeKind.Utc),
        Customer = new Customer
        {
            Name = "Jane Doe",
            Email = "jane@example.com",
            Address = new Address { Street = "Main Street 1", City = "Ghent", PostalCode = "9000", Country = "BE" },
        },
        Lines =
        [
            .. Enumerable.Range(1, 20).Select(i => new OrderLine
            {
                LineNumber = i,
                Product = new Product { Sku = $"SKU-{i:D4}", Name = $"Product {i}", Price = 9.99m * i },
                Quantity = i % 5 + 1,
            }),
        ],
    };
}

[AssertEquivalency] public class Order
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public DateTime PlacedOn { get; set; }
    public Customer Customer { get; set; } = null!;
    public List<OrderLine> Lines { get; set; } = [];
}

public class Customer
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public Address Address { get; set; } = null!;
}

public class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Country { get; set; } = "";
}

public class OrderLine
{
    public int LineNumber { get; set; }
    public Product Product { get; set; } = null!;
    public int Quantity { get; set; }
}

public class Product
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}
