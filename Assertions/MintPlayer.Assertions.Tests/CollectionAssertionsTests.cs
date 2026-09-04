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
    public void NotHaveCount()
    {
        new[] { 1, 2 }.Should().NotHaveCount(3);

        var ex = Fails(() => new[] { 1, 2 }.Should().NotHaveCount(2));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to contain 2 item(s)", ex.Message);
        Assert.Contains("but found {1, 2}", ex.Message);
    }

    [Fact]
    public void NotHaveCount_NullSubject()
    {
        IEnumerable<int>? subject = null;
        var ex = Fails(() => subject.Should().NotHaveCount(2));
        Assert.Contains("not to contain 2 item(s)", ex.Message);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotContainSingle()
    {
        new[] { 1, 2 }.Should().NotContainSingle();
        Array.Empty<int>().Should().NotContainSingle();

        var ex = Fails(() => new[] { 1 }.Should().NotContainSingle());
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to contain a single item", ex.Message);
        Assert.Contains("but found {1}", ex.Message);
    }

    [Fact]
    public void NotContainSingle_NullSubject()
    {
        IEnumerable<int>? subject = null;
        var ex = Fails(() => subject.Should().NotContainSingle());
        Assert.Contains("not to contain a single item", ex.Message);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotContainSingle_Predicate()
    {
        // Two matches and zero matches both satisfy "not exactly one".
        new[] { 1, 2, 3 }.Should().NotContainSingle(i => i > 1);
        new[] { 1, 2, 3 }.Should().NotContainSingle(i => i > 5);

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().NotContainSingle(i => i == 2));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to contain a single item matching the given predicate", ex.Message);
        Assert.Contains("but found {2}", ex.Message);
    }

    [Fact]
    public void NotContainInOrder()
    {
        new[] { 1, 2, 3 }.Should().NotContainInOrder(3, 1);  // present, but out of order
        new[] { 1, 2, 3 }.Should().NotContainInOrder(1, 9);  // one item missing entirely

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().NotContainInOrder(1, 3));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to contain {1, 3} in order", ex.Message);
        Assert.Contains("but found {1, 2, 3}", ex.Message);
    }

    [Fact]
    public void NotContainInOrder_EmptyExpectation_AlwaysFails()
    {
        // Every collection vacuously contains nothing in order, so the negation cannot hold.
        var ex = Fails(() => new[] { 1 }.Should().NotContainInOrder());
        Assert.Contains("to contain {empty} in order", ex.Message);
    }

    [Fact]
    public void NotContainInOrder_NullSubject()
    {
        IEnumerable<int>? subject = null;
        var ex = Fails(() => subject.Should().NotContainInOrder(1, 2));
        Assert.Contains("not to contain {1, 2} in order", ex.Message);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotOnlyContain()
    {
        // 1 fails the predicate, so it is not true that the collection contains ONLY matches.
        new[] { 1, 2, 3 }.Should().NotOnlyContain(i => i > 1);

        var ex = Fails(() => new[] { 2, 3 }.Should().NotOnlyContain(i => i > 1));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to only contain items matching the given predicate", ex.Message);
        Assert.Contains("but all 2 item(s) did", ex.Message);
    }

    [Fact]
    public void NotOnlyContain_EmptyCollection_Fails()
    {
        // OnlyContain holds vacuously for an empty collection, so its negation must fail.
        var ex = Fails(() => Array.Empty<int>().Should().NotOnlyContain(i => i > 1));
        Assert.Contains("but all 0 item(s) did", ex.Message);
    }

    [Fact]
    public void NotOnlyContain_NullSubject()
    {
        IEnumerable<int>? subject = null;
        var ex = Fails(() => subject.Should().NotOnlyContain(i => i > 1));
        Assert.Contains("not to only contain items matching the given predicate", ex.Message);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotOnlyHaveUniqueItems()
    {
        new[] { 1, 2, 2 }.Should().NotOnlyHaveUniqueItems();

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().NotOnlyHaveUniqueItems());
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to only have unique items", ex.Message);
        Assert.Contains("but found {1, 2, 3}", ex.Message);
    }

    [Fact]
    public void NotOnlyHaveUniqueItems_EmptyCollection_Fails()
    {
        var ex = Fails(() => Array.Empty<int>().Should().NotOnlyHaveUniqueItems());
        Assert.Contains("to only have unique items", ex.Message);
    }

    [Fact]
    public void NotOnlyHaveUniqueItems_NullSubject()
    {
        IEnumerable<int>? subject = null;
        var ex = Fails(() => subject.Should().NotOnlyHaveUniqueItems());
        Assert.Contains("not to only have unique items", ex.Message);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void ContainNulls()
    {
        new string?[] { "a", null }.Should().ContainNulls();

        var ex = Fails(() => new string?[] { "a", "b" }.Should().ContainNulls());
        Assert.Contains("Expected", ex.Message);
        Assert.Contains("to contain <null> items", ex.Message);
        Assert.Contains("but found {\"a\", \"b\"}", ex.Message);
    }

    [Fact]
    public void ContainNulls_NullSubject()
    {
        IEnumerable<string?>? subject = null;
        var ex = Fails(() => subject.Should().ContainNulls());
        Assert.Contains("to contain <null> items", ex.Message);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotStartWith_Item()
    {
        new[] { 1, 2 }.Should().NotStartWith(2);
        Array.Empty<int>().Should().NotStartWith(1);  // an empty collection starts with nothing

        var ex = Fails(() => new[] { 1, 2 }.Should().NotStartWith(1));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to start with 1", ex.Message);
    }

    [Fact]
    public void NotStartWith_Sequence()
    {
        new[] { 1, 2, 3 }.Should().NotStartWith(new[] { 2, 3 });
        new[] { 1 }.Should().NotStartWith(new[] { 1, 2 });  // too short to have that prefix

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().NotStartWith(new[] { 1, 2 }));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to start with {1, 2}", ex.Message);
    }

    [Fact]
    public void NotStartWith_NullSubject()
    {
        IEnumerable<int>? subject = null;
        var item = Fails(() => subject.Should().NotStartWith(1));
        Assert.Contains("not to start with 1", item.Message);
        Assert.Contains("but found <null>", item.Message);

        var sequence = Fails(() => subject.Should().NotStartWith(new[] { 1, 2 }));
        Assert.Contains("not to start with {1, 2}", sequence.Message);
        Assert.Contains("but found <null>", sequence.Message);
    }

    [Fact]
    public void NotEndWith_Item()
    {
        new[] { 1, 2 }.Should().NotEndWith(1);
        Array.Empty<int>().Should().NotEndWith(1);

        var ex = Fails(() => new[] { 1, 2 }.Should().NotEndWith(2));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to end with 2", ex.Message);
    }

    [Fact]
    public void NotEndWith_Sequence()
    {
        new[] { 1, 2, 3 }.Should().NotEndWith(new[] { 1, 2 });
        new[] { 3 }.Should().NotEndWith(new[] { 2, 3 });  // too short to have that suffix

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().NotEndWith(new[] { 2, 3 }));
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to end with {2, 3}", ex.Message);
    }

    [Fact]
    public void NotEndWith_NullSubject()
    {
        IEnumerable<int>? subject = null;
        var item = Fails(() => subject.Should().NotEndWith(1));
        Assert.Contains("not to end with 1", item.Message);
        Assert.Contains("but found <null>", item.Message);

        var sequence = Fails(() => subject.Should().NotEndWith(new[] { 1, 2 }));
        Assert.Contains("not to end with {1, 2}", sequence.Message);
        Assert.Contains("but found <null>", sequence.Message);
    }

    [Fact]
    public void NotBeInAscendingOrder()
    {
        new[] { 2, 1, 3 }.Should().NotBeInAscendingOrder();

        var ex = Fails(() => new[] { 1, 2, 3 }.Should().NotBeInAscendingOrder());
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to be in ascending order", ex.Message);
        Assert.Contains("but found {1, 2, 3}", ex.Message);
    }

    [Fact]
    public void NotBeInAscendingOrder_SingleItemCollection_Fails()
    {
        // One item is vacuously ordered, so the negation cannot hold.
        var ex = Fails(() => new[] { 1 }.Should().NotBeInAscendingOrder());
        Assert.Contains("to be in ascending order", ex.Message);
    }

    [Fact]
    public void NotBeInAscendingOrder_Comparer()
    {
        // Reversed comparer: {1, 2} is NOT ascending under it, though it is under the default one.
        var reversed = Comparer<int>.Create((a, b) => b.CompareTo(a));
        new[] { 1, 2 }.Should().NotBeInAscendingOrder(reversed);

        var ex = Fails(() => new[] { 2, 1 }.Should().NotBeInAscendingOrder(reversed));
        Assert.Contains("to be in ascending order", ex.Message);
    }

    [Fact]
    public void NotBeInAscendingOrder_Selector()
    {
        new[] { "bb", "a" }.Should().NotBeInAscendingOrder(s => s.Length);

        var ex = Fails(() => new[] { "a", "bb" }.Should().NotBeInAscendingOrder(s => s.Length));
        Assert.Contains("to be in ascending order", ex.Message);
    }

    [Fact]
    public void NotBeInAscendingOrder_NullSubject()
    {
        IEnumerable<int>? subject = null;
        var ex = Fails(() => subject.Should().NotBeInAscendingOrder());
        Assert.Contains("not to be in ascending order", ex.Message);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotBeInDescendingOrder()
    {
        new[] { 1, 2, 3 }.Should().NotBeInDescendingOrder();

        var ex = Fails(() => new[] { 3, 2, 1 }.Should().NotBeInDescendingOrder());
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to be in descending order", ex.Message);
        Assert.Contains("but found {3, 2, 1}", ex.Message);
    }

    [Fact]
    public void NotBeInDescendingOrder_Comparer()
    {
        var reversed = Comparer<int>.Create((a, b) => b.CompareTo(a));
        new[] { 2, 1 }.Should().NotBeInDescendingOrder(reversed);

        var ex = Fails(() => new[] { 1, 2 }.Should().NotBeInDescendingOrder(reversed));
        Assert.Contains("to be in descending order", ex.Message);
    }

    [Fact]
    public void NotBeInDescendingOrder_Selector()
    {
        new[] { "a", "bb" }.Should().NotBeInDescendingOrder(s => s.Length);

        var ex = Fails(() => new[] { "bb", "a" }.Should().NotBeInDescendingOrder(s => s.Length));
        Assert.Contains("to be in descending order", ex.Message);
    }

    [Fact]
    public void NotBeInDescendingOrder_NullSubject()
    {
        IEnumerable<int>? subject = null;
        var ex = Fails(() => subject.Should().NotBeInDescendingOrder());
        Assert.Contains("not to be in descending order", ex.Message);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void AllEqualItems_AreBothAscendingAndDescending()
    {
        // Documented consequence: neither negation can hold for a run of equal items.
        Assert.Contains("ascending", Fails(() => new[] { 1, 1 }.Should().NotBeInAscendingOrder()).Message);
        Assert.Contains("descending", Fails(() => new[] { 1, 1 }.Should().NotBeInDescendingOrder()).Message);
    }

    [Fact]
    public void NotAllBeOfType()
    {
        new object[] { "a", 1 }.Should().NotAllBeOfType<string>();

        var ex = Fails(() => new object[] { "a", "b" }.Should().NotAllBeOfType<string>());
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to all be of type", ex.Message);
        Assert.Contains("but all 2 item(s) are", ex.Message);
    }

    [Fact]
    public void NotAllBeOfType_EmptyCollection_Fails()
    {
        var ex = Fails(() => Array.Empty<object>().Should().NotAllBeOfType<string>());
        Assert.Contains("but all 0 item(s) are", ex.Message);
    }

    [Fact]
    public void NotAllBeOfType_NullSubject()
    {
        IEnumerable<object>? subject = null;
        var ex = Fails(() => subject.Should().NotAllBeOfType<string>());
        Assert.Contains("not to all be of type", ex.Message);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NotAllBeAssignableTo()
    {
        new object?[] { "a", null }.Should().NotAllBeAssignableTo<string>();

        var ex = Fails(() => new object[] { "a", 1 }.Should().NotAllBeAssignableTo<IComparable>());
        Assert.Contains("Did not expect", ex.Message);
        Assert.Contains("to all be assignable to", ex.Message);
        Assert.Contains("but all 2 item(s) are", ex.Message);
    }

    [Fact]
    public void NotAllBeAssignableTo_NullSubject()
    {
        IEnumerable<object>? subject = null;
        var ex = Fails(() => subject.Should().NotAllBeAssignableTo<string>());
        Assert.Contains("not to all be assignable to", ex.Message);
        Assert.Contains("but found <null>", ex.Message);
    }

    [Fact]
    public void NegativeAssertions_EnumerateTheSubjectOnlyOnce()
    {
        var enumerations = 0;
        IEnumerable<int> Sequence()
        {
            enumerations++;
            yield return 1;
            yield return 3;
        }

        Sequence().Should().NotHaveCount(3)
            .And.NotStartWith(3)
            .And.NotEndWith(1)
            .And.NotContainInOrder(3, 1)
            .And.NotContainSingle()
            .And.NotOnlyContain(i => i == 1)
            .And.NotBeInDescendingOrder();
        Assert.Equal(1, enumerations);
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
            using var scope = new AssertionScope();
            new[] { 1 }.Should().BeEmpty();
            new[] { 1 }.Should().HaveCount(2);
        });

        var ex = Assert.IsType<AssertionFailedException>(exception);
        Assert.Contains("to be empty", ex.Message);
        Assert.Contains("to contain 2 item(s)", ex.Message);
    }
}
