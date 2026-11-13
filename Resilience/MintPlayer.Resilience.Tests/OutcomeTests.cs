using MintPlayer.Resilience.CircuitBreaker;
using MintPlayer.Resilience.RateLimiting;
using MintPlayer.Resilience.Retry;
using MintPlayer.Resilience.Timeout;

namespace MintPlayer.Resilience.Tests;

public class OutcomeTests
{
    [Fact]
    public void FromResult_IsASuccess()
    {
        var outcome = Outcome.FromResult(5);

        outcome.IsSuccess.Should().BeTrue();
        outcome.IsRejected.Should().BeFalse();
        outcome.Rejection.Should().Be(RejectionKind.None);
        outcome.Result.Should().Be(5);
        outcome.Exception.Should().BeNull();
        outcome.RetryAfter.Should().NotHaveValue();
        outcome.GetResultOrThrow().Should().Be(5);
    }

    [Fact]
    public void FromException_RethrowsTheSameInstance()
    {
        var exception = new InvalidOperationException("x");
        var outcome = Outcome.FromException<int>(exception);

        outcome.IsSuccess.Should().BeFalse();
        outcome.IsRejected.Should().BeFalse();
        outcome.Exception.Should().BeSameAs(exception);

        Action act = () => outcome.GetResultOrThrow();
        act.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(exception);
    }

