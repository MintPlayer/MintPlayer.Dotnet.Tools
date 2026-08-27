using MintPlayer.Assertions;
using MintPlayer.Assertions.Equivalency;

namespace MintPlayer.Assertions.Tests;

public class EquivalencyOptionsTests
{
    [Fact]
    public void Excluding_SkipsATopLevelMember()
    {
        var subject = new Person { Name = "John", Age = 30 };
        var expectation = new Person { Name = "Jane", Age = 30 };

        subject.Should().BeEquivalentTo(expectation, o => o.Excluding(p => p.Name));
    }

    [Fact]
    public void Excluding_SupportsMemberChains()
    {
        var subject = new Person { Name = "John", Address = new Address { City = "Brussels", Street = "Main" } };
        var expectation = new Person { Name = "John", Address = new Address { City = "Ghent", Street = "Main" } };

        subject.Should().BeEquivalentTo(expectation, o => o.Excluding(p => p.Address!.City));
    }

    [Fact]
    public void Excluding_Throws_ForNonMemberExpressions()
    {
        var options = new EquivalencyOptions<Person>();

        var ex = Record.Exception(() => options.Excluding(p => p.Name!.Trim()));

        Assert.IsType<ArgumentException>(ex);
    }

    [Fact]
    public void ExcludingNested_AppliesToNodesInsideCollections()
    {
        var subject = new Team
        {
            Name = "A-Team",
            Members = [new() { Name = "John", Age = 30 }, new() { Name = "Jane", Age = 25 }],
        };
        var expectation = new Team
        {
            Name = "A-Team",
            Members = [new() { Name = "Johnny", Age = 30 }, new() { Name = "Janet", Age = 25 }],
        };

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingNested<Member>(m => m.Name));
    }

    [Fact]
    public void ExcludingPath_SkipsWildcardMatchedPaths()
    {
        var subject = new Person { Name = "John", Address = new Address { City = "Brussels", Street = "Side" } };
        var expectation = new Person { Name = "John", Address = new Address { City = "Ghent", Street = "Main" } };

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingPath("Address.*"));
    }

    [Fact]
    public void Including_LimitsComparisonToTheIncludedMembers()
    {
        var subject = new Person { Name = "John", Age = 30, Address = new Address { City = "Brussels" } };
        var expectation = new Person { Name = "John", Age = 31, Address = new Address { City = "Ghent" } };

        subject.Should().BeEquivalentTo(expectation, o => o.Including(p => p.Name));
    }

    [Fact]
    public void Including_StillComparesTheIncludedMember()
    {
        var subject = new Person { Name = "John", Age = 30 };
        var expectation = new Person { Name = "Jane", Age = 30 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.Including(p => p.Name)));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Name", ex!.Message);
    }

    [Fact]
    public void Using_AppliesTheCustomComparison()
    {
        var subject = new Person { Name = "JOHN", Age = 30 };
        var expectation = new Person { Name = "john", Age = 30 };

        subject.Should().BeEquivalentTo(expectation, o => o.Using<string>((s, e) =>
        {
            if (!string.Equals(s, e, StringComparison.OrdinalIgnoreCase))
                throw new AssertionFailedException($"expected \"{e}\" (ignoring case), but found \"{s}\"");
        }));
    }

    [Fact]
    public void Using_ReportsTheCustomFailureAtThePath()
    {
        var subject = new Person { Name = "Pete", Age = 30 };
        var expectation = new Person { Name = "john", Age = 30 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.Using<string>((s, e) =>
        {
            if (!string.Equals(s, e, StringComparison.OrdinalIgnoreCase))
                throw new AssertionFailedException($"expected \"{e}\" (ignoring case), but found \"{s}\"");
        })));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Name", ex!.Message);
        Assert.Contains("(ignoring case)", ex!.Message);
    }

    [Fact]
    public void WithStrictOrdering_FailsOnSwappedItems()
    {
        var subject = new[] { 1, 2 };

        var ex = Record.Exception(() =>
            ((object)subject).Should().BeEquivalentTo(new[] { 2, 1 }, o => o.WithStrictOrdering()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("[0]", ex!.Message);
    }

    [Fact]
    public void WithoutStrictOrdering_SwappedItemsAreEquivalent()
    {
        ((object)new[] { 1, 2 }).Should().BeEquivalentTo(new[] { 2, 1 });
    }

    [Fact]
    public void ComparingByValue_UsesEqualsInsteadOfMembers()
    {
        var subject = new IdEntity { Id = 1, Label = "one" };
        var expectation = new IdEntity { Id = 1, Label = "uno" };

        subject.Should().BeEquivalentTo(expectation, o => o.ComparingByValue<IdEntity>());
    }

    [Fact]
    public void WithoutComparingByValue_MembersAreCompared()
    {
        var subject = new IdEntity { Id = 1, Label = "one" };
        var expectation = new IdEntity { Id = 1, Label = "uno" };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Label", ex!.Message);
    }

    [Fact]
    public void ComparingByMembers_OverridesComparingByValue()
    {
        var subject = new IdEntity { Id = 1, Label = "one" };
        var expectation = new IdEntity { Id = 1, Label = "uno" };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation,
            o => o.ComparingByValue<IdEntity>().ComparingByMembers<IdEntity>()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Label", ex!.Message);
    }

    [Fact]
    public void WithMaxDepth_TreatsDeeperNodesAsEqual()
    {
        var subject = Chain.Build(4, leafValue: 1);
        var expectation = Chain.Build(4, leafValue: 2);

        subject.Should().BeEquivalentTo(expectation, o => o.WithMaxDepth(2));
    }

    [Fact]
    public void WithinMaxDepth_DifferencesAreFound()
    {
        var subject = Chain.Build(4, leafValue: 1);
        var expectation = Chain.Build(4, leafValue: 2);

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Value", ex!.Message);
    }

    [Fact]
    public void AllowingInfiniteRecursion_FindsDifferencesBeyondTheDefaultDepth()
    {
        var subject = Chain.Build(15, leafValue: 1);
        var expectation = Chain.Build(15, leafValue: 2);

        // The default depth of 10 hides the difference at level 15...
        subject.Should().BeEquivalentTo(expectation);

        // ...but without the limit it is found.
        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.AllowingInfiniteRecursion()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Value", ex!.Message);
    }

    [Fact]
    public void RespectingRuntimeTypes_ComparesMembersOfTheRuntimeType()
    {
        Animal subject = new Dog { Name = "Rex", Breed = "Labrador" };
        Animal expectation = new Dog { Name = "Rex", Breed = "Poodle" };

        // Declared as Animal, only Name takes part by default.
        subject.Should().BeEquivalentTo(expectation);

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.RespectingRuntimeTypes()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Breed", ex!.Message);
    }
}

file sealed class Person
{
    public string? Name { get; set; }
    public int Age { get; set; }
    public Address? Address { get; set; }
}

file sealed class Address
{
    public string? City { get; set; }
    public string? Street { get; set; }
}

file sealed class Team
{
    public string? Name { get; set; }
    public List<Member> Members { get; set; } = [];
}

file sealed class Member
{
    public string? Name { get; set; }
    public int Age { get; set; }
}

file sealed class IdEntity
{
    public int Id { get; set; }
    public string? Label { get; set; }

    public override bool Equals(object? obj) => obj is IdEntity other && other.Id == Id;
    public override int GetHashCode() => Id;
}

file sealed class Chain
{
    public int Value { get; set; }
    public Chain? Next { get; set; }

    /// <summary>A linked chain of the given length whose deepest node carries <paramref name="leafValue"/>.</summary>
    public static Chain Build(int levels, int leafValue)
        => levels == 0 ? new Chain { Value = leafValue } : new Chain { Next = Build(levels - 1, leafValue) };
}

file class Animal
{
    public string? Name { get; set; }
}

file sealed class Dog : Animal
{
    public string? Breed { get; set; }
}
