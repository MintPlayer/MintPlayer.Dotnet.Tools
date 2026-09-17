namespace MintPlayer.Assertions.Tests;

[AssertEquivalency]
public class OptionPoco
{
    public string Name { get; set; } = string.Empty;
    public int Size { get; set; }
    public OptionNested? Nested { get; set; }
    public int[] Items { get; set; } = [];
}

[AssertEquivalency]
public class OptionNested
{
    public string Label { get; set; } = string.Empty;
    public int Ignored { get; set; }
}

public enum Colour { Red = 1, Green = 2 }

public enum Shade { Red = 7, Green = 8 }

/// <summary>The remaining M5c equivalency options.</summary>
public class EquivalencyOptionsM5cTests
{
    // -------------------------------------------------------------------------------------------
    // Ordering
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void WithoutStrictOrderingIsTheDefaultAndCanBeStatedOutLoud()
    {
        var subject = new OptionPoco { Items = [1, 2, 3] };
        var expectation = new OptionPoco { Items = [3, 2, 1] };

        subject.Should().BeEquivalentTo(expectation);
        subject.Should().BeEquivalentTo(expectation, o => o.WithoutStrictOrdering());
        subject.Should().BeEquivalentTo(expectation, o => o.WithStrictOrdering().WithoutStrictOrdering());
    }

