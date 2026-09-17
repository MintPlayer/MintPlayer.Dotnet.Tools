namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The per-primitive <c>Not*</c> mirrors and the collection comparer overloads.
/// </summary>
/// <remarks>
/// The null subject is asserted for every mirror, because that is the ONLY thing separating these
/// from their positive counterparts — and an implementation written as a call to the counterpart
/// would pass every other test in this file.
/// </remarks>
public class PrimitiveMirrorsAndComparersTests
{
    // -------------------------------------------------------------------------------------------
    // TimeSpan
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void TimeSpanMirrors()
    {
        var value = TimeSpan.FromSeconds(5);

        value.Should().NotBeGreaterThan(TimeSpan.FromSeconds(5));
        value.Should().NotBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(6));
        value.Should().NotBeLessThan(TimeSpan.FromSeconds(5));
        value.Should().NotBeLessThanOrEqualTo(TimeSpan.FromSeconds(4));

        Assert.IsType<AssertionFailedException>(Record.Exception(() => value.Should().NotBeGreaterThan(TimeSpan.FromSeconds(4))));
        Assert.IsType<AssertionFailedException>(Record.Exception(() => value.Should().NotBeLessThan(TimeSpan.FromSeconds(6))));
    }

    [Fact]
    public void ANullTimeSpanPassesEveryNegativeComparison()
    {
        TimeSpan? value = null;

        value.Should().NotBeGreaterThan(TimeSpan.Zero);
        value.Should().NotBeLessThan(TimeSpan.Zero);
        value.Should().NotBeGreaterThanOrEqualTo(TimeSpan.Zero);
        value.Should().NotBeLessThanOrEqualTo(TimeSpan.Zero);

        // ...while the positive counterpart fails on the same subject. That difference is the reason
        // these exist as their own methods rather than as aliases.
        Assert.IsType<AssertionFailedException>(Record.Exception(() => value.Should().BeLessThanOrEqualTo(TimeSpan.Zero)));
    }

    // -------------------------------------------------------------------------------------------
    // Dates and times
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void DateTimeMirrors()
    {
        var value = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var earlier = value.AddDays(-1);
        var later = value.AddDays(1);

        value.Should().NotBeBefore(value);
        value.Should().NotBeBefore(earlier);
        value.Should().NotBeOnOrBefore(earlier);
        value.Should().NotBeAfter(value);
        value.Should().NotBeAfter(later);
        value.Should().NotBeOnOrAfter(later);

        Assert.IsType<AssertionFailedException>(Record.Exception(() => value.Should().NotBeBefore(later)));
        Assert.IsType<AssertionFailedException>(Record.Exception(() => value.Should().NotBeAfter(earlier)));
    }

    [Fact]
    public void ANullDateTimePassesEveryNegativeComparison()
    {
        DateTime? value = null;
        var moment = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

        value.Should().NotBeBefore(moment);
        value.Should().NotBeAfter(moment);
        value.Should().NotBeOnOrBefore(moment);
        value.Should().NotBeOnOrAfter(moment);

        Assert.IsType<AssertionFailedException>(Record.Exception(() => value.Should().BeOnOrAfter(moment)));
    }

    [Fact]
    public void DateTimeOffsetDateOnlyAndTimeOnlyMirrors()
    {
        var offset = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
        offset.Should().NotBeBefore(offset).And.NotBeAfter(offset);

        var date = new DateOnly(2026, 6, 15);
        date.Should().NotBeBefore(date).And.NotBeAfter(date);

        var time = new TimeOnly(12, 0);
        time.Should().NotBeBefore(time).And.NotBeAfter(time);
    }

    // -------------------------------------------------------------------------------------------
    // Comparable
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void ComparableMirrors()
    {
        IComparable<int> value = 5;

        value.Should().NotBeGreaterThan(5);
        value.Should().NotBeLessThan(5);
        value.Should().NotBeGreaterThanOrEqualTo(6);
        value.Should().NotBeLessThanOrEqualTo(4);

        Assert.IsType<AssertionFailedException>(Record.Exception(() => value.Should().NotBeGreaterThan(4)));
    }

    [Fact]
    public void ANullComparablePassesEveryNegativeComparison()
    {
        IComparable<int>? value = null;

        value.Should().NotBeGreaterThan(3);
        value.Should().NotBeLessThan(3);
    }

    // -------------------------------------------------------------------------------------------
    // Boolean
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Three states, three answers. A null bool? is neither true nor false, so BOTH negatives pass on
    /// it — which is exactly why NotBeTrue is not BeFalse.
    /// </summary>
    [Fact]
    public void BooleanMirrorsAreNotTheOppositeAssertion()
    {
        bool? nothing = null;

        nothing.Should().NotBeTrue();
        nothing.Should().NotBeFalse();

        Assert.IsType<AssertionFailedException>(Record.Exception(() => nothing.Should().BeFalse()));
        Assert.IsType<AssertionFailedException>(Record.Exception(() => nothing.Should().BeTrue()));

        true.Should().NotBeFalse();
        false.Should().NotBeTrue();
        Assert.IsType<AssertionFailedException>(Record.Exception(() => true.Should().NotBeTrue()));
    }

    // -------------------------------------------------------------------------------------------
    // Collection comparer overloads
    // -------------------------------------------------------------------------------------------

    private sealed class IgnoreCase : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(string obj) => obj.ToUpperInvariant().GetHashCode();
    }

    [Fact]
    public void ContainAndNotContainWithAComparer()
    {
        string[] subject = ["Alpha", "Beta"];

        subject.Should().Contain("alpha", new IgnoreCase());
        subject.Should().NotContain("gamma", new IgnoreCase());

        // The default comparer disagrees, which is the whole point of the overload.
        Assert.IsType<AssertionFailedException>(Record.Exception(() => subject.Should().Contain("alpha")));
    }

    [Fact]
    public void EqualWithAComparer()
    {
        string[] subject = ["Alpha", "Beta"];

        subject.Should().Equal(["alpha", "beta"], new IgnoreCase());

        var ex = Record.Exception(() => subject.Should().Equal(["alpha", "gamma"], new IgnoreCase()));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("differ at index 1", ex.Message);
    }

    [Fact]
    public void EqualWithAComparerReportsALengthMismatchFirst()
    {
        string[] subject = ["Alpha"];

        var ex = Record.Exception(() => subject.Should().Equal(["alpha", "beta"], new IgnoreCase()));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("1 item(s) against 2", ex.Message);
    }

    [Fact]
    public void BeSubsetOfWithAComparer()
    {
        string[] subject = ["Alpha"];

        subject.Should().BeSubsetOf(["alpha", "beta"], new IgnoreCase());

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().BeSubsetOf(["beta"], new IgnoreCase())));
    }

    [Fact]
    public void OnlyHaveUniqueItemsWithAComparer()
    {
        string[] distinct = ["Alpha", "Beta"];
        string[] duplicated = ["Alpha", "alpha"];

        distinct.Should().OnlyHaveUniqueItems(new IgnoreCase());
        duplicated.Should().OnlyHaveUniqueItems();

        var ex = Record.Exception(() => duplicated.Should().OnlyHaveUniqueItems(new IgnoreCase()));
        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void ANullComparerIsRejected()
    {
        string[] subject = ["a"];

        Assert.Throws<ArgumentNullException>(() => subject.Should().Contain("a", (IEqualityComparer<string>)null!));
        Assert.Throws<ArgumentNullException>(() => subject.Should().NotContain("a", (IEqualityComparer<string>)null!));
        Assert.Throws<ArgumentNullException>(() => subject.Should().Equal(["a"], (IEqualityComparer<string>)null!));
        Assert.Throws<ArgumentNullException>(() => subject.Should().BeSubsetOf(["a"], (IEqualityComparer<string>)null!));
        Assert.Throws<ArgumentNullException>(() => subject.Should().OnlyHaveUniqueItems((IEqualityComparer<string>)null!));
    }
}
