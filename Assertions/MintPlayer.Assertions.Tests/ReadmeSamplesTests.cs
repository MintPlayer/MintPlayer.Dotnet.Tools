using System.Net.Sockets;
using System.Text.Json;
using MintPlayer.Assertions;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Tests.DocSamples;

// Every code sample in the package README, compiled and executed. The README is the first
// thing a user copies from, so a sample that stops compiling - or stops passing - is a bug;
// this file makes CI catch that instead of a reader discovering it.
public class Order
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Total { get; set; }
    public DateTime PlacedOn { get; set; }
    public bool IsSettled { get; set; }
    public string Status { get; set; } = "";
    public AuditInfo Audit { get; set; } = new();
}

public class AuditInfo { public DateTime ModifiedOn { get; set; } }

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

public static class Predicates
{
    [GenerateAssertion]
    public static bool IsEven(int value) => value % 2 == 0;
}

public class DocSamples
{
    [Fact]
    public void Basics()
    {
        var order = new Order { Total = 120m, IsSettled = true, Status = "settled" };
        order.Total.Should().Be(120m, because: "the order was paid");

        var count = 3;
        count.Should().Be(3, because: "the importer skips duplicates");

        var items = new[] { new Order { Name = "main", Id = 1 }, new Order { Name = "b" } };
        items.Should().HaveCount(2)
             .And.ContainSingle(i => i.Id == 1)
             .Which.Name.Should().Be("main");

        ((object)order).Should().BeSettled();
    }

    [Fact]
    public void SoftAssertions()
    {
        var status = 200; var body = "x";
        var headers = new Dictionary<string, string> { ["ETag"] = "1" };

        using (new AssertionScope("the response"))
        {
            status.Should().Be(200);
            body.Should().NotBeEmpty();
            headers.Should().ContainKey("ETag");
        }
    }

    [Fact]
    public void Equivalency()
    {
        var dto = new Order { Id = 1, Name = "Ada" };
        var expected = new Order { Id = 1, Name = "Ada" };

        ((object)dto).Should().BeEquivalentTo(new { Id = 1, Name = "Ada" });

        ((object)dto).Should().BeEquivalentTo(expected, opt => opt
            .Excluding(x => x.Total)
            .ExcludingNested((AuditInfo a) => a.ModifiedOn)
            .Using<DateTime>((a, e) => a.Should().BeCloseTo(e, TimeSpan.FromSeconds(1)))
            .WithStrictOrdering());
    }

    [Fact]
    public void ScalarsAndStrings()
    {
        var name = "hello world";
        name.Should().StartWith("hello").And.MatchRegex("^hello");

        var temperature = 21.52;
        temperature.Should().BeCloseTo(21.5, 0.1);

        var ratio = 0.5;
        ratio.Should().BeInRange(0, 1);
    }

    [Fact]
    public void Collections()
    {
        var orders = new List<Order>
        {
            new() { Id = 1, Total = 5m, PlacedOn = new DateTime(2026, 1, 1) },
            new() { Id = 2, Total = 6m, PlacedOn = new DateTime(2026, 2, 1) },
        };

        orders.Should().BeInAscendingOrder(o => o.PlacedOn);
        orders.Should().AllSatisfy(o => o.Total.Should().BePositive());
        orders.Should().SatisfyRespectively(
            first => first.Id.Should().Be(1),
            second => second.Id.Should().Be(2));

        var versions = new Dictionary<string, string> { ["Newtonsoft.Json"] = "13.0.3" };
        versions.Should().ContainKey("Newtonsoft.Json").Which.Should().Be("13.0.3");
    }

    [Fact]
    public void ExceptionsAndTiming()
    {
        Action act = () => throw new ArgumentNullException("order");
        act.Should().Throw<ArgumentNullException>().WithParameterName("order");

        var noop = () => { };
        noop.Should().NotThrow();
        noop.Should().ExecutionTime().BeLessThan(TimeSpan.FromSeconds(30));

        var parse = () => int.Parse("42");
        var parsed = parse.Should().NotThrow().Which;
        parsed.Should().Be(42);

        var number = 4;
        number.Should().BeEven();

        Action missing = () => throw new InvalidOperationException("The Widget was NOT FOUND");
        missing.Should().Throw<InvalidOperationException>()
               .WithMessage("*not found*", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Async()
    {
        var act = () => Task.CompletedTask;
        await act.Should().NotThrowAsync();
        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(30));

        var throwing = () => Task.FromException(new TimeoutException("it timed out"));
        await throwing.Should().ThrowAsync<TimeoutException>().WithMessage("*timed out*");

        var inner = () => Task.FromException(
            new HttpRequestException("outer", new SocketException(10061)));
        await inner.Should().ThrowAsync<HttpRequestException>()
                   .WithInnerException<SocketException>();
    }

    [Fact]
    public void Json()
    {
        using var document = JsonDocument.Parse("""{ "id": 1, "tags": ["a", "b"] }""");

        document.Should().BeJsonEquivalentTo("""{ "id": 1, "tags": ["a", "b"] }""");
        document.RootElement.Should().HaveProperty("id").Which.Should().HaveNumberValue(1);
    }

    [Fact]
    public void Types()
    {
        typeof(Order).Should().BeAClass().And.NotBeDecoratedWith<ObsoleteAttribute>();
    }

    [Fact]
    public void DictionaryEquivalency()
    {
        var lanes = new Dictionary<string, Lane> { ["left"] = new() { Width = 3 } };

        lanes.Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["left"] = new { Width = 3 },
        });
    }

    /// <summary>
    /// The README states that a comparison which compares nothing is refused, and quotes the
    /// message. This keeps that promise honest.
    /// </summary>
    [Fact]
    public void VacuousComparisonIsRefused()
    {
        var invoice = new Invoice { Name = "ACME", Amount = 2 };

        var ex = Record.Exception(() => invoice.Should().BeEquivalentTo(new object()));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("No members were compared", ex!.Message);
        Assert.Contains("AllowingVacuousComparison()", ex.Message);

        // And the documented opt-out works.
        invoice.Should().BeEquivalentTo(new object(), o => o.AllowingVacuousComparison());
    }

    /// <summary>
    /// The README warns that Excluding is root-relative and so does not reach a collection
    /// element's member. Pin both halves of that claim.
    /// </summary>
    [Fact]
    public void ExcludingIsRootRelativeOnCollections()
    {
        Lane[] subject = [new() { Width = 3 }];
        Lane[] expectation = [new() { Width = 99 }];

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingNested<Lane>(x => x.Width));
        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingPath("*.Width"));

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.Excluding(x => x.Width)));
        Assert.IsType<AssertionFailedException>(ex);
    }
}

public class Lane
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
}

public class Invoice
{
    public string Name { get; set; } = "";
    public int Amount { get; set; }
}
