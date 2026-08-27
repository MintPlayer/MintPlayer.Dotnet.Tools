namespace MintPlayer.Assertions.Tests;

public class CollectionAssertionsTests
{
    private static AssertionFailedException Fails(Action action)
    {
        var exception = Record.Exception(action);
        return Assert.IsType<AssertionFailedException>(exception);
    }

    [Fact]
    public void BeEmpty()
    {
        Array.Empty<int>().Should().BeEmpty();

        var ex = Fails(() => new[] { 1 }.Should().BeEmpty());
        Assert.Contains("to be empty", ex.Message);
        Assert.Contains("{1}", ex.Message);
    }

    [Fact]
    public void BeEmpty_NullSubject()
    {
        IEnumerable<int>? subject = null;
        var ex = Fails(() => subject.Should().BeEmpty());
        Assert.Contains("but found <null>", ex.Message);
        Assert.Contains("subject", ex.Message);
    }

    [Fact]
    public void NotBeEmpty()
    {
        new[] { 1 }.Should().NotBeEmpty();

        var ex = Fails(() => Array.Empty<int>().Should().NotBeEmpty("we filled it"));
        Assert.Contains("not to be empty", ex.Message);
        Assert.Contains("because we filled it", ex.Message);
    }

    [Fact]
    public void HaveCount()
    {
        new[] { 1, 2, 3 }.Should().HaveCount(3);

        var ex = Fails(() => new[] { 1, 2 }.Should().HaveCount(3));
        Assert.Contains("to contain 3 item(s)", ex.Message);
        Assert.Contains("but found 2", ex.Message);
    }

    [Fact]
    public void HaveCount_Predicate()
    {
        new[] { 1, 2, 3 }.Should().HaveCount(c => c % 2 == 1);

        var ex = Fails(() => new[] { 1, 2 }.Should().HaveCount(c => c % 2 == 1));
        Assert.Contains("count matching the given predicate", ex.Message);
        Assert.Contains("count is 2", ex.Message);
    }

    [Fact]
    public void HaveCountGreaterThan()
    {
        new[] { 1, 2 }.Should().HaveCountGreaterThan(1);

        var ex = Fails(() => new[] { 1, 2 }.Should().HaveCountGreaterThan(2));
        Assert.Contains("to contain more than 2 item(s)", ex.Message);
    }

    [Fact]
    public void HaveCountGreaterThanOrEqualTo()
    {
        new[] { 1, 2 }.Should().HaveCountGreaterThanOrEqualTo(2);

        var ex = Fails(() => new[] { 1 }.Should().HaveCountGreaterThanOrEqualTo(2));
        Assert.Contains("to contain at least 2 item(s)", ex.Message);
    }

    [Fact]
    public void HaveCountLessThan()
    {
        new[] { 1 }.Should().HaveCountLessThan(2);

        var ex = Fails(() => new[] { 1, 2 }.Should().HaveCountLessThan(2));
        Assert.Contains("to contain fewer than 2 item(s)", ex.Message);
    }

