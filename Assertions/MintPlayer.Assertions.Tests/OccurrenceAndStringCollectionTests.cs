namespace MintPlayer.Assertions.Tests;

/// <summary>
/// Occurrence constraints, the string-collection surface, and the numeric <c>Not*</c> mirrors.
/// </summary>
public class OccurrenceAndStringCollectionTests
{
    // -------------------------------------------------------------------------------------------
    // OccurrenceConstraint
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void ExactlyCountsMatches()
    {
        int[] subject = [1, 2, 2, 3];

        subject.Should().Contain(2, Exactly.Twice());
        subject.Should().Contain(1, Exactly.Once());

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().Contain(2, Exactly.Thrice())));
    }

    [Fact]
    public void AtLeastAtMostMoreThanLessThan()
    {
        int[] subject = [1, 2, 2, 3];

        subject.Should().Contain(2, AtLeast.Twice());
        subject.Should().Contain(2, AtMost.Twice());
        subject.Should().Contain(2, MoreThan.Once());
        subject.Should().Contain(2, LessThan.Thrice());

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().Contain(2, MoreThan.Twice())));
    }

    [Fact]
    public void AnAbsentItemIsZeroOccurrences()
    {
        int[] subject = [1];

        subject.Should().Contain(9, Exactly.Times(0));
        subject.Should().Contain(9, AtMost.Once());

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().Contain(9, AtLeast.Once())));
    }

    [Fact]
    public void PredicateOccurrences()
    {
        int[] subject = [1, 2, 3, 4];

        subject.Should().Contain(x => x % 2 == 0, Exactly.Twice());
        subject.Should().HaveCount(AtLeast.Times(4));
    }

    /// <summary>
    /// The default value is not a constraint. Treating it as "exactly zero" would turn a caller's
    /// mistake into an assertion that quietly passes only when the item is absent.
    /// </summary>
    /// <remarks>
    /// ⚠️ The type argument on <c>default</c> is not decoration. A bare <c>Contain(1, default)</c>
    /// binds to the <c>because</c> overload, not this one — <c>default</c> converts to both
    /// <c>string?</c> and <c>OccurrenceConstraint</c>, and the reference conversion wins. That is the
    /// naming trap running in reverse, and the only reason it is harmless here is that nobody writes
    /// a bare <c>default</c> on purpose. It is pinned so the resolution cannot change unnoticed.
    /// </remarks>
    [Fact]
    public void ADefaultConstraintIsRejected()
    {
        int[] subject = [1];

        Assert.Throws<InvalidOperationException>(() => subject.Should().Contain(1, default(OccurrenceConstraint)));

        // And the bare form really does bind elsewhere: this passes, because it means because: null.
        subject.Should().Contain(1, default);
    }

    [Fact]
    public void ANegativeCountIsRejectedAtTheBuilder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Exactly.Times(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtLeast.Times(-5));
    }

    [Fact]
    public void TheFailureMessageReadsAsAPhrase()
    {
        int[] subject = [1];

        var ex = Record.Exception(() => subject.Should().Contain(1, Exactly.Twice()));

        Assert.NotNull(ex);
        Assert.Contains("exactly 2 times", ex.Message);
        Assert.Contains("found it 1 time(s)", ex.Message);
    }

    [Fact]
    public void OneOccurrenceIsSingular()
    {
        Assert.Equal("exactly 1 time", Exactly.Once().ToString());
        Assert.Equal("at least 2 times", AtLeast.Twice().ToString());
    }

    // -------------------------------------------------------------------------------------------
    // String occurrences
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void StringOccurrencesAreCountedNonOverlapping()
    {
        // "aaa" contains "aa" ONCE under non-overlapping counting, not twice. This is the case the
        // two readings disagree on, so it is the one worth pinning.
        "aaa".Should().Contain("aa", Exactly.Once());
        "abab".Should().Contain("ab", Exactly.Twice());
    }

    [Fact]
    public void StringOccurrencesCanIgnoreCase()
    {
        "aAbA".Should().ContainEquivalentOf("a", Exactly.Thrice());
    }

    [Fact]
    public void ANullStringHasNoOccurrences()
    {
        string? subject = null;

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().Contain("a", AtLeast.Once())));
    }

    // -------------------------------------------------------------------------------------------
    // String collections
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void ContainMatchUsesWildcards()
    {
        string[] subject = ["alpha", "beta", "gamma"];

        subject.Should().ContainMatch("al*").And.HaveCount(3);
        subject.Should().NotContainMatch("z*");
    }

    /// <summary>An extension on the array overload and on the interface overload alike — the reason these are extensions.</summary>
    [Fact]
    public void TheStringSurfaceReachesArraysAndListsEqually()
    {
        string[] asArray = ["alpha"];
        List<string> asList = ["alpha"];
        IEnumerable<string> asSequence = asList;

        asArray.Should().ContainMatch("al*");
        asList.Should().ContainMatch("al*");
        asSequence.Should().ContainMatch("al*");
    }

    [Fact]
    public void ContainMatchTakesAnOccurrence()
    {
        string[] subject = ["a1", "a2", "b1"];

        subject.Should().ContainMatch("a*", Exactly.Twice());
    }

    [Fact]
    public void ContainEquivalentOfIgnoresCase()
    {
        string[] subject = ["Alpha"];

        subject.Should().ContainEquivalentOf("alpha");

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().ContainEquivalentOf("beta")));
    }

    /// <summary>Distinct from NotContainNulls: a blank string is as wrong as a missing one here.</summary>
    [Fact]
    public void NotContainNullsOrWhiteSpaceRejectsBlanks()
    {
        string[] clean = ["a", "b"];
        string[] blank = ["a", "   "];

        clean.Should().NotContainNullsOrWhiteSpace();
        clean.Should().NotContainNulls();

        blank.Should().NotContainNulls();
        var ex = Record.Exception(() => blank.Should().NotContainNullsOrWhiteSpace());

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("index(es)", ex.Message);
    }

    [Fact]
    public void AllStartWith()
    {
        string[] subject = ["pre-a", "pre-b"];

        subject.Should().AllStartWith("pre-");

        Assert.IsType<AssertionFailedException>(
            Record.Exception(() => subject.Should().AllStartWith("x")));
    }

    // -------------------------------------------------------------------------------------------
    // Numeric Not* mirrors
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void NumericMirrorsNegateTheComparison()
    {
        5.Should().NotBeGreaterThan(5);
        5.Should().NotBeGreaterThanOrEqualTo(6);
        5.Should().NotBeLessThan(5);
        5.Should().NotBeLessThanOrEqualTo(4);

        Assert.IsType<AssertionFailedException>(Record.Exception(() => 5.Should().NotBeGreaterThan(4)));
        Assert.IsType<AssertionFailedException>(Record.Exception(() => 5.Should().NotBeLessThan(6)));
    }

    /// <summary>
    /// The reason these are not aliases of BeLessThanOrEqualTo and friends. A null subject is not
    /// greater than anything, so the negative form passes — while the positive form fails, because a
    /// value that does not exist is not less than anything either.
    /// </summary>
    [Fact]
    public void TheNullSubjectIsWhereTheMirrorsDifferFromTheirCounterparts()
    {
        int? subject = null;

        subject.Should().NotBeGreaterThan(3);
        subject.Should().NotBeLessThan(3);

        Assert.IsType<AssertionFailedException>(Record.Exception(() => subject.Should().BeLessThanOrEqualTo(3)));
        Assert.IsType<AssertionFailedException>(Record.Exception(() => subject.Should().BeGreaterThanOrEqualTo(3)));
    }
}
