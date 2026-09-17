namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The inspection surface on <see cref="AssertionScope"/>: reading collected failures, discarding
/// them, adding one verbatim, and attaching context that only a failure pays for.
/// </summary>
public class AssertionScopeInspectionTests
{
    [Fact]
    public void FailuresExposesWhatHasBeenCollectedSoFar()
    {
        using var scope = new AssertionScope();

        1.Should().Be(2);
        3.Should().Be(4);

        Assert.Equal(2, scope.Failures.Count);
        Assert.Contains("to be 2", scope.Failures[0]);

        scope.Discard();
    }

    /// <summary>
    /// The snapshot must not be the live list — handing out the collection that is about to be
    /// thrown would let a caller mutate the failure report by accident.
    /// </summary>
    [Fact]
    public void FailuresIsASnapshotNotAView()
    {
        using var scope = new AssertionScope();

        1.Should().Be(2);
        var snapshot = scope.Failures;
        3.Should().Be(4);

        Assert.Single(snapshot);
        Assert.Equal(2, scope.Failures.Count);

        scope.Discard();
    }

    [Fact]
    public void DiscardReturnsTheFailuresAndLeavesTheScopeClean()
    {
        var scope = new AssertionScope();
        1.Should().Be(2);

        var discarded = scope.Discard();

        Assert.Single(discarded);
        Assert.False(scope.HasFailures);

        // The whole point: disposing now throws nothing.
        scope.Dispose();
    }

    [Fact]
    public void AddPreFormattedFailureIsReportedVerbatim()
    {
        var scope = new AssertionScope();
        scope.AddPreFormattedFailure("a message with {0} braces {not} substituted");

        var ex = Assert.Throws<AssertionFailedException>(scope.Dispose);

        Assert.Contains("a message with {0} braces {not} substituted", ex.Message);
    }

    [Fact]
    public void AReportableIsAppendedToTheFailureReport()
    {
        var scope = new AssertionScope();
        scope.AddReportable("correlationId", "abc-123");
        1.Should().Be(2);

        var ex = Assert.Throws<AssertionFailedException>(scope.Dispose);

        Assert.Contains("correlationId: abc-123", ex.Message);
    }

    /// <summary>
    /// The reason the API takes a factory: a passing scope must not pay to build text nobody reads.
    /// </summary>
    [Fact]
    public void AReportableFactoryIsNotCalledWhenNothingFails()
    {
        var called = false;

        using (var scope = new AssertionScope())
        {
            scope.AddReportable("expensive", () => { called = true; return "value"; });
            1.Should().Be(1);
        }

        Assert.False(called);
    }

    [Fact]
    public void AReportableFactoryIsCalledOnceWhenSomethingFails()
    {
        var calls = 0;
        var scope = new AssertionScope();
        scope.AddReportable("expensive", () => { calls++; return "value"; });
        1.Should().Be(2);

        Assert.Throws<AssertionFailedException>(scope.Dispose);

        Assert.Equal(1, calls);
    }

    /// <summary>
    /// A diagnostic that throws must not replace the failure it was there to explain.
    /// </summary>
    [Fact]
    public void AThrowingReportableDoesNotHideTheFailure()
    {
        var scope = new AssertionScope();
        scope.AddReportable("broken", () => throw new InvalidOperationException("boom"));
        1.Should().Be(2);

        var ex = Assert.Throws<AssertionFailedException>(scope.Dispose);

        Assert.Contains("to be 2", ex.Message);
        Assert.Contains("threw InvalidOperationException", ex.Message);
    }

    /// <summary>
    /// The outermost scope is the one that throws, so context attached inside a nested scope has to
    /// travel up with the failures or it is lost exactly when it becomes useful.
    /// </summary>
    [Fact]
    public void AReportableAttachedInsideANestedScopeStillReaches()
    {
        var outer = new AssertionScope("outer");

        using (var inner = new AssertionScope("inner"))
        {
            inner.AddReportable("seed", "42");
            1.Should().Be(2);
        }

        var ex = Assert.Throws<AssertionFailedException>(outer.Dispose);

        Assert.Contains("seed: 42", ex.Message);
        Assert.Contains("[inner]", ex.Message);
    }

    [Fact]
    public void DiscardingANestedScopeKeepsItsFailuresOutOfTheParent()
    {
        using var outer = new AssertionScope();

        using (var inner = new AssertionScope())
        {
            1.Should().Be(2);
            inner.Discard();
        }

        Assert.False(outer.HasFailures);
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        using var scope = new AssertionScope();

        Assert.Throws<ArgumentNullException>(() => scope.AddPreFormattedFailure(null!));
        Assert.Throws<ArgumentNullException>(() => scope.AddReportable(null!, "v"));
        Assert.Throws<ArgumentNullException>(() => scope.AddReportable("n", (string)null!));
        Assert.Throws<ArgumentNullException>(() => scope.AddReportable("n", (Func<string>)null!));
    }
}
