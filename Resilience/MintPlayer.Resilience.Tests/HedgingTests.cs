using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.Hedging;
using MintPlayer.Resilience.Retry;
using MintPlayer.Resilience.Timeout;

namespace MintPlayer.Resilience.Tests;

public class HedgingTests
{
    private static readonly TimeSpan TwoSeconds = TimeSpan.FromSeconds(2);

    /// <summary>One gate per attempt: attempt <c>i</c> waits for <c>gates[i]</c>, and is cancelled with its token.</summary>
    private sealed class Gates<T>
    {
        private readonly TaskCompletionSource<T>[] _gates;
        private readonly CancellationToken[] _tokens;
        private int _calls;

        public Gates(int count = 16)
        {
            _gates = new TaskCompletionSource<T>[count];
            _tokens = new CancellationToken[count];
            for (var i = 0; i < count; i++)
            {
                _gates[i] = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public int Calls => Volatile.Read(ref _calls);

        public TaskCompletionSource<T> this[int attempt] => _gates[attempt];

        public CancellationToken Token(int attempt) => _tokens[attempt];

        public async ValueTask<T> Invoke(CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _calls) - 1;
            var gate = _gates[attempt];
            _tokens[attempt] = cancellationToken;
            using var registration = cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));
            return await gate.Task;
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("The condition was not met within 10 s.");
            }

