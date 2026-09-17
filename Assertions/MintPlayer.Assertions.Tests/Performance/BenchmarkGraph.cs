namespace MintPlayer.Assertions.Tests.Performance;

/// <summary>
/// The graph the README's benchmark measures: 5 types, 4 levels, a 20-item collection.
/// </summary>
/// <remarks>
/// Declared here rather than shared with <c>MintPlayer.Assertions.Benchmarks</c> on purpose — that
/// project references FluentAssertions as a comparison baseline, which has no business in the test
/// assembly. The shape is what matters, and it is the shape the 13.13 µs / 20.34 KB figure was
/// measured on, so a number produced here is comparable to the one in the PRD.
/// </remarks>
internal static class BenchmarkGraph
{
    internal sealed class Order
    {
        public int Id { get; set; }
        public string Reference { get; set; } = "";
        public DateTime PlacedOn { get; set; }
        public Customer Customer { get; set; } = null!;
        public List<OrderLine> Lines { get; set; } = [];
    }

    internal sealed class Customer
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public Address Address { get; set; } = null!;
    }

    internal sealed class Address
    {
        public string Street { get; set; } = "";
        public string City { get; set; } = "";
        public string PostalCode { get; set; } = "";
        public string Country { get; set; } = "";
    }

    internal sealed class OrderLine
    {
        public int Quantity { get; set; }
        public Product Product { get; set; } = null!;
    }

    internal sealed class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
    }

    public static Order Create() => new()
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
}