    /// <summary>
    /// The point of the per-path form: one collection in the graph is genuinely ordered and the
    /// others are not. Ordering the whole graph to say that makes every other collection assert
    /// something the code does not guarantee.
    /// </summary>
    [Fact]
    public void StrictOrderingCanBeScopedToAPath()
    {
        var subject = new OptionPoco { Items = [1, 2, 3] };
        var expectation = new OptionPoco { Items = [3, 2, 1] };

        subject.Should().BeEquivalentTo(expectation, o => o.WithStrictOrderingFor("Other"));

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.WithStrictOrderingFor("Items")));
        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void AnEmptyOrderingPathIsRejected()
    {
        var subject = new OptionPoco();
        Assert.Throws<ArgumentException>(() => subject.Should().BeEquivalentTo(subject, o => o.WithStrictOrderingFor("  ")));
    }

    // -------------------------------------------------------------------------------------------
    // Missing members
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void MissingMembersAreReportedByDefaultAndCanBeIgnored()
    {
        var subject = new { Name = "a" };
        var expectation = new { Name = "a", Extra = 1 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("but subject does not", ex.Message);

        subject.Should().BeEquivalentTo(expectation, o => o.ExcludingMissingMembers());
    }

    /// <summary>
    /// A skipped member has not been compared, so a comparison where every member was skipped has
    /// asserted nothing — and the vacuity check still refuses it rather than passing.
    /// </summary>
    [Fact]
    public void IgnoringEveryMemberIsStillVacuous()
    {
        var subject = new { Unrelated = 1 };
        var expectation = new { OnlyThis = 2 };

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(expectation, o => o.ExcludingMissingMembers()));

        Assert.IsType<InvalidOperationException>(ex);
    }

    // -------------------------------------------------------------------------------------------
    // Depth
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void WithoutRecursingComparesTheTopLevelOnly()
    {
        var subject = new OptionPoco { Name = "a", Nested = new() { Label = "x" } };
        var expectation = new OptionPoco { Name = "a", Nested = new() { Label = "DIFFERENT" } };

        subject.Should().BeEquivalentTo(expectation, o => o.WithoutRecursing());

        Assert.IsType<AssertionFailedException>(Record.Exception(() => subject.Should().BeEquivalentTo(expectation)));
    }

    // -------------------------------------------------------------------------------------------
    // Nested inclusions
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void IncludingNestedRestrictsTheTypeToTheSelectedMembers()
    {
        var subject = new OptionPoco { Name = "a", Nested = new() { Label = "x", Ignored = 1 } };
        var expectation = new OptionPoco { Name = "a", Nested = new() { Label = "x", Ignored = 999 } };

        subject.Should().BeEquivalentTo(expectation, o => o.IncludingNested<OptionNested>(n => n.Label));

        Assert.IsType<AssertionFailedException>(Record.Exception(() => subject.Should().BeEquivalentTo(expectation)));
    }

    [Fact]
    public void IncludingNestedCanNameSeveralMembers()
    {
        var subject = new OptionPoco { Nested = new() { Label = "x", Ignored = 1 } };
        var expectation = new OptionPoco { Nested = new() { Label = "x", Ignored = 1 } };

        subject.Should().BeEquivalentTo(expectation,
            o => o.IncludingNested<OptionNested>(n => n.Label).IncludingNested<OptionNested>(n => n.Ignored));
    }

    /// <summary>Excluding something you also included is a contradiction; the exclusion is the more specific intent.</summary>
    [Fact]
    public void AnExclusionBeatsAnInclusion()
    {
        var subject = new OptionPoco { Name = "a", Nested = new() { Label = "x" } };
        var expectation = new OptionPoco { Name = "a", Nested = new() { Label = "DIFFERENT" } };

        subject.Should().BeEquivalentTo(expectation,
            o => o.IncludingNested<OptionNested>(n => n.Label).ExcludingNested<OptionNested>(n => n.Label));
    }

    // -------------------------------------------------------------------------------------------
    // Enums
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The default is neither by-name nor by-value: two different enum types are never equal under
    /// Equals, whatever they contain. Picking one is a deliberate act.
    /// </summary>
    [Fact]
    public void DifferentEnumTypesAreNotEquivalentByDefault()
    {
        var subject = new { Value = (object)Colour.Red };
        var expectation = new { Value = (object)Shade.Red };

        Assert.IsType<AssertionFailedException>(Record.Exception(() => subject.Should().BeEquivalentTo(expectation)));
    }

    [Fact]
    public void ComparingEnumsByName()
    {
        var subject = new { Value = (object)Colour.Red };
        var expectation = new { Value = (object)Shade.Red };

        subject.Should().BeEquivalentTo(expectation, o => o.ComparingEnumsByName());

        var mismatched = new { Value = (object)Shade.Green };
        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeEquivalentTo(mismatched, o => o.ComparingEnumsByName())));
    }

    [Fact]
    public void ComparingEnumsByValue()
    {
        var subject = new { Value = (object)Colour.Green };   // 2
        var expectation = new { Value = (object)(Shade)2 };   // numerically equal, different type

        subject.Should().BeEquivalentTo(expectation, o => o.ComparingEnumsByValue());
    }

    [Fact]
    public void TheTwoEnumModesAreMutuallyExclusive()
    {
        var subject = new { Value = (object)Colour.Red };     // Red = 1
        var expectation = new { Value = (object)Shade.Red };  // Red = 7

        // The last call wins, so this is by-value and the numbers differ.
        Assert.IsType<AssertionFailedException>(Record.Exception(
            () => subject.Should().BeEquivalentTo(expectation, o => o.ComparingEnumsByName().ComparingEnumsByValue())));

        // And the other way round it is by-name, which matches.
        subject.Should().BeEquivalentTo(expectation, o => o.ComparingEnumsByValue().ComparingEnumsByName());
    }

    // -------------------------------------------------------------------------------------------
    // Strings and typing
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void ComparingStringsIgnoringCase()
    {
        var subject = new OptionPoco { Name = "Alpha" };
        var expectation = new OptionPoco { Name = "alpha" };

        subject.Should().BeEquivalentTo(expectation, o => o.ComparingStringsIgnoringCase());

        Assert.IsType<AssertionFailedException>(Record.Exception(() => subject.Should().BeEquivalentTo(expectation)));
    }

    /// <summary>
    /// Structural comparison ignoring the types is what makes comparing against an anonymous object
    /// work, and is the default. This rules it out when that is the point.
    /// </summary>
    [Fact]
    public void WithStrictTypingRequiresTheSameRuntimeType()
    {
        var subject = new OptionPoco { Name = "a" };

        subject.Should().BeEquivalentTo(new { Name = "a" });

        var ex = Record.Exception(() => subject.Should().BeEquivalentTo(new { Name = "a" }, o => o.WithStrictTyping()));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("expected type", ex.Message);
    }

    [Fact]
    public void WithStrictTypingPassesForMatchingTypes()
    {
        var subject = new OptionPoco { Name = "a" };
        var expectation = new OptionPoco { Name = "a" };

        subject.Should().BeEquivalentTo(expectation, o => o.WithStrictTyping());
    }

    // -------------------------------------------------------------------------------------------
    // Comparer overload
    // -------------------------------------------------------------------------------------------

    private sealed class LengthComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => x?.Length == y?.Length;

        public int GetHashCode(string obj) => obj.Length;
    }

    [Fact]
    public void UsingAComparer()
    {
        var subject = new OptionPoco { Name = "abc" };
        var expectation = new OptionPoco { Name = "xyz" };

        subject.Should().BeEquivalentTo(expectation, o => o.Using<string>(new LengthComparer()));

        var longer = new OptionPoco { Name = "wxyz" };
        Assert.IsType<AssertionFailedException>(Record.Exception(
            () => subject.Should().BeEquivalentTo(longer, o => o.Using<string>(new LengthComparer()))));
    }

    [Fact]
    public void ANullComparerIsRejected()
    {
        var subject = new OptionPoco();
        Assert.Throws<ArgumentNullException>(
            () => subject.Should().BeEquivalentTo(subject, o => o.Using<string>((IEqualityComparer<string>)null!)));
    }
}