            await Task.Delay(5);
        }
    }

    private static ResiliencePipelineBuilder<T> Builder<T>(TimeProvider? time = null) => new() { TimeProvider = time ?? new FakeTimeProvider() };

    [Fact]
    public async Task AcceptablePrimaryOutcome_IsReturned_WithoutAHedgedAttempt()
    {
        var hedges = 0;
        var calls = 0;
        var pipeline = Builder<string>().AddHedging(new HedgingStrategyOptions<string>
        {
            OnHedging = _ =>
            {
                hedges++;
                return default;
            },
        }).Build();

        var result = await pipeline.ExecuteAsync(_ =>
        {
            calls++;
            return new ValueTask<string>("primary");
        });

        result.Should().Be("primary");
        calls.Should().Be(1);
        hedges.Should().Be(0);
    }

    [Fact]
    public async Task SlowPrimary_StartsAHedgedAttemptAfterTheDelay_AndTheFirstAcceptableOutcomeWins()
    {
        var time = new DelayRecordingTimeProvider();
        var gates = new Gates<string>();
        var pipeline = Builder<string>(time).AddHedging(new HedgingStrategyOptions<string> { Delay = TwoSeconds }).Build();

        var execution = pipeline.ExecuteAsync(gates.Invoke).AsTask();
        (await time.NextDelayAsync()).Should().Be(TwoSeconds);
        gates.Calls.Should().Be(1);

        time.Advance(TimeSpan.FromMilliseconds(1999));
        gates.Calls.Should().Be(1);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await WaitUntil(() => gates.Calls == 2);

        gates[1].SetResult("hedged");
        (await execution).Should().Be("hedged");

        // The losing primary attempt was cancelled (and awaited) before the execution completed.
        gates.Token(0).IsCancellationRequested.Should().BeTrue();
        gates[0].Task.IsCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task PrimaryWinsWhenItCompletesFirst_AndTheHedgedAttemptIsCancelled()
    {
        var time = new DelayRecordingTimeProvider();
        var gates = new Gates<string>();
        var pipeline = Builder<string>(time).AddHedging(new HedgingStrategyOptions<string> { Delay = TwoSeconds }).Build();

        var execution = pipeline.ExecuteAsync(gates.Invoke).AsTask();
        await time.NextDelayAsync();
        time.Advance(TwoSeconds);
        await WaitUntil(() => gates.Calls == 2);

        gates[0].SetResult("primary");
        (await execution).Should().Be("primary");
        gates.Token(1).IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task HandledPrimaryOutcome_StartsTheHedgedAttemptWithoutWaitingForTheDelay()
    {
        var calls = 0;
        var pipeline = Builder<string>().AddHedging(new HedgingStrategyOptions<string> { Delay = TimeSpan.FromHours(1) }).Build();

        var result = await pipeline.ExecuteAsync(_ => Interlocked.Increment(ref calls) == 1
            ? throw new InvalidOperationException("primary failed")
            : new ValueTask<string>("hedged"));

        result.Should().Be("hedged");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task ZeroDelay_ParallelMode_StartsEveryAttemptAtOnce()
    {
        var gates = new Gates<string>();
        var pipeline = Builder<string>().AddHedging(new HedgingStrategyOptions<string> { Delay = TimeSpan.Zero, MaxHedgedAttempts = 3 }).Build();

        var execution = pipeline.ExecuteAsync(gates.Invoke).AsTask();
        await WaitUntil(() => gates.Calls == 4);

        gates[2].SetResult("third");
        (await execution).Should().Be("third");
        gates.Token(0).IsCancellationRequested.Should().BeTrue();
        gates.Token(1).IsCancellationRequested.Should().BeTrue();
        gates.Token(2).IsCancellationRequested.Should().BeFalse();
        gates.Token(3).IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task NegativeDelay_FallbackMode_StartsTheNextAttemptOnlyAfterAHandledOutcome()
    {
        var time = new FakeTimeProvider();
        var gates = new Gates<string>();
        var pipeline = Builder<string>(time).AddHedging(new HedgingStrategyOptions<string> { Delay = System.Threading.Timeout.InfiniteTimeSpan }).Build();

        var execution = pipeline.ExecuteAsync(gates.Invoke).AsTask();
        time.Advance(TimeSpan.FromHours(1));
        await Task.Delay(50);
        gates.Calls.Should().Be(1);

        gates[0].SetException(new InvalidOperationException("primary failed"));
        await WaitUntil(() => gates.Calls == 2);
        gates[1].SetResult("fallback");

        (await execution).Should().Be("fallback");
    }

    [Fact]
    public async Task EveryAttemptHandled_ReturnsThePrimaryOutcome_AsPolly()
    {
        var calls = 0;
        var pipeline = Builder<string>().AddHedging(new HedgingStrategyOptions<string> { Delay = TimeSpan.Zero, MaxHedgedAttempts = 2 }).Build();

        Func<Task> act = async () => await pipeline.ExecuteAsync(_ => throw new InvalidOperationException($"attempt {Interlocked.Increment(ref calls) - 1}"));

        var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        thrown.Message.Should().Be("attempt 0");
        calls.Should().Be(3);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public async Task MaxHedgedAttempts_BoundsTheNumberOfAttempts(int maxHedgedAttempts)
    {
        var calls = 0;
        var pipeline = Builder<int>().AddHedging(new HedgingStrategyOptions<int> { Delay = TimeSpan.Zero, MaxHedgedAttempts = maxHedgedAttempts }).Build();

        var outcome = await pipeline.TryExecuteAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException();
        });

        outcome.Exception.Should().BeOfType<InvalidOperationException>();
        calls.Should().Be(maxHedgedAttempts + 1);
    }

    [Fact]
    public async Task HandledResultOfALosingAttempt_IsDisposed_TheWinnerIsNot()
    {
        var results = new[] { new DisposableResult(0), new DisposableResult(1) };
        var calls = 0;
        var pipeline = Builder<DisposableResult>().AddHedging(new HedgingStrategyOptions<DisposableResult>
        {
            ShouldHandle = new PredicateBuilder<DisposableResult>().HandleResult(r => r.Value == 0),
        }).Build();

        var result = await pipeline.ExecuteAsync(_ => new ValueTask<DisposableResult>(results[Interlocked.Increment(ref calls) - 1]));

        result.Should().BeSameAs(results[1]);
        results[0].IsDisposed.Should().BeTrue();
        results[1].IsDisposed.Should().BeFalse();
    }

    [Fact]
    public async Task ResultOfACancelledLoser_IsDisposed_BeforeTheExecutionCompletes()
    {
        var loser = new DisposableResult(0);
        var winner = new DisposableResult(1);
        var calls = 0;
        var pipeline = Builder<DisposableResult>().AddHedging(new HedgingStrategyOptions<DisposableResult> { Delay = TimeSpan.Zero }).Build();

        var result = await pipeline.ExecuteAsync(async cancellationToken =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                return winner;
            }

            // The primary ignores the cancellation and still produces a result, which nobody will use.
            var gate = new TaskCompletionSource<DisposableResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => gate.TrySetResult(loser));
            return await gate.Task;
        });

        result.Should().BeSameAs(winner);
        winner.IsDisposed.Should().BeFalse();
        loser.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ActionGenerator_ProducesTheHedgedAction_WithAForkedContext()
    {
        var seen = new List<(int Attempt, bool SameContext)>();
        var pipeline = Builder<string>().AddHedging(new HedgingStrategyOptions<string>
        {
            ActionGenerator = args =>
            {
                seen.Add((args.AttemptNumber, ReferenceEquals(args.PrimaryContext, args.ActionContext)));
                return () => Outcome.FromResultAsValueTask($"generated {args.AttemptNumber}");
            },
        }).Build();

        var result = await pipeline.ExecuteAsync(_ => throw new InvalidOperationException());

        result.Should().Be("generated 1");
        seen.Should().Equal((1, false));
    }

    [Fact]
    public async Task ActionGenerator_Callback_RunsTheRestOfThePipelineAgain()
    {
        var calls = 0;
        var pipeline = Builder<string>().AddHedging(new HedgingStrategyOptions<string>
        {
            ActionGenerator = args => () => args.Callback(args.ActionContext),
        }).Build();

        var result = await pipeline.ExecuteAsync(_ => Interlocked.Increment(ref calls) == 1
            ? throw new InvalidOperationException()
            : new ValueTask<string>("again"));

        result.Should().Be("again");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task ActionGenerator_ReturningNull_StartsNothing_AndDoesNotSpinInParallelMode()
    {
        var generatorCalls = 0;
        var gates = new Gates<string>();
        var pipeline = Builder<string>().AddHedging(new HedgingStrategyOptions<string>
        {
            Delay = TimeSpan.Zero,
            ActionGenerator = _ =>
            {
                Interlocked.Increment(ref generatorCalls);
                return null;
            },
        }).Build();

        var execution = pipeline.ExecuteAsync(gates.Invoke).AsTask();
        await Task.Delay(50);
        Volatile.Read(ref generatorCalls).Should().Be(1);

        gates[0].SetException(new InvalidOperationException("primary"));
        Func<Task> act = () => execution;
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("primary");
        gates.Calls.Should().Be(1);
        generatorCalls.Should().Be(2);
    }

    [Fact]
    public async Task ActionGenerator_Throwing_BecomesTheOutcomeOfThatAttempt()
    {
        var pipeline = Builder<string>().AddHedging(new HedgingStrategyOptions<string>
        {
            ShouldHandle = new PredicateBuilder<string>().Handle<TimeoutException>(),
            ActionGenerator = _ => throw new InvalidOperationException("generator"),
        }).Build();

        Func<Task> act = async () => await pipeline.ExecuteAsync(_ => throw new TimeoutException());

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("generator");
    }

    [Fact]
    public async Task OnHedging_IsRaisedBeforeEachHedgedAttempt_WithPollysAttemptNumber()
    {
        var events = new List<(int Attempt, bool SameContext)>();
        var calls = 0;
        var pipeline = Builder<string>().AddHedging(new HedgingStrategyOptions<string>
        {
            Delay = TimeSpan.Zero,
            MaxHedgedAttempts = 2,
            OnHedging = args =>
            {
                lock (events)
                {
                    events.Add((args.AttemptNumber, ReferenceEquals(args.PrimaryContext, args.ActionContext)));
                }

                return default;
            },
        }).Build();

        var result = await pipeline.ExecuteAsync(_ => Interlocked.Increment(ref calls) < 3
            ? throw new InvalidOperationException()
            : new ValueTask<string>("third"));

        result.Should().Be("third");
        events.Should().Equal((0, false), (1, false));
    }

    [Fact]
    public async Task DelayGenerator_OverridesTheDelay_PerAttempt()
    {
        var time = new DelayRecordingTimeProvider();
        var gates = new Gates<string>();
        var seen = new List<int>();
        var pipeline = Builder<string>(time).AddHedging(new HedgingStrategyOptions<string>
        {
            MaxHedgedAttempts = 2,
            DelayGenerator = args =>
            {
                lock (seen)
                {
                    seen.Add(args.AttemptNumber);
                }

                return args.AttemptNumber == 1 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(3);
            },
        }).Build();

        var execution = pipeline.ExecuteAsync(gates.Invoke).AsTask();
        (await time.AdvanceNextDelayAsync()).Should().Be(TimeSpan.FromSeconds(1));
        await WaitUntil(() => gates.Calls == 2);
        (await time.AdvanceNextDelayAsync()).Should().Be(TimeSpan.FromSeconds(3));
        await WaitUntil(() => gates.Calls == 3);

        gates[2].SetResult("third");
        (await execution).Should().Be("third");
        seen.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Context_IsForkedPerAttempt_AndOnlyTheWinnersPropertiesAreMergedBack()
    {
        var inherited = new ResiliencePropertyKey<string>("inherited");
        var mark = new ResiliencePropertyKey<string>("mark");
        var loserOnly = new ResiliencePropertyKey<string>("loser-only");
        var context = ResilienceContextPool.Shared.Get();
        context.Properties.Set(inherited, "caller");
        var seen = new List<(string Inherited, bool SameContext)>();
        var calls = 0;
        var pipeline = Builder<string>().AddHedging(new HedgingStrategyOptions<string> { Delay = TimeSpan.Zero }).Build();

        var result = await pipeline.ExecuteAsync(
            attemptContext =>
            {
                var attempt = Interlocked.Increment(ref calls) - 1;
                lock (seen)
                {
                    seen.Add((attemptContext.Properties.GetValue(inherited, "missing"), ReferenceEquals(attemptContext, context)));
                }

                attemptContext.Properties.Set(mark, $"attempt {attempt}");
                if (attempt == 0)
                {
                    attemptContext.Properties.Set(loserOnly, "loser");
                    throw new InvalidOperationException();
                }

                return new ValueTask<string>("ok");
            },
            context);

        result.Should().Be("ok");
        seen.Should().Equal(("caller", false), ("caller", false));
        context.Properties.GetValue(mark, "none").Should().Be("attempt 1");
        context.Properties.TryGetValue(loserOnly, out _).Should().BeFalse();
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task CallerCancellation_CancelsEveryAttempt_AndReportsTheCallersToken()
    {
        var time = new DelayRecordingTimeProvider();
        var gates = new Gates<string>();
        using var cts = new CancellationTokenSource();
        var pipeline = Builder<string>(time).AddHedging(new HedgingStrategyOptions<string> { Delay = TwoSeconds }).Build();

        var execution = pipeline.ExecuteAsync(gates.Invoke, cts.Token).AsTask();
        await time.AdvanceNextDelayAsync();
        await WaitUntil(() => gates.Calls == 2);
        await cts.CancelAsync();

        Func<Task> act = () => execution;
        var thrown = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
        thrown.CancellationToken.Should().Be(cts.Token);
        gates.Token(0).IsCancellationRequested.Should().BeTrue();
        gates.Token(1).IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task RetryOutsideHedging_RetriesTheWholeHedgedExecution()
    {
        var calls = 0;
        var pipeline = Builder<string>()
            .AddRetry(new RetryStrategyOptions<string> { MaxRetryAttempts = 1, Delay = TimeSpan.Zero })
            .AddHedging(new HedgingStrategyOptions<string> { Delay = TimeSpan.Zero })
            .Build();

        Func<Task> act = async () => await pipeline.ExecuteAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException();
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().Be(4);
    }

    [Fact]
    public async Task RetryInsideHedging_RunsPerAttempt()
    {
        var calls = 0;
        var pipeline = Builder<string>()
            .AddHedging(new HedgingStrategyOptions<string> { Delay = TimeSpan.Zero, MaxHedgedAttempts = 2 })
            .AddRetry(new RetryStrategyOptions<string> { MaxRetryAttempts = 2, Delay = TimeSpan.Zero })
            .Build();

        Func<Task> act = async () => await pipeline.ExecuteAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException();
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().Be(9);
    }

    [Fact]
    public async Task TimeoutInsideHedging_AppliesPerAttempt_AndATimedOutAttemptIsHedged()
    {
        var time = new FakeTimeProvider();
        var calls = 0;
        var pipeline = Builder<string>(time)
            .AddHedging(new HedgingStrategyOptions<string> { Delay = TimeSpan.FromSeconds(10) })
            .AddTimeout(TimeSpan.FromSeconds(1))
            .Build();

        var execution = pipeline.ExecuteAsync(async cancellationToken =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            }

            return "hedged";
        }).AsTask();

        time.Advance(TimeSpan.FromSeconds(1));
        (await execution).Should().Be("hedged");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task TimeoutOutsideHedging_CancelsEveryAttempt_AndIsATimeout()
    {
        var time = new FakeTimeProvider();
        var calls = 0;
        var pipeline = Builder<string>(time)
            .AddTimeout(TimeSpan.FromSeconds(3))
            .AddHedging(new HedgingStrategyOptions<string> { Delay = TwoSeconds })
            .Build();

        var execution = pipeline.ExecuteAsync(async cancellationToken =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            return "never";
        }).AsTask();

        time.Advance(TwoSeconds);
        await WaitUntil(() => Volatile.Read(ref calls) == 2);
        time.Advance(TimeSpan.FromSeconds(1));

        Func<Task> act = () => execution;
        await act.Should().ThrowAsync<TimeoutRejectedException>();
    }

    [Fact]
    public void SynchronousExecute_RunsHedgedAttemptsOnTheThreadPool()
    {
        var calls = 0;
        var pipeline = Builder<string>().AddHedging(new HedgingStrategyOptions<string> { Delay = TimeSpan.Zero }).Build();

        var result = pipeline.Execute(_ => Interlocked.Increment(ref calls) == 1
            ? throw new InvalidOperationException()
            : "sync");

        result.Should().Be("sync");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task PredicateBuilder_ConvertsToTheHedgingPredicate()
    {
        var calls = 0;
        var pipeline = Builder<string>().AddHedging(new HedgingStrategyOptions<string>
        {
            ShouldHandle = new PredicateBuilder<string>().HandleResult("bad"),
        }).Build();

        var result = await pipeline.ExecuteAsync(_ => new ValueTask<string>(Interlocked.Increment(ref calls) == 1 ? "bad" : "good"));

        result.Should().Be("good");
    }

    [Fact]
    public async Task ManyConcurrentExecutions_ReusePooledState()
    {
        var pipeline = Builder<int>().AddHedging(new HedgingStrategyOptions<int> { Delay = TimeSpan.Zero, MaxHedgedAttempts = 2 }).Build();

        var tasks = Enumerable.Range(0, 200).Select(i => pipeline.ExecuteAsync(async (value, cancellationToken) =>
        {
            await Task.Yield();
            return value;
        }, i).AsTask());

        var results = await Task.WhenAll(tasks);
        results.Should().Equal(Enumerable.Range(0, 200).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void MaxHedgedAttempts_OutOfRange_IsInvalid(int maxHedgedAttempts)
    {
        var act = () => new ResiliencePipelineBuilder<int>().AddHedging(new HedgingStrategyOptions<int> { MaxHedgedAttempts = maxHedgedAttempts });

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Defaults_MatchPolly()
    {
        var options = new HedgingStrategyOptions<int>();

        options.Name.Should().Be("Hedging");
        options.MaxHedgedAttempts.Should().Be(1);
        options.Delay.Should().Be(TwoSeconds);
        options.DelayGenerator.Should().BeNull();
        options.OnHedging.Should().BeNull();
    }
}
