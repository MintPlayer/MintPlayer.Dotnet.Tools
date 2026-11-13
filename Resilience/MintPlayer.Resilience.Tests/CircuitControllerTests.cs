using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.CircuitBreaker;

namespace MintPlayer.Resilience.Tests;

/// <summary>
/// The lock-free controller on its own, driven step by step with a <see cref="FakeTimeProvider"/>: the
/// deterministic transition tests of spike S4 (T1–T5) plus the slow-call extension.
/// </summary>
public class CircuitControllerTests
{
    // Ratio 0.5, minimum throughput 4, sampling 1 s, break 1 s.
    private static CircuitController Std(TimeProvider time, int stripes = 0) => new(time, 0.5, 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), stripes: stripes);

    private static Admission Enter(CircuitController breaker) => breaker.TryEnter(out _);

    private static void Fail(CircuitController breaker, int count)
    {
        for (var i = 0; i < count; i++)
        {
            Enter(breaker);
            breaker.RecordFailure(null, CircuitController.NotMeasured, null);
        }
    }

    private static void Succeed(CircuitController breaker, int count)
    {
        for (var i = 0; i < count; i++)
        {
            Enter(breaker);
            breaker.RecordSuccess(CircuitController.NotMeasured, null);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void T1_ClosedOpenHalfOpenClosed_WithASingleProbe_AndTheWindowResetOnClose(int stripes)
    {
        var time = new FakeTimeProvider();
        var b = Std(time, stripes);

        b.State.Should().Be(CircuitState.Closed);
        Fail(b, 3);
        b.State.Should().Be(CircuitState.Closed); // below the minimum throughput
        Fail(b, 1);
        b.State.Should().Be(CircuitState.Open); // min throughput reached, ratio 1 >= 0.5
        Enter(b).Should().Be(Admission.Rejected);

        time.Advance(TimeSpan.FromMilliseconds(999));
        Enter(b).Should().Be(Admission.Rejected); // 1 ms before the break ends
        time.Advance(TimeSpan.FromMilliseconds(1));
        Enter(b).Should().Be(Admission.Probe); // exactly at the break end
        b.State.Should().Be(CircuitState.HalfOpen);
        Enter(b).Should().Be(Admission.Rejected); // a second caller while the probe is out

        b.RecordSuccess(CircuitController.NotMeasured, null).State.Should().Be(CircuitState.Closed);
        b.State.Should().Be(CircuitState.Closed);

        Fail(b, 3);
        Succeed(b, 1);
        b.State.Should().Be(CircuitState.Closed); // 3F + 1S: a success never breaks, so the old 4F were cleared
        Fail(b, 1);
        b.State.Should().Be(CircuitState.Open); // 4F / 5
    }

    [Fact]
    public void T2_AProbeFailure_ReopensWithAFreshBreak()
    {
        var time = new FakeTimeProvider();
        var b = Std(time);
        Fail(b, 4);
        time.Advance(TimeSpan.FromSeconds(1));
        Enter(b).Should().Be(Admission.Probe);

        time.Advance(TimeSpan.FromMilliseconds(300));
        var transition = b.RecordFailure(null, CircuitController.NotMeasured, null);
        transition.Happened.Should().BeTrue();
        transition.State.Should().Be(CircuitState.Open);
        transition.BreakDuration.Should().Be(TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromMilliseconds(999));
        Enter(b).Should().Be(Admission.Rejected); // the break counts from the probe failure
        time.Advance(TimeSpan.FromMilliseconds(1));
        Enter(b).Should().Be(Admission.Probe);
    }

    [Fact]
    public void T3_FailuresOlderThanTheSamplingDuration_Expire()
    {
        var time = new FakeTimeProvider();
        var b = Std(time);
        Fail(b, 3);
        time.Advance(TimeSpan.FromMilliseconds(1000));
        Fail(b, 1);
        b.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void T3_FailuresJustInsideTheSamplingDuration_StillCount()
    {
        var time = new FakeTimeProvider();
        var b = Std(time);
        Fail(b, 3);
        time.Advance(TimeSpan.FromMilliseconds(999));
        Fail(b, 1);
        b.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public void T3_WindowsAreAnchoredAtTheirFirstEvent_NotOnAFixedGrid()
    {
        // 2F @0, then 1F + 2S @150 (a window anchored at 150), then 1F @1120. The window @0 expired; the
        // one @150 lives until 1150 (a grid bucket [100, 200) would be gone at 1100), so 2F/4 opens.
        var time = new FakeTimeProvider();
        var b = Std(time);
        Fail(b, 2);
        time.Advance(TimeSpan.FromMilliseconds(150));
        Fail(b, 1);
        Succeed(b, 2);
        time.Advance(TimeSpan.FromMilliseconds(970));
        Fail(b, 1);
        b.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public void T4_Isolate_RejectsForever_AndCloseResetsTheWindow()
    {
        var time = new FakeTimeProvider();
        var b = Std(time);

        var isolate = b.Isolate();
        isolate.State.Should().Be(CircuitState.Isolated);
        isolate.BreakDuration.Should().Be(TimeSpan.MaxValue);
        b.State.Should().Be(CircuitState.Isolated);
        time.Advance(TimeSpan.FromHours(1));
        Enter(b).Should().Be(Admission.Rejected);

        b.Close().Happened.Should().BeTrue();
        b.State.Should().Be(CircuitState.Closed);
        Enter(b).Should().Be(Admission.Closed);

        Fail(b, 4);
        b.Close();
        Fail(b, 3);
        b.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void T4_CloseWhenAlreadyClosed_IsNoTransition_ButStillClearsTheWindow()
    {
        var b = Std(new FakeTimeProvider());
        Fail(b, 3);
        b.Close().Happened.Should().BeFalse();
        b.Health().Should().Be((0L, 0L, 0L));
    }

    [Fact]
    public void T5_LateResultsWhileOpen_ChangeNothing_AndALateSuccessInHalfOpenCloses()
    {
        var time = new FakeTimeProvider();
        var b = Std(time);
        Fail(b, 4);

        b.RecordSuccess(CircuitController.NotMeasured, null).Happened.Should().BeFalse();
        b.RecordFailure(null, CircuitController.NotMeasured, null).Happened.Should().BeFalse();
        b.State.Should().Be(CircuitState.Open);

        time.Advance(TimeSpan.FromSeconds(1));
        Enter(b).Should().Be(Admission.Probe);
        b.RecordSuccess(CircuitController.NotMeasured, null); // a call admitted before the break, finishing now
        b.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void EveryTransition_IncrementsTheGeneration_AndIsReportedToTheWinner()
    {
        var time = new FakeTimeProvider();
        var b = Std(time);
        var log = new List<(CircuitState From, CircuitState To, long Gen)>();
        b.OnTransition = (from, to) => log.Add((CircuitController.StateOf(from), CircuitController.StateOf(to), CircuitController.GenOf(to)));

        Fail(b, 4);
        time.Advance(TimeSpan.FromSeconds(1));
        Enter(b);
        b.RecordSuccess(CircuitController.NotMeasured, null);
        b.Isolate();
        b.Close();

        log.Should().Equal(
            (CircuitState.Closed, CircuitState.Open, 1L),
            (CircuitState.Open, CircuitState.HalfOpen, 2L),
            (CircuitState.HalfOpen, CircuitState.Closed, 3L),
            (CircuitState.Closed, CircuitState.Isolated, 4L),
            (CircuitState.Isolated, CircuitState.Closed, 5L));
    }

    [Fact]
    public void Reject_WhileOpen_CarriesTheRemainingBreakAndTheLastHandledException()
    {
        var time = new FakeTimeProvider();
        var b = Std(time);
        var boom = new InvalidOperationException("boom");
        for (var i = 0; i < 4; i++)
        {
            Enter(b);
            b.RecordFailure(boom, CircuitController.NotMeasured, null);
        }

        time.Advance(TimeSpan.FromMilliseconds(300));
        Enter(b).Should().Be(Admission.Rejected);
        b.TryEnter(out var word);
        var outcome = b.Reject<int>(word);

        outcome.Rejection.Should().Be(RejectionKind.CircuitOpen);
        outcome.RetryAfter.Should().Be(TimeSpan.FromMilliseconds(700));
        outcome.Exception.Should().BeOfType<BrokenCircuitException>();
        outcome.Exception!.InnerException.Should().BeSameAs(boom);
    }

    [Fact]
    public void Reject_WhileIsolated_IsAnIsolatedRejectionWithoutRetryAfter()
    {
        var b = Std(new FakeTimeProvider());
        b.Isolate();
        b.TryEnter(out var word).Should().Be(Admission.Rejected);
        var outcome = b.Reject<int>(word);

        outcome.Rejection.Should().Be(RejectionKind.CircuitIsolated);
        outcome.RetryAfter.Should().NotHaveValue();
        outcome.Exception.Should().BeOfType<IsolatedCircuitException>();
    }

    [Fact]
    public void BreakDurationGenerator_DecidesTheBreak_WithTheWindowAtBreakTime_AndCountsHalfOpenAttempts()
    {
        var time = new FakeTimeProvider();
        var calls = new List<(double Rate, int Count, int HalfOpenAttempts)>();
        var b = new CircuitController(time, 0.5, 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), breakDurationGenerator: args =>
        {
            calls.Add((args.FailureRate, args.FailureCount, args.HalfOpenAttempts));
            return TimeSpan.FromSeconds(3);
        });

        Succeed(b, 1);
        Fail(b, 3); // 3F / 4 = 0.75
        b.State.Should().Be(CircuitState.Open);

        time.Advance(TimeSpan.FromMilliseconds(2999));
        Enter(b).Should().Be(Admission.Rejected); // the generated 3 s break, not the 1 s option
        time.Advance(TimeSpan.FromMilliseconds(1));
        Enter(b).Should().Be(Admission.Probe);
        b.RecordFailure(null, CircuitController.NotMeasured, null).BreakDuration.Should().Be(TimeSpan.FromSeconds(3));

        // The re-open reports the window as it was when the circuit broke (cleared at the probe; S4).
        calls.Should().Equal((0.75, 3, 0), (0.75, 3, 1));
    }

    [Fact]
    public void BreakDurationGenerator_NegativeValue_MeansAZeroBreak()
    {
        var time = new FakeTimeProvider();
        var b = new CircuitController(time, 0.5, 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), breakDurationGenerator: _ => TimeSpan.FromSeconds(-5));
        Fail(b, 4);
        b.State.Should().Be(CircuitState.Open);
        Enter(b).Should().Be(Admission.Probe);
    }

    // ------------------------------------------------------------------ slow calls (beyond Polly)

    private static CircuitController Slow(TimeProvider time) => new(time, 0.5, 4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100), slowCallRatio: 0.5);

    // A call admitted `elapsedMs` ago: the start is expressed in the controller's µs clock.
    private static Transition SucceedAfter(CircuitController b, int elapsedMs)
    {
        Enter(b);
        return b.RecordSuccess(b.Now() - (elapsedMs * 1000L), null);
    }

    [Fact]
    public void SlowCalls_TripTheCircuit_EvenWhenTheyAllSucceed()
    {
        var b = Slow(new FakeTimeProvider());
        for (var i = 0; i < 3; i++)
        {
            SucceedAfter(b, 200);
        }

        b.State.Should().Be(CircuitState.Closed); // below the minimum throughput
        SucceedAfter(b, 10);
        b.State.Should().Be(CircuitState.Closed); // a fast success never trips
        SucceedAfter(b, 100).State.Should().Be(CircuitState.Open); // exactly the threshold is slow: 4 slow / 5
        b.Health().Slow.Should().Be(4);
    }

    [Fact]
    public void SlowCalls_ASlowProbe_IsAFailedProbe_AndAFastProbeClosesAndResets()
    {
        var time = new FakeTimeProvider();
        var b = Slow(time);
        for (var i = 0; i < 4; i++)
        {
            SucceedAfter(b, 200);
        }

        b.State.Should().Be(CircuitState.Open);
        time.Advance(TimeSpan.FromSeconds(1));
        Enter(b).Should().Be(Admission.Probe);
        b.RecordSuccess(b.Now() - 500_000, null).State.Should().Be(CircuitState.Open);

        time.Advance(TimeSpan.FromSeconds(1));
        Enter(b).Should().Be(Admission.Probe);
        b.RecordSuccess(b.Now() - 5_000, null).State.Should().Be(CircuitState.Closed);
        b.Health().Should().Be((0L, 0L, 0L));
    }

    [Fact]
    public void SlowCalls_SlowFailuresCountAsSlowToo()
    {
        var b = Slow(new FakeTimeProvider());
        for (var i = 0; i < 4; i++)
        {
            SucceedAfter(b, 10);
        }

        Enter(b);
        b.RecordFailure(null, b.Now() - 150_000, null);
        b.Health().Should().Be((4L, 1L, 1L));
        b.State.Should().Be(CircuitState.Closed); // 1F/5 < 0.5 and 1 slow/5 < 0.5
    }

    [Fact]
    public void SlowCalls_Disabled_ADurationIsIgnored()
    {
        var b = Std(new FakeTimeProvider());
        b.MeasuresDuration.Should().BeFalse();
        for (var i = 0; i < 10; i++)
        {
            SucceedAfter(b, 10_000);
        }

        b.State.Should().Be(CircuitState.Closed);
        b.Health().Slow.Should().Be(0);
    }
}