    [Fact]
    public void FromException_RejectsNull()
    {
        Action act = () => Outcome.FromException<int>(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Default_IsASuccessWithTheDefaultResult()
    {
        var outcome = default(Outcome<int>);

        outcome.IsSuccess.Should().BeTrue();
        outcome.RetryAfter.Should().NotHaveValue();
    }

    [Theory]
    [InlineData(RejectionKind.CircuitOpen, typeof(BrokenCircuitException))]
    [InlineData(RejectionKind.CircuitIsolated, typeof(IsolatedCircuitException))]
    [InlineData(RejectionKind.RateLimited, typeof(RateLimiterRejectedException))]
    [InlineData(RejectionKind.Timeout, typeof(TimeoutRejectedException))]
    public void Rejection_CreatesAFreshExceptionOfTheMatchingType_OnEveryRead(RejectionKind kind, Type expected)
    {
        var outcome = Outcome.Rejected<int>(kind);

        outcome.IsRejected.Should().BeTrue();
        outcome.IsSuccess.Should().BeFalse();
        var first = outcome.Exception;
        var second = outcome.Exception;

        first!.GetType().Should().Be(expected);
        ((ResilienceRejectedException)first).Kind.Should().Be(kind);
        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void Rejection_GetResultOrThrow_ThrowsAFreshExceptionEachTime()
    {
        var outcome = Outcome.Rejected<int>(RejectionKind.CircuitOpen, TimeSpan.FromSeconds(3).Ticks);

        Exception? first = null, second = null;
        try { outcome.GetResultOrThrow(); } catch (Exception e) { first = e; }
        try { outcome.GetResultOrThrow(); } catch (Exception e) { second = e; }

        first.Should().BeOfType<BrokenCircuitException>();
        second.Should().BeOfType<BrokenCircuitException>();
        second.Should().NotBeSameAs(first);
        ((BrokenCircuitException)first!).RetryAfter.Should().Be(TimeSpan.FromSeconds(3));
        first!.Data.Count.Should().Be(0);
    }

    [Fact]
    public void RetryAfter_IsReportedForCircuitAndLimiterRejections_NotForTimeouts()
    {
        Outcome.Rejected<int>(RejectionKind.RateLimited, TimeSpan.FromSeconds(1).Ticks).RetryAfter.Should().Be(TimeSpan.FromSeconds(1));
        Outcome.Rejected<int>(RejectionKind.CircuitOpen).RetryAfter.Should().NotHaveValue();

        var timeout = Outcome.Rejected<int>(RejectionKind.Timeout, TimeSpan.FromSeconds(2).Ticks);
        timeout.RetryAfter.Should().NotHaveValue();
        ((TimeoutRejectedException)timeout.Exception!).Timeout.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Rejection_Cause_BecomesTheInnerException()
    {
        var cause = new OperationCanceledException();
        var outcome = Outcome.Rejected<int>(RejectionKind.Timeout, TimeSpan.FromSeconds(1).Ticks, cause);

        outcome.Exception!.InnerException.Should().BeSameAs(cause);
    }

    [Fact]
    public void ThrowIfException_DoesNothingForASuccess()
    {
        Action act = () => Outcome.FromResult("ok").ThrowIfException();
        act.Should().NotThrow();
    }

    [Fact]
    public void ToString_DescribesTheOutcome()
    {
        Outcome.FromResult(3).ToString().Should().Be("3");
        Outcome.FromException<int>(new InvalidOperationException("bad")).ToString().Should().Be("bad");
        Outcome.Rejected<int>(RejectionKind.RateLimited).ToString().Should().Be("Rejected: RateLimited");
    }

    [Fact]
    public async Task TryExecuteAsync_ReturnsTheCallbackException_WithoutThrowing()
    {
        var pipeline = new ResiliencePipelineBuilder<int>().AddRetry(new RetryStrategyOptions<int> { MaxRetryAttempts = 1, Delay = TimeSpan.Zero }).Build();

        var outcome = await pipeline.TryExecuteAsync(static _ => throw new InvalidOperationException("kept"));

        outcome.IsSuccess.Should().BeFalse();
        outcome.IsRejected.Should().BeFalse();
        outcome.Exception!.Message.Should().Be("kept");
    }

    [Fact]
    public async Task TryExecuteAsync_ReturnsTheResult()
    {
        var outcome = await ResiliencePipeline<string>.Empty.TryExecuteAsync(static (s, _) => ValueTask.FromResult(s + "!"), "hi");

        outcome.IsSuccess.Should().BeTrue();
        outcome.Result.Should().Be("hi!");
    }

    [Fact]
    public async Task TryExecuteAsync_OnTheNonGenericPipeline_ReturnsTheOutcome()
    {
        var outcome = await ResiliencePipeline.Empty.TryExecuteAsync<int>(static _ => throw new FormatException());

        outcome.Exception.Should().BeOfType<FormatException>();
    }
}

public class PredicateBuilderTests
{
    private static bool Matches<T>(PredicateBuilder<T> builder, Outcome<T> outcome) => builder.Build()(outcome);

    [Fact]
    public void Handle_MatchesTheExceptionTypeAndSubtypes()
    {
        var builder = new PredicateBuilder<int>().Handle<ArgumentException>();

        Matches(builder, Outcome.FromException<int>(new ArgumentException())).Should().BeTrue();
        Matches(builder, Outcome.FromException<int>(new ArgumentNullException())).Should().BeTrue();
        Matches(builder, Outcome.FromException<int>(new InvalidOperationException())).Should().BeFalse();
        Matches(builder, Outcome.FromResult(1)).Should().BeFalse();
    }

    [Fact]
    public void Handle_MatchesRejectionsByTheirExceptionType()
    {
        Matches(new PredicateBuilder<int>().Handle<TimeoutRejectedException>(), Outcome.Rejected<int>(RejectionKind.Timeout)).Should().BeTrue();
        Matches(new PredicateBuilder<int>().Handle<TimeoutRejectedException>(), Outcome.Rejected<int>(RejectionKind.CircuitOpen)).Should().BeFalse();
        Matches(new PredicateBuilder<int>().Handle<BrokenCircuitException>(), Outcome.Rejected<int>(RejectionKind.CircuitIsolated)).Should().BeTrue();
        Matches(new PredicateBuilder<int>().Handle<IsolatedCircuitException>(), Outcome.Rejected<int>(RejectionKind.CircuitOpen)).Should().BeFalse();
        Matches(new PredicateBuilder<int>().Handle<ResilienceRejectedException>(), Outcome.Rejected<int>(RejectionKind.RateLimited)).Should().BeTrue();
        Matches(new PredicateBuilder<int>().Handle<Exception>(), Outcome.Rejected<int>(RejectionKind.RateLimited)).Should().BeTrue();
    }

    [Fact]
    public void Handle_WithAPredicate_FiltersTheException()
    {
        var builder = new PredicateBuilder<int>().Handle<InvalidOperationException>(e => e.Message == "retry");

        Matches(builder, Outcome.FromException<int>(new InvalidOperationException("retry"))).Should().BeTrue();
        Matches(builder, Outcome.FromException<int>(new InvalidOperationException("stop"))).Should().BeFalse();

        var timeouts = new PredicateBuilder<int>().Handle<TimeoutRejectedException>(e => e.Timeout == TimeSpan.FromSeconds(1));
        Matches(timeouts, Outcome.Rejected<int>(RejectionKind.Timeout, TimeSpan.FromSeconds(1).Ticks)).Should().BeTrue();
        Matches(timeouts, Outcome.Rejected<int>(RejectionKind.Timeout, TimeSpan.FromSeconds(2).Ticks)).Should().BeFalse();
    }

    [Fact]
    public void HandleInner_LooksThroughInnerAndAggregateExceptions()
    {
        var builder = new PredicateBuilder<int>().HandleInner<TimeoutException>();

        Matches(builder, Outcome.FromException<int>(new InvalidOperationException("outer", new TimeoutException()))).Should().BeTrue();
        Matches(builder, Outcome.FromException<int>(new AggregateException(new FormatException(), new TimeoutException()))).Should().BeTrue();
        Matches(builder, Outcome.FromException<int>(new InvalidOperationException())).Should().BeFalse();
    }

    [Fact]
    public void HandleResult_MatchesResults_NotExceptions()
    {
        var builder = new PredicateBuilder<int>().HandleResult(-1).HandleResult(r => r > 100);

        Matches(builder, Outcome.FromResult(-1)).Should().BeTrue();
        Matches(builder, Outcome.FromResult(101)).Should().BeTrue();
        Matches(builder, Outcome.FromResult(5)).Should().BeFalse();
        Matches(builder, Outcome.FromException<int>(new Exception())).Should().BeFalse();
    }

    [Fact]
    public void Build_WithoutPredicates_Throws()
    {
        Action act = () => new PredicateBuilder<int>().Build();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ConvertsImplicitlyToStrategyPredicates()
    {
        Func<RetryPredicateArguments<int>, bool> retry = new PredicateBuilder<int>().HandleResult(1);
        Func<MintPlayer.Resilience.Fallback.FallbackPredicateArguments<int>, bool> fallback = new PredicateBuilder<int>().HandleResult(1);
        var context = ResilienceContextPool.Shared.Get();
        try
        {
            retry(new RetryPredicateArguments<int>(context, Outcome.FromResult(1), 0)).Should().BeTrue();
            fallback(new MintPlayer.Resilience.Fallback.FallbackPredicateArguments<int>(context, Outcome.FromResult(2))).Should().BeFalse();
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    [Fact]
    public void Handle_OnARejection_DoesNotAllocate()
    {
        var predicate = new PredicateBuilder<int>().Handle<TimeoutRejectedException>().Build();
        var outcome = Outcome.Rejected<int>(RejectionKind.Timeout);
        predicate(outcome); // warm up

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            predicate(outcome);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }
}
