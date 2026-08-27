using MintPlayer.Assertions;

namespace MintPlayer.Assertions.Tests;

public class EquivalencyTests
{
    [Fact]
    public void BeEquivalentTo_Passes_ForEqualNestedObjects()
    {
        var subject = new Person { Name = "John", Address = new Address { City = "Ghent", Street = "Main" } };
        var expectation = new Person { Name = "John", Address = new Address { City = "Ghent", Street = "Main" } };

        subject.Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public void BeEquivalentTo_Fails_ForNestedValueMismatch()
    {
        var subject = new Person { Name = "John", Address = new Address { City = "Brussels", Street = "Main" } };
        var expectation = new Person { Name = "John", Address = new Address { City = "Ghent", Street = "Main" } };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Address.City", ex!.Message);
        Assert.Contains("\"Ghent\"", ex!.Message);
        Assert.Contains("\"Brussels\"", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_ReportsAllDifferences()
    {
        var subject = new Person { Name = "Jane", Address = new Address { City = "Brussels", Street = "Side" } };
        var expectation = new Person { Name = "John", Address = new Address { City = "Ghent", Street = "Side" } };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Name", ex!.Message);
        Assert.Contains("Address.City", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_Passes_ForEqualRecords()
    {
        new Point(1, 2).Should().BeEquivalentTo(new Point(1, 2));
    }

    [Fact]
    public void BeEquivalentTo_Fails_ForDifferentRecords()
    {
        var ex = Record.Exception(() => new Point(1, 2).Should().BeEquivalentTo(new Point(1, 3)));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Y", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_Passes_ComparingDtoToAnonymousObject()
    {
        var subject = new PersonDto { Name = "John", Age = 30 };

        subject.Should().BeEquivalentTo(new { Name = "John" });
    }

    [Fact]
    public void BeEquivalentTo_Fails_ComparingDtoToDifferentAnonymousObject()
    {
        var subject = new PersonDto { Name = "John", Age = 30 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(new { Name = "John", Age = 31 }));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Age", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_Fails_WhenSubjectMissesAMemberOfTheExpectation()
    {
        var subject = new { Name = "John" };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(new PersonDto { Name = "John", Age = 30 }));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("expectation has member Age but subject does not", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_Passes_ForUnorderedValueCollections()
    {
        var subject = new[] { 1, 2, 3 };

        ((object)subject).Should().BeEquivalentTo(new[] { 3, 2, 1 });
    }

    [Fact]
    public void BeEquivalentTo_Passes_ForUnorderedComplexCollections()
    {
        var subject = new List<Person> { new() { Name = "B" }, new() { Name = "A" } };
        var expectation = new List<Person> { new() { Name = "A" }, new() { Name = "B" } };

        ((object)subject).Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public void BeEquivalentTo_Fails_WhenACollectionItemIsMissing()
    {
        var subject = new[] { 1, 2 };

        var ex = Record.Exception(() => ((object)subject).Should().BeEquivalentTo(new[] { 1, 3 }));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("expected collection to contain 3", ex!.Message);
        Assert.Contains("found unexpected item(s)", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_Fails_OnCollectionCountMismatch()
    {
        var subject = new[] { 1, 2 };

        var ex = Record.Exception(() => ((object)subject).Should().BeEquivalentTo(new[] { 1, 2, 3 }));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("expected 3 item(s), but found 2", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_Passes_ForEqualDictionaries()
    {
        var subject = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var expectation = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 };

        ((object)subject).Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public void BeEquivalentTo_Fails_WhenDictionaryKeyIsMissing()
    {
        var subject = new Dictionary<string, int> { ["a"] = 1 };
        var expectation = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        var ex = Record.Exception(() => ((object)subject).Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("expected dictionary to contain key \"b\"", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_Fails_OnDictionaryValueMismatch()
    {
        var subject = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var expectation = new Dictionary<string, int> { ["a"] = 1, ["b"] = 3 };

        var ex = Record.Exception(() => ((object)subject).Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("[b]", ex!.Message);
        Assert.Contains("expected 3, but found 2", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_DoesNotHang_OnSelfReferencingObjects()
    {
        var subject = new Node { Name = "root" };
        subject.Next = subject;
        var expectation = new Node { Name = "root" };
        expectation.Next = expectation;

        subject.Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public void BeEquivalentTo_Fails_OnSelfReferencingObjectsWithDifferences()
    {
        var subject = new Node { Name = "one" };
        subject.Next = subject;
        var expectation = new Node { Name = "two" };
        expectation.Next = expectation;

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Name", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_Passes_WhenBothAreNull()
    {
        ((object?)null).Should().BeEquivalentTo((Person?)null);
    }

    [Fact]
    public void BeEquivalentTo_Fails_WhenSubjectIsNull()
    {
        var ex = Record.Exception(() => ((object?)null).Should().BeEquivalentTo(new Person { Name = "John" }));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("but found <null>", ex!.Message);
    }

    [Fact]
    public void BeEquivalentTo_Fails_WhenExpectationIsNull()
    {
        var subject = new Person { Name = "John" };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo((Person?)null));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("expected <null>", ex!.Message);
    }

    [Fact]
    public void NotBeEquivalentTo_Passes_ForDifferentObjects()
    {
        var subject = new Person { Name = "John" };

        subject.Should().NotBeEquivalentTo(new Person { Name = "Jane" });
    }

    [Fact]
    public void NotBeEquivalentTo_Fails_ForEquivalentObjects()
    {
        var subject = new Person { Name = "John" };

        var ex = Record.Exception(() => subject.Should().NotBeEquivalentTo(new Person { Name = "John" }));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex!.Message);
        Assert.Contains("no differences were found", ex!.Message);
    }
}

file sealed class Person
{
    public string? Name { get; set; }
    public Address? Address { get; set; }
}

file sealed class Address
{
    public string? City { get; set; }
    public string? Street { get; set; }
}

file sealed class PersonDto
{
    public string? Name { get; set; }
    public int Age { get; set; }
}

file sealed class Node
{
    public string? Name { get; set; }
    public Node? Next { get; set; }
}

file sealed record Point(int X, int Y);
