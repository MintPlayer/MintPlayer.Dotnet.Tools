namespace MintPlayer.Assertions.Tests;

/// <summary>
/// The exception/async surface added in Phase 2 (PRD §3), plus the AggregateException fix from §2.1.
/// </summary>
public class Phase2SpecializedAdditionsTests
{
    #region AggregateException extraction (M1.1)

    /// <summary>
    /// The defect this fixes: the action DID throw an ArgumentException, but it arrived wrapped, so
    /// a direct type test said no. Anything going through Task.Wait or Parallel.ForEach wraps.
    /// </summary>
    [Fact]
    public void Throw_SeesThroughAnAggregateException()
    {
        Action action = () => throw new AggregateException(new ArgumentException("inner boom"));

        action.Should().Throw<ArgumentException>().WithMessage("inner boom");
    }

    [Fact]
    public void Throw_SeesThroughNestedAggregateExceptions()
    {
        Action action = () => throw new AggregateException(
            new AggregateException(new InvalidOperationException("deep")));

        action.Should().Throw<InvalidOperationException>().Which.Message.Should().Be("deep");
    }

    /// <summary>Asking about the wrapper itself still matches the wrapper — it is not transparent both ways.</summary>
    [Fact]
    public void Throw_StillMatchesTheAggregateExceptionItself()
    {
        Action action = () => throw new AggregateException(new ArgumentException("inner"));

        action.Should().Throw<AggregateException>();
    }

    [Fact]
    public void Throw_StillFails_WhenNoInnerExceptionMatches()
    {
        Action action = () => throw new AggregateException(new ArgumentException("inner"));

        var ex = Record.Exception(() => action.Should().Throw<FormatException>());
        Assert.IsType<AssertionFailedException>(ex);
    }

    #endregion

    #region Exception surface

    [Fact]
    public void Throw_NonGeneric_AcceptsAnyException()
    {
        Action action = () => throw new FormatException("boom");
        action.Should().Throw().WithMessage("boom");
    }

    [Fact]
    public void And_IsAnAliasForWhich()
    {
        Action action = () => throw new ArgumentException("boom", "param");
        var assertions = action.Should().Throw<ArgumentException>();

        Assert.Same(assertions.Which, assertions.And);
    }

