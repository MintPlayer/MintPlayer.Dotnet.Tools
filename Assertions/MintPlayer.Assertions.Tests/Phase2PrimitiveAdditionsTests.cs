namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The primitive/scalar surface added in Phase 2 (PRD §3). Covers the assertions that previously
/// did not exist or did not compile, and pins the two that carry real semantics rather than sugar:
/// <c>DateTimeOffset.BeExactly</c> and the null-tolerant boolean negatives.
/// </summary>
public class Phase2PrimitiveAdditionsTests
{
    private static readonly DateTime Noon = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Midnight = new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);

    #region Temporal negatives

    [Fact]
    public void NotBeBefore_Passes_WhenEqualOrLater()
    {
        Noon.Should().NotBeBefore(Noon);
        Noon.Should().NotBeBefore(Midnight);
    }

    [Fact]
    public void NotBeBefore_Fails_WhenEarlier()
    {
        var ex = Record.Exception(() => Midnight.Should().NotBeBefore(Noon));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("Did not expect", ex.Message);
    }

    [Fact]
    public void NotBeAfter_Passes_WhenEqualOrEarlier()
    {
        Noon.Should().NotBeAfter(Noon);
        Midnight.Should().NotBeAfter(Noon);
    }

    [Fact]
    public void NotBeOnOrBefore_Fails_WhenEqual()
    {
        var ex = Record.Exception(() => Noon.Should().NotBeOnOrBefore(Noon));
        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void NotBeOnOrAfter_Fails_WhenEqual()
    {
        var ex = Record.Exception(() => Noon.Should().NotBeOnOrAfter(Noon));
        Assert.IsType<AssertionFailedException>(ex);
    }

    /// <summary>
    /// A null subject satisfies every negative — the library's documented rule, and the reason these
    /// are not merely inverted positives.
    /// </summary>
    [Fact]
    public void TheTemporalNegatives_Pass_ForANullSubject()
    {
        DateTime? nothing = null;
        nothing.Should().NotBeBefore(Noon);
        nothing.Should().NotBeOnOrBefore(Noon);
        nothing.Should().NotBeAfter(Noon);
        nothing.Should().NotBeOnOrAfter(Noon);
    }

    [Fact]
    public void DateOnlyAndTimeOnly_HaveTheNegativesToo()
    {
        var day = new DateOnly(2026, 9, 15);
        day.Should().NotBeBefore(day).And.NotBeAfter(day);

        var time = new TimeOnly(12, 0);
        time.Should().NotBeBefore(time).And.NotBeAfter(time);
    }

    #endregion

    #region Millisecond components

    [Fact]
    public void HaveMillisecond_ComparesTheComponent()
    {
        var subject = new DateTime(2026, 9, 15, 12, 0, 0, 250, DateTimeKind.Utc);
        subject.Should().HaveMillisecond(250);

        var ex = Record.Exception(() => subject.Should().HaveMillisecond(0));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("millisecond", ex.Message);
    }

    [Fact]
    public void NotHaveMillisecond_PassesForANullSubject()
    {
        DateTime? nothing = null;
        nothing.Should().NotHaveMillisecond(250);
    }

    [Fact]
    public void TimeOnly_HasTheNegativeComponents()
    {
        var time = new TimeOnly(12, 30, 15, 250);
        time.Should().NotHaveHours(13).And.NotHaveMinutes(0).And.NotHaveSeconds(0).And.NotHaveMilliseconds(0);
    }

    #endregion

    #region DateTimeOffset.BeExactly

    /// <summary>
    /// The assertion Phase 2 added because it could not be expressed at all: <c>Be</c> compares
    /// instants, so two values an hour apart with a one-hour offset difference are equal to it.
    /// </summary>
    [Fact]
    public void BeExactly_SeparatesValuesThatBeConsidersEqual()
    {
        var utc = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
        var sameInstantElsewhere = new DateTimeOffset(2026, 9, 15, 13, 0, 0, TimeSpan.FromHours(1));

        // Same moment: Be is satisfied.
        utc.Should().Be(sameInstantElsewhere);

        // Different offset: BeExactly is not.
        var ex = Record.Exception(() => utc.Should().BeExactly(sameInstantElsewhere));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("same offset", ex.Message);

        utc.Should().BeExactly(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        utc.Should().NotBeExactly(sameInstantElsewhere);
    }

    #endregion

    #region BeNull / NotBeNull on nullable value types

    [Fact]
    public void BeNull_WorksOnEveryNullableValueFamily()
    {
        ((int?)null).Should().BeNull();
        ((DateTime?)null).Should().BeNull();
        ((DateTimeOffset?)null).Should().BeNull();
        ((DateOnly?)null).Should().BeNull();
        ((TimeOnly?)null).Should().BeNull();
        ((TimeSpan?)null).Should().BeNull();
        ((Guid?)null).Should().BeNull();
        ((bool?)null).Should().BeNull();
        ((DayOfWeek?)null).Should().BeNull();
    }

    [Fact]
    public void NotBeNull_ExposesTheUnwrappedValueViaWhich()
    {
        int? value = 42;
        value.Should().NotBeNull().Which.Should().Be(42);

        Guid? id = Guid.NewGuid();
        id.Should().NotBeNull().Which.Should().NotBeEmpty();
    }

    [Fact]
    public void BeNull_Fails_WhenThereIsAValue()
    {
        var ex = Record.Exception(() => ((int?)42).Should().BeNull());
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be <null>", ex.Message);
        Assert.Contains("42", ex.Message);
    }

    /// <summary>
    /// Inside a scope the failure is collected rather than thrown, so <c>NotBeNull</c> keeps running
    /// with no value. It must not throw an unrelated InvalidOperationException that masks the real
    /// failure — hence GetValueOrDefault rather than .Value.
    /// </summary>
    [Fact]
    public void NotBeNull_InsideAScope_ReportsTheFailureRatherThanThrowingOnValue()
    {
        var ex = Record.Exception(() =>
        {
            using var scope = new AssertionScope();
            ((int?)null).Should().NotBeNull();
        });

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("not to be <null>", ex.Message);
    }

    #endregion

    #region Boolean additions

    /// <summary>
    /// <c>NotBeTrue</c> is not <c>BeFalse</c>: null satisfies the former and fails the latter. Only
    /// this pair can express "definitely not true" for a tri-state flag.
    /// </summary>
    [Fact]
    public void NotBeTrue_IsSatisfiedByNull_WhereBeFalseIsNot()
    {
        bool? nothing = null;
        nothing.Should().NotBeTrue();
        nothing.Should().NotBeFalse();

        Assert.IsType<AssertionFailedException>(Record.Exception(() => nothing.Should().BeFalse()));
    }

    [Fact]
    public void NotBeTrue_Fails_WhenTrue()
        => Assert.IsType<AssertionFailedException>(Record.Exception(() => true.Should().NotBeTrue()));

    [Fact]
    public void Imply_IsSatisfiedWheneverTheSubjectIsNotTrue()
    {
        false.Should().Imply(false);
        ((bool?)null).Should().Imply(false);
        true.Should().Imply(true);

        Assert.IsType<AssertionFailedException>(Record.Exception(() => true.Should().Imply(false)));
    }

    #endregion

    #region BeOneOf

    [Fact]
    public void BeOneOf_OnStrings()
    {
        "green".Should().BeOneOf("red", "green", "blue");
        "green".Should().NotBeOneOf("red", "blue");

        var ex = Record.Exception(() => "purple".Should().BeOneOf("red", "green", "blue"));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("to be one of", ex.Message);
    }

    [Fact]
    public void BeOneOf_OnStrings_IsOrdinal()
        => Assert.IsType<AssertionFailedException>(Record.Exception(() => "GREEN".Should().BeOneOf("green")));

    [Fact]
    public void BeOneOf_OnObjects()
    {
        object subject = 42;
        subject.Should().BeOneOf(1, 42, 99);
        subject.Should().NotBeOneOf(1, 99);
    }

    [Fact]
    public void BeOneOf_OnComparables()
    {
        var version = new Version(2, 0);
        version.Should().BeOneOf(new Version(1, 0), new Version(2, 0));
        version.Should().NotBeOneOf(new Version(3, 0));
    }

    [Fact]
    public void BeOneOf_OnTimeOnly()
    {
        var time = new TimeOnly(12, 0);
        time.Should().BeOneOf(new TimeOnly(9, 0), new TimeOnly(12, 0));
        time.Should().NotBeOneOf(new TimeOnly(9, 0));
    }

    /// <summary>An empty exclusion set passes: there is nothing for the subject to be one of.</summary>
    [Fact]
    public void NotBeOneOf_PassesForAnEmptySet()
        => "anything".Should().NotBeOneOf();

    #endregion

    #region Rank equality

    [Fact]
    public void BeRankedEquallyTo_UsesCompareTo()
    {
        var version = new Version(2, 0);
        version.Should().BeRankedEquallyTo(new Version(2, 0));
        version.Should().NotBeRankedEquallyTo(new Version(3, 0));

        var ex = Record.Exception(() => version.Should().BeRankedEquallyTo(new Version(3, 0)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("rank equally", ex.Message);
    }

    #endregion
}