    [Fact]
    public void HaveCountLessThanOrEqualTo()
    {
        new[] { 1, 2 }.Should().HaveCountLessThanOrEqualTo(2);

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().HaveCountLessThanOrEqualTo(2));
        Assert.Contains("to contain at most 2 item(s)", ex.Message);
    }

    [Fact]
    public void HaveSameCountAs()
    {
        new[] { 1, 2 }.Should().HaveSameCountAs(new[] { "a", "b" });

        var ex = Fails(() => new[] { 1, 2 }.Should().HaveSameCountAs(new[] { "a" }));
        Assert.Contains("to have 1 item(s), the same count as the other collection", ex.Message);
    }

    [Fact]
    public void NotHaveSameCountAs()
    {
        new[] { 1, 2 }.Should().NotHaveSameCountAs(new[] { "a" });

        var ex = Fails(() => new[] { 1, 2 }.Should().NotHaveSameCountAs(new[] { "a", "b" }));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("2 item(s)", ex.Message);
    }

    [Fact]
    public void ContainSingle()
    {
        var which = new[] { 42 }.Should().ContainSingle().Which;
        Assert.Equal(42, which);

        var ex = Fails(() => new[] { 1, 2 }.Should().ContainSingle());
        Assert.Contains("to contain a single item", ex.Message);
        Assert.Contains("but found 2", ex.Message);
    }

    [Fact]
    public void ContainSingle_Predicate()
    {
        var which = new[] { 1, 42, 3 }.Should().ContainSingle(i => i > 10).Which;
        Assert.Equal(42, which);

        var ex = Fails(() => new[] { 11, 42 }.Should().ContainSingle(i => i > 10));
        Assert.Contains("a single item matching the given predicate", ex.Message);
        Assert.Contains("but found 2", ex.Message);
    }

    [Fact]
    public void Contain()
    {
        new[] { 1, 2, 3 }.Should().Contain(2);

        var ex = Fails(() => new[] { 1, 3 }.Should().Contain(2));
        Assert.Contains("to contain 2", ex.Message);
    }

    [Fact]
    public void Contain_Predicate()
    {
        var which = new[] { 1, 20, 30 }.Should().Contain(i => i > 10).Which;
        Assert.Equal(20, which);

        var ex = Fails(() => new[] { 1, 2 }.Should().Contain(i => i > 10));
        Assert.Contains("an item matching the given predicate", ex.Message);
    }

    [Fact]
    public void NotContain()
    {
        new[] { 1, 3 }.Should().NotContain(2);

        var ex = Fails(() => new[] { 1, 2 }.Should().NotContain(2));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to contain 2", ex.Message);
    }

    [Fact]
    public void NotContain_Predicate()
    {
        new[] { 1, 2 }.Should().NotContain(i => i > 10);

        var ex = Fails(() => new[] { 1, 20 }.Should().NotContain(i => i > 10));
        Assert.Contains("an item matching the given predicate", ex.Message);
        Assert.Contains("{20}", ex.Message);
    }

    [Fact]
    public void ContainInOrder()
    {
        new[] { 1, 2, 3, 4, 5 }.Should().ContainInOrder(1, 3, 5);

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().ContainInOrder(3, 1));
        Assert.Contains("in order", ex.Message);
    }

    [Fact]
    public void OnlyContain()
    {
        new[] { 2, 4 }.Should().OnlyContain(i => i % 2 == 0);

        var ex = Fails(() => new[] { 2, 3, 5 }.Should().OnlyContain(i => i % 2 == 0));
        Assert.Contains("only contain items matching the given predicate", ex.Message);
        Assert.Contains("{3, 5}", ex.Message);
    }

    [Fact]
    public void OnlyHaveUniqueItems()
    {
        new[] { 1, 2, 3 }.Should().OnlyHaveUniqueItems();

        var ex = Fails(() => new[] { 1, 2, 2, 3, 3 }.Should().OnlyHaveUniqueItems());
        Assert.Contains("unique items", ex.Message);
        Assert.Contains("{2, 3}", ex.Message);
    }

    [Fact]
    public void NotContainNulls()
    {
        new[] { "a", "b" }.Should().NotContainNulls();

        var ex = Fails(() => new[] { "a", null, "b", null }.Should().NotContainNulls());
        Assert.Contains("not to contain <null> items", ex.Message);
        Assert.Contains("{1, 3}", ex.Message);
    }

    [Fact]
    public void Equal()
    {
        new[] { 1, 2, 3 }.Should().Equal(1, 2, 3);
        new[] { 1, 2, 3 }.Should().Equal(new List<int> { 1, 2, 3 });

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().Equal(1, 9, 3));
        Assert.Contains("differs at index 1", ex.Message);
        Assert.Contains("found 2 instead of 9", ex.Message);
    }

    [Fact]
    public void Equal_CountMismatch()
    {
        var ex = Fails(() => new[] { 1, 2 }.Should().Equal(1, 2, 3));
        Assert.Contains("contains 2 item(s) instead of 3", ex.Message);
    }

    [Fact]
    public void NotEqual()
    {
        new[] { 1, 2 }.Should().NotEqual(new[] { 2, 1 });

        var ex = Fails(() => new[] { 1, 2 }.Should().NotEqual(new[] { 1, 2 }));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to equal", ex.Message);
    }

    [Fact]
    public void StartWith_Item()
    {
        new[] { 1, 2, 3 }.Should().StartWith(1);

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().StartWith(2));
        Assert.Contains("to start with 2", ex.Message);
        Assert.Contains("but found 1", ex.Message);
    }

    [Fact]
    public void StartWith_Sequence()
    {
        new[] { 1, 2, 3 }.Should().StartWith(new[] { 1, 2 });

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().StartWith(new[] { 2, 3 }));
        Assert.Contains("to start with {2, 3}", ex.Message);
    }

    [Fact]
    public void EndWith_Item()
    {
        new[] { 1, 2, 3 }.Should().EndWith(3);

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().EndWith(2));
        Assert.Contains("to end with 2", ex.Message);
        Assert.Contains("but found 3", ex.Message);
    }

    [Fact]
    public void EndWith_Sequence()
    {
        new[] { 1, 2, 3 }.Should().EndWith(new[] { 2, 3 });

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().EndWith(new[] { 1, 2 }));
        Assert.Contains("to end with {1, 2}", ex.Message);
    }

    [Fact]
    public void BeInAscendingOrder()
    {
        new[] { 1, 2, 2, 3 }.Should().BeInAscendingOrder();

        var ex = Fails(() => new[] { 1, 3, 2 }.Should().BeInAscendingOrder());
        Assert.Contains("ascending order", ex.Message);
        Assert.Contains("found 3 before 2 at index 1", ex.Message);
    }

    [Fact]
    public void BeInAscendingOrder_Selector()
    {
        new[] { "a", "bb", "ccc" }.Should().BeInAscendingOrder(s => s.Length);

        var ex = Fails(() => new[] { "ccc", "a" }.Should().BeInAscendingOrder(s => s.Length));
        Assert.Contains("ascending order", ex.Message);
    }

    [Fact]
    public void BeInAscendingOrder_Comparer()
    {
        new[] { 3, 2, 1 }.Should().BeInAscendingOrder(Comparer<int>.Create((a, b) => b.CompareTo(a)));

        var ex = Fails(() => new[] { 1, 2 }.Should().BeInAscendingOrder(Comparer<int>.Create((a, b) => b.CompareTo(a))));
        Assert.Contains("ascending order", ex.Message);
    }

    [Fact]
    public void BeInDescendingOrder()
    {
        new[] { 3, 2, 1 }.Should().BeInDescendingOrder();
        new[] { "ccc", "bb", "a" }.Should().BeInDescendingOrder(s => s.Length);

        var ex = Fails(() => new[] { 1, 2 }.Should().BeInDescendingOrder());
        Assert.Contains("descending order", ex.Message);
        Assert.Contains("found 1 before 2 at index 0", ex.Message);
    }

    [Fact]
    public void BeSubsetOf()
    {
        new[] { 1, 2 }.Should().BeSubsetOf(new[] { 1, 2, 3 });

        var ex = Fails(() => new[] { 1, 4 }.Should().BeSubsetOf(new[] { 1, 2, 3 }));
        Assert.Contains("to be a subset of", ex.Message);
        Assert.Contains("{4}", ex.Message);
    }

    [Fact]
    public void NotBeSubsetOf()
    {
        new[] { 1, 4 }.Should().NotBeSubsetOf(new[] { 1, 2, 3 });

        var ex = Fails(() => new[] { 1, 2 }.Should().NotBeSubsetOf(new[] { 1, 2, 3 }));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("subset", ex.Message);
    }

    [Fact]
    public void IntersectWith()
    {
        new[] { 1, 2 }.Should().IntersectWith(new[] { 2, 3 });

        var ex = Fails(() => new[] { 1, 2 }.Should().IntersectWith(new[] { 3, 4 }));
        Assert.Contains("to intersect with", ex.Message);
        Assert.Contains("do not share any items", ex.Message);
    }

    [Fact]
    public void NotIntersectWith()
    {
        new[] { 1, 2 }.Should().NotIntersectWith(new[] { 3, 4 });

        var ex = Fails(() => new[] { 1, 2 }.Should().NotIntersectWith(new[] { 2, 3 }));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("{2}", ex.Message);
    }

    [Fact]
    public void AllSatisfy()
    {
        new[] { 2, 4, 6 }.Should().AllSatisfy(i => ((object)(i % 2)).Should().Be(0));

        var ex = Fails(() => new[] { 2, 3, 5 }.Should().AllSatisfy(i => ((object)(i % 2)).Should().Be(0)));
        Assert.Contains("to all satisfy the given assertion", ex.Message);
        Assert.Contains("item at index 1", ex.Message);
        Assert.Contains("item at index 2", ex.Message);
        Assert.DoesNotContain("item at index 0", ex.Message);
    }

    [Fact]
    public void SatisfyRespectively()
    {
        new[] { 1, 2 }.Should().SatisfyRespectively(
            first => ((object)first).Should().Be(1),
            second => ((object)second).Should().Be(2));

        var ex = Fails(() => new[] { 1, 2 }.Should().SatisfyRespectively(
            first => ((object)first).Should().Be(9),
            second => ((object)second).Should().Be(2)));
        Assert.Contains("item at index 0", ex.Message);
        Assert.DoesNotContain("item at index 1", ex.Message);
    }

    [Fact]
    public void SatisfyRespectively_CountMismatch()
    {
        var ex = Fails(() => new[] { 1 }.Should().SatisfyRespectively(
            first => { },
            second => { }));
        Assert.Contains("to satisfy all 2 inspector(s)", ex.Message);
        Assert.Contains("contains 1 item(s)", ex.Message);
    }

    [Fact]
    public void AllBeOfType()
    {
        new object[] { "a", "b" }.Should().AllBeOfType<string>();

        var ex = Fails(() => new object[] { "a", 1 }.Should().AllBeOfType<string>());
        Assert.Contains("to all be of type", ex.Message);
        Assert.Contains("index 1", ex.Message);
        Assert.Contains("System.Int32", ex.Message);
    }

    [Fact]
    public void AllBeAssignableTo()
    {
        new object[] { "a", 1 }.Should().AllBeAssignableTo<IComparable>();

        var ex = Fails(() => new object?[] { "a", null }.Should().AllBeAssignableTo<string>());
        Assert.Contains("to all be assignable to", ex.Message);
        Assert.Contains("index 1", ex.Message);
    }

    [Fact]
    public void Subject_IsEnumeratedOnlyOnce_PerAssertionsInstance()
    {
        var enumerations = 0;
        IEnumerable<int> Sequence()
        {
            enumerations++;
            yield return 1;
            yield return 2;
        }

        Sequence().Should().HaveCount(2).And.Contain(1).And.StartWith(1).And.EndWith(2);
        Assert.Equal(1, enumerations);
    }

    [Fact]
    public void Failures_AreCollected_InAnAssertionScope()
    {
        var exception = Record.Exception(() =>
        {
            using var scope = new Execution.AssertionScope();
            new[] { 1 }.Should().BeEmpty();
            new[] { 1 }.Should().HaveCount(2);
        });

        var ex = Assert.IsType<AssertionFailedException>(exception);
        Assert.Contains("to be empty", ex.Message);
        Assert.Contains("to contain 2 item(s)", ex.Message);
    }
}