    [Fact]
    public void WithoutMessage_PassesWhenThePatternDoesNotMatch()
    {
        Action action = () => throw new InvalidOperationException("the widget jammed");

        action.Should().Throw<InvalidOperationException>().WithoutMessage("*exploded*");

        var ex = Record.Exception(() =>
            action.Should().Throw<InvalidOperationException>().WithoutMessage("*jammed*"));
        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void WithInnerException_AcceptsARuntimeType()
    {
        Action action = () => throw new InvalidOperationException("outer", new FormatException("inner"));

        action.Should().Throw<InvalidOperationException>().WithInnerException(typeof(FormatException));
        action.Should().Throw<InvalidOperationException>().WithInnerExceptionExactly(typeof(FormatException));

        var ex = Record.Exception(() =>
            action.Should().Throw<InvalidOperationException>().WithInnerException(typeof(ArgumentException)));
        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void NotThrow_OfASpecificType_LetsOtherExceptionsPast()
    {
        Action action = () => throw new FormatException("boom");

        action.Should().NotThrow<ArgumentException>();

        var ex = Record.Exception(() => action.Should().NotThrow<FormatException>());
        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public void NotThrowAfter_SucceedsOnceTheActionStopsThrowing()
    {
        var attempts = 0;
        Action action = () =>
        {
            if (++attempts < 3) throw new InvalidOperationException("not yet");
        };

        action.Should().NotThrowAfter(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10));
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void NotThrowAfter_Fails_WhenItNeverStops()
    {
        Action action = () => throw new InvalidOperationException("always");

        var ex = Record.Exception(() =>
            action.Should().NotThrowAfter(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10)));
        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("kept throwing", ex.Message);
    }

    #endregion

    #region Async surface

    [Fact]
    public async Task ThrowAsync_NonGeneric_AcceptsAnyException()
    {
        Func<Task> action = () => throw new FormatException("boom");
        await action.Should().ThrowAsync().WithMessage("boom");
    }

    [Fact]
    public async Task NotThrowAsync_OfASpecificType_LetsOtherExceptionsPast()
    {
        Func<Task> action = () => throw new FormatException("boom");
        await action.Should().NotThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ThrowWithinAsync_PassesWhenItFailsFast()
    {
        Func<Task> action = () => throw new InvalidOperationException("immediate");

        var assertions = await action.Should().ThrowWithinAsync<InvalidOperationException>(TimeSpan.FromSeconds(5));
        assertions.Which.Message.Should().Be("immediate");
    }

    [Fact]
    public async Task NotCompleteWithinAsync_PassesForATaskThatKeepsRunning()
    {
        Func<Task> action = () => Task.Delay(TimeSpan.FromSeconds(30));

        await action.Should().NotCompleteWithinAsync(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task NotCompleteWithinAsync_Fails_WhenItFinishes()
    {
        Func<Task> action = () => Task.CompletedTask;

        var ex = await Record.ExceptionAsync(() =>
            action.Should().NotCompleteWithinAsync(TimeSpan.FromSeconds(1)));
        Assert.IsType<AssertionFailedException>(ex);
    }

    [Fact]
    public async Task NotThrowAfterAsync_IsAvailableOnTheGenericVariant()
    {
        var attempts = 0;
        Func<Task<int>> action = () =>
        {
            if (++attempts < 3) throw new InvalidOperationException("not yet");
            return Task.FromResult(42);
        };

        var result = await action.Should().NotThrowAfterAsync(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10));
        result.Which.Should().Be(42);
    }

    #endregion

    #region ValueTask

    /// <summary>
    /// Before Phase 2 a Func&lt;ValueTask&gt; bound to FuncAssertions&lt;ValueTask&gt;, which can
    /// only observe the creation of the ValueTask — never the await. A test asserting a throw here
    /// passed for the wrong reason.
    /// </summary>
    [Fact]
    public async Task AValueTaskReturningFunction_CanAssertOnWhatAwaitingItThrows()
    {
        Func<ValueTask> action = async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("from the await");
        };

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("from the await");
    }

    [Fact]
    public async Task AGenericValueTaskReturningFunction_ExposesItsResult()
    {
        Func<ValueTask<int>> action = () => new ValueTask<int>(42);

        var result = await action.Should().NotThrowAsync();
        result.Which.Should().Be(42);
    }

    [Fact]
    public async Task Awaiting_WrapsValueTaskMembers()
    {
        var sut = new ValueTaskHolder();

        await sut.Awaiting(s => s.FailAsync()).Should().ThrowAsync<InvalidOperationException>();
        (await sut.Awaiting(s => s.GetAsync()).Should().NotThrowAsync()).Which.Should().Be(7);
    }

    private sealed class ValueTaskHolder
    {
        public async ValueTask FailAsync()
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }

        public ValueTask<int> GetAsync() => new(7);
    }

    #endregion

    #region ExecutionTime (M1.2)

    /// <summary>
    /// The hang this fixes: measured inline, BeLessThan against an action that never returns never
    /// returned either. A generous bound keeps the test honest without making it slow.
    /// </summary>
    [Fact]
    public void BeLessThan_Fails_RatherThanHanging_OnASlowAction()
    {
        Action slow = () => Thread.Sleep(TimeSpan.FromSeconds(30));

        var ex = Record.Exception(() =>
            slow.Should().ExecutionTime().BeLessThan(TimeSpan.FromMilliseconds(100)));

        Assert.IsType<AssertionFailedException>(ex);
        Assert.Contains("had not completed", ex.Message);
    }

    /// <summary>Chained assertions read one measurement instead of re-running the action.</summary>
    [Fact]
    public void TheActionRunsOnce_AcrossChainedAssertions()
    {
        var runs = 0;
        Action action = () => runs++;

        action.Should().ExecutionTime()
            .BeGreaterThanOrEqualTo(TimeSpan.Zero).And
            .BeGreaterThanOrEqualTo(TimeSpan.Zero);

        Assert.Equal(1, runs);
    }

    [Fact]
    public void AnExceptionFromTheMeasuredAction_StillPropagates()
    {
        Action boom = () => throw new FormatException("from the action");

        var ex = Record.Exception(() =>
            boom.Should().ExecutionTime().BeLessThan(TimeSpan.FromSeconds(5)));

        Assert.IsType<FormatException>(ex);
    }

    #endregion
}
