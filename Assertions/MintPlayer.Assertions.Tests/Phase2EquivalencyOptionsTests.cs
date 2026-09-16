using System.Collections.Frozen;
using System.ComponentModel;
using MintPlayer.Assertions.Equivalency;
using MintPlayer.Assertions.Primitives;

namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The equivalency options added in Phase 2 M5 (PRD §6.1), plus the §6.3 fixes: a too-deep graph is
/// reported instead of passing silently, and a dictionary that implements only the generic
/// interfaces is compared by key instead of as a bag of pairs.
/// </summary>
public class Phase2EquivalencyOptionsTests
{
    #region Member visibility

    private sealed class WithInternals
    {
        public int Public { get; init; }
        internal int Internal { get; init; }
        public int PublicField;
        internal int InternalField;
    }

    /// <summary>The default has not moved: internal members stay out of the comparison.</summary>
    [Fact]
    public void InternalMembersAreExcludedByDefault()
    {
        var subject = new WithInternals { Public = 1, Internal = 2 };
        var expectation = new WithInternals { Public = 1, Internal = 99 };

        subject.Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public void IncludingInternalMembers_BringsThemIn()
    {
        var subject = new WithInternals { Public = 1, Internal = 2 };
        var expectation = new WithInternals { Public = 1, Internal = 99 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.IncludingInternalMembers()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Internal", ex!.Message);
    }

    [Fact]
    public void ExcludingFields_LeavesPropertiesOnly()
    {
        var subject = new WithInternals { Public = 1, PublicField = 1 };
        var expectation = new WithInternals { Public = 1, PublicField = 99 };

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingFields());
        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeEquivalentTo(expectation)));
    }

    [Fact]
    public void ExcludingProperties_LeavesFieldsOnly()
    {
        var subject = new WithInternals { Public = 1, PublicField = 5 };
        var expectation = new WithInternals { Public = 99, PublicField = 5 };

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingProperties());
    }

    private sealed class WithHiddenMember
    {
        public int Visible { get; init; }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public int Hidden { get; init; }
    }

    /// <summary>
    /// The documented divergence from FluentAssertions: hiding a member from IntelliSense says
    /// nothing about whether it should be compared, so it is compared by default.
    /// </summary>
    [Fact]
    public void NonBrowsableMembersAreComparedByDefault()
    {
        var subject = new WithHiddenMember { Visible = 1, Hidden = 2 };
        var expectation = new WithHiddenMember { Visible = 1, Hidden = 99 };

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeEquivalentTo(expectation)));

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingNonBrowsableMembers());
    }

    private interface IHasSecret
    {
        int Secret { get; }
    }

    private sealed class ExplicitImplementation : IHasSecret
    {
        public int Open { get; init; }
        public int SecretValue { get; init; }
        int IHasSecret.Secret => SecretValue;
    }

    [Fact]
    public void ExplicitInterfaceMembersAreExcludedByDefaultAndCanBeIncluded()
    {
        var subject = new ExplicitImplementation { Open = 1, SecretValue = 2 };
        var expectation = new ExplicitImplementation { Open = 1, SecretValue = 2 };

        subject.Should().BeEquivalentTo(expectation);
        subject.Should().BeEquivalentTo(expectation, o => o.IncludingExplicitInterfaceMembers());
    }

    #endregion

    #region Selection

    private sealed class Person
    {
        public string Name { get; init; } = "";
        public int Age { get; init; }
        public Address Home { get; init; } = new();
    }

    private sealed class Address
    {
        public string City { get; init; } = "";
        public string Street { get; init; } = "";
    }

    /// <summary>
    /// The bug this fixes: <c>Including(x =&gt; x.Home.City)</c> used to be matched at the root only,
    /// so it meant "compare all of Home" — a weaker assertion than the one written.
    /// </summary>
    [Fact]
    public void Including_ReachesANestedLeafAndStopsThere()
    {
        var subject = new Person { Name = "a", Age = 1, Home = new() { City = "Ghent", Street = "Main" } };
        var expectation = new Person { Name = "z", Age = 9, Home = new() { City = "Ghent", Street = "Other" } };

        subject.Should().BeEquivalentTo(expectation, o => o.Including(x => x.Home.City));

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.Including(x => x.Home)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Street", ex!.Message);
    }

    [Fact]
    public void Including_ARootMemberStillWorks()
    {
        var subject = new Person { Name = "a", Age = 1 };
        var expectation = new Person { Name = "a", Age = 99 };

        subject.Should().BeEquivalentTo(expectation, o => o.Including(x => x.Name));
    }

    [Fact]
    public void ExcludingMembersNamed_AppliesAtEveryDepth()
    {
        var subject = new Person { Name = "a", Age = 1, Home = new() { City = "Ghent", Street = "Main" } };
        var expectation = new Person { Name = "a", Age = 1, Home = new() { City = "Other", Street = "Main" } };

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingMembersNamed("City"));
    }

    [Fact]
    public void ExcludingByMemberType()
    {
        var subject = new Person { Name = "a", Age = 1 };
        var expectation = new Person { Name = "different", Age = 1 };

        subject.Should().BeEquivalentTo(expectation, o => o.Excluding<string>());
    }

    private sealed class RenamedDto
    {
        public string FullName { get; init; } = "";
    }

    [Fact]
    public void WithMapping_MatchesARenamedMember()
    {
        var subject = new Person { Name = "Alice", Age = 1 };
        var expectation = new RenamedDto { FullName = "Alice" };

        subject.Should().BeEquivalentTo(expectation, o => o.WithMapping("FullName", "Name"));

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));
        Assert.Contains("FullName", ex!.Message);
    }

    [Fact]
    public void ExcludingMissingMembers_TurnsAMissingMemberIntoAPass()
    {
        var subject = new Person { Name = "Alice", Age = 1 };
        var expectation = new RenamedDto { FullName = "anything" };

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingMissingMembers().AllowingVacuousComparison());

        Assert.IsType<AssertionFailedException>(Record.Exception(
            () => subject.Should().BeEquivalentTo(expectation, o => o.ThrowingOnMissingMembers())));
    }

    #endregion

    #region Comparison behaviour

    private enum Left { One, Two }

    private enum Right { Two, One }

    private sealed class HoldsLeft { public Left Value { get; init; } }

    private sealed class HoldsRight { public Right Value { get; init; } }

    /// <summary>
    /// The case the option exists for: the same concept modelled twice, with the members declared in
    /// a different order so the underlying values do not line up.
    /// </summary>
    [Fact]
    public void ComparingEnumsByName()
    {
        var subject = new HoldsLeft { Value = Left.Two };
        var expectation = new HoldsRight { Value = Right.Two };

        subject.Should().BeEquivalentTo(expectation, o => o.ComparingEnumsByName());

        Assert.IsType<AssertionFailedException>(Record.Exception(
            () => subject.Should().BeEquivalentTo(expectation, o => o.ComparingEnumsByValue())));
    }

    private sealed record Point(int X, int Y);

    private sealed class HoldsPoint { public Point Value { get; init; } = new(0, 0); }

    [Fact]
    public void ComparingRecordsByValue()
    {
        var subject = new HoldsPoint { Value = new(1, 2) };
        var same = new HoldsPoint { Value = new(1, 2) };
        var different = new HoldsPoint { Value = new(1, 9) };

        subject.Should().BeEquivalentTo(same, o => o.ComparingRecordsByValue());
        Assert.IsType<AssertionFailedException>(Record.Exception(
            () => subject.Should().BeEquivalentTo(different, o => o.ComparingRecordsByValue())));
    }

    private sealed class HoldsText { public string Text { get; init; } = ""; }

    [Fact]
    public void ComparingStringsWith_ReusesTheStringAssertionOptions()
    {
        var subject = new HoldsText { Text = "  Hello\r\nWorld  " };
        var expectation = new HoldsText { Text = "hello\nworld" };

        subject.Should().BeEquivalentTo(expectation, o => o.ComparingStringsWith(
            StringMatchOptions.IgnoringCase | StringMatchOptions.IgnoringNewlineStyle | StringMatchOptions.IgnoringSurroundingWhitespace));

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeEquivalentTo(expectation)));
    }

    [Fact]
    public void TreatingNullAsEmptyString()
    {
        var subject = new HoldsText { Text = "" };
        var expectation = new HoldsText { Text = null! };

        subject.Should().BeEquivalentTo(expectation, o => o.TreatingNullAsEmptyString());

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeEquivalentTo(expectation)));
    }

    private sealed class HoldsDouble { public double Value { get; init; } }

    private sealed class Tolerant : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 0.01;
        public int GetHashCode(double value) => 0;
    }

    [Fact]
    public void Using_AnEqualityComparer()
    {
        var subject = new HoldsDouble { Value = 1.0005 };
        var expectation = new HoldsDouble { Value = 1.0 };

        subject.Should().BeEquivalentTo(expectation, o => o.Using<double>(new Tolerant()));

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeEquivalentTo(expectation)));
    }

    private sealed class HoldsLists
    {
        public int[] Ordered { get; init; } = [];
        public int[] Unordered { get; init; } = [];
    }

    [Fact]
    public void StrictOrderingForOnePathOnly()
    {
        var subject = new HoldsLists { Ordered = [1, 2], Unordered = [1, 2] };
        var expectation = new HoldsLists { Ordered = [1, 2], Unordered = [2, 1] };

        subject.Should().BeEquivalentTo(expectation, o => o.WithStrictOrderingFor("Ordered"));

        var reversed = new HoldsLists { Ordered = [2, 1], Unordered = [1, 2] };
        Assert.IsType<AssertionFailedException>(Record.Exception(
            () => reversed.Should().BeEquivalentTo(expectation, o => o.WithStrictOrderingFor("Ordered"))));
    }

    [Fact]
    public void WithoutStrictOrderingFor_CarvesAnExceptionOutOfStrictOrdering()
    {
        var subject = new HoldsLists { Ordered = [1, 2], Unordered = [1, 2] };
        var expectation = new HoldsLists { Ordered = [1, 2], Unordered = [2, 1] };

        subject.Should().BeEquivalentTo(expectation,
            o => o.WithStrictOrdering().WithoutStrictOrderingFor("Unordered"));
    }

    #endregion

    #region Depth, cycles and dictionaries (§6.3)

    private sealed class Node
    {
        public int Value { get; init; }
        public Node? Next { get; set; }
    }

    private static Node Chain(int length)
    {
        var head = new Node { Value = 0 };
        var current = head;
        for (var i = 1; i < length; i++)
        {
            current.Next = new Node { Value = i };
            current = current.Next;
        }
        return head;
    }

    /// <summary>
    /// The §6.3 fix. A graph deeper than the limit used to be treated as <em>equal</em> below the
    /// cut and reported nothing at all — a silent pass, which is the worst thing an assertion
    /// library can do.
    /// </summary>
    [Fact]
    public void ATooDeepGraphIsReported()
    {
        var subject = Chain(30);
        var expectation = Chain(30);

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("deeper than the configured maximum depth", ex!.Message);
    }

    [Fact]
    public void RaisingTheDepthLimitLetsADeepGraphThrough()
    {
        Chain(30).Should().BeEquivalentTo(Chain(30), o => o.WithMaxDepth(100));
        Chain(30).Should().BeEquivalentTo(Chain(30), o => o.AllowingInfiniteRecursion());
    }

    [Fact]
    public void ACycleIsIgnoredByDefaultAndCanBeReported()
    {
        static Node Cycle()
        {
            var head = new Node { Value = 1 };
            head.Next = head;
            return head;
        }

        Cycle().Should().BeEquivalentTo(Cycle(), o => o.AllowingInfiniteRecursion());

        var ex = Record.Exception(() => Cycle().Should().BeEquivalentTo(Cycle(),
            o => o.AllowingInfiniteRecursion().ThrowingOnCyclicReferences()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("acyclic", ex!.Message);
    }

    /// <summary>
    /// A <see cref="FrozenDictionary{TKey, TValue}"/> reaches the comparison through
    /// <c>IReadOnlyDictionary</c>. It used to fall through to the collection path, where a wrong
    /// value under a matching key was reported as "no equivalent item was found".
    /// </summary>
    [Fact]
    public void AGenericOnlyDictionaryIsComparedByKey()
    {
        var subject = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }.ToFrozenDictionary();
        var expectation = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }.ToFrozenDictionary();

        subject.Should().BeEquivalentTo(expectation);

        var different = new Dictionary<string, int> { ["a"] = 1, ["b"] = 99 }.ToFrozenDictionary();
        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(different));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("[b]", ex!.Message);
    }

    [Fact]
    public void AGenericOnlyDictionaryReportsAMissingKeyByName()
    {
        var subject = new Dictionary<string, int> { ["a"] = 1 }.ToFrozenDictionary();
        var expectation = new Dictionary<string, int> { ["a"] = 1, ["missing"] = 2 }.ToFrozenDictionary();

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));

        Assert.Contains("missing", ex!.Message);
    }

    /// <summary>
    /// The reason the <see cref="System.Collections.IDictionary"/> path was kept rather than folded
    /// into the generic one: a dictionary's own key comparer has to keep applying.
    /// </summary>
    [Fact]
    public void ADictionaryKeepsItsOwnKeyComparer()
    {
        var subject = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Key"] = 1 };
        var expectation = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["KEY"] = 1 };

        subject.Should().BeEquivalentTo(expectation);
    }

    #endregion
}
