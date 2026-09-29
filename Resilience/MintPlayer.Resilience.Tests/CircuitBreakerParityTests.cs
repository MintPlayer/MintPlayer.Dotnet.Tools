using Microsoft.Extensions.Time.Testing;
using MintPlayer.Resilience.CircuitBreaker;
using P = Polly;
using PCB = Polly.CircuitBreaker;

namespace MintPlayer.Resilience.Tests;

/// <summary>
/// Parity with Polly 8.8.0 (the harness of spike S4): random scripts of calls, in-flight calls that end
/// late, clock jumps (including exactly one break) and manual isolate/close drive a Polly pipeline, our
/// pipeline and our bare controller (with a random stripe count) on one <see cref="FakeTimeProvider"/>.
/// After every step the three must agree on the state, on whether the call was admitted, and on the
/// rejection's retry-after.
/// </summary>
public class CircuitBreakerParityTests
{
    private const int Scripts = 400;
    private const int Steps = 250;

    private sealed record Settings(double FailureRatio, int MinimumThroughput, TimeSpan SamplingDuration, TimeSpan BreakDuration);

    private sealed class Flag
    {
        public bool Value;
    }

    /// <summary>Polly 8.8.0 with only a circuit breaker; failures are handled results (-1), so nothing throws.</summary>
    private sealed class PollySide
    {
        private readonly P.ResiliencePipeline<int> _pipeline;
        private readonly PCB.CircuitBreakerStateProvider _state = new();
        private readonly PCB.CircuitBreakerManualControl _manual = new();

        public PollySide(Settings o, TimeProvider time)
        {
            var builder = new P.ResiliencePipelineBuilder<int> { TimeProvider = time };
            P.CircuitBreakerResiliencePipelineBuilderExtensions.AddCircuitBreaker(builder, new PCB.CircuitBreakerStrategyOptions<int>
            {
                FailureRatio = o.FailureRatio,
                MinimumThroughput = o.MinimumThroughput,
                SamplingDuration = o.SamplingDuration,
                BreakDuration = o.BreakDuration,
                ShouldHandle = static args => ValueTask.FromResult(args.Outcome.Result < 0),
                StateProvider = _state,
                ManualControl = _manual,
            });
            _pipeline = builder.Build();
        }

        public CircuitState State => (CircuitState)(int)_state.CircuitState;

        /// <summary>Returns whether the callback ran, and the rejection's retry-after (null when none or admitted).</summary>
        public async Task<(bool Admitted, TimeSpan? RetryAfter)> Call(bool success)
        {
            var context = P.ResilienceContextPool.Shared.Get();
            var ran = new Flag();
            var outcome = await _pipeline.ExecuteOutcomeAsync(
                static (_, st) =>
                {
                    st.Ran.Value = true;
                    return ValueTask.FromResult(P.Outcome.FromResult(st.Success ? 1 : -1));
                },
                context,
                (Ran: ran, Success: success));
            P.ResilienceContextPool.Shared.Return(context);
            return (ran.Value, (outcome.Exception as PCB.BrokenCircuitException)?.RetryAfter);
        }

        public async Task<(TaskCompletionSource<int>? Gate, Task? Run)> Begin()
        {
            var context = P.ResilienceContextPool.Shared.Get();
            var ran = new Flag();
            var gate = new TaskCompletionSource<int>();
            var run = RunAsync(context, ran, gate);
            if (!ran.Value)
            {
                await run;
                return (null, null);
            }

            return (gate, run);
        }

        private async Task RunAsync(P.ResilienceContext context, Flag ran, TaskCompletionSource<int> gate)
        {
            await _pipeline.ExecuteOutcomeAsync(
                static async (_, st) =>
                {
                    st.Ran.Value = true;
                    return P.Outcome.FromResult(await st.Gate.Task);
                },
                context,
                (Ran: ran, Gate: gate));
            P.ResilienceContextPool.Shared.Return(context);
        }

        public Task Isolate() => _manual.IsolateAsync();

        public Task Close() => _manual.CloseAsync();
    }

    /// <summary>Our pipeline, configured the same way.</summary>
    private sealed class OurSide
    {
        private readonly ResiliencePipeline<int> _pipeline;
        private readonly CircuitBreakerStateProvider _state = new();
        private readonly CircuitBreakerManualControl _manual = new();

        public OurSide(Settings o, TimeProvider time)
        {
            _pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
                {
                    FailureRatio = o.FailureRatio,
                    MinimumThroughput = o.MinimumThroughput,
                    SamplingDuration = o.SamplingDuration,
                    BreakDuration = o.BreakDuration,
                    ShouldHandle = static args => args.Outcome.Result < 0,
                    StateProvider = _state,
                    ManualControl = _manual,
                })
                .Build();
        }

        public CircuitState State => _state.CircuitState;

        public async Task<(bool Admitted, TimeSpan? RetryAfter)> Call(bool success)
        {
            var ran = new Flag();
            var outcome = await _pipeline.TryExecuteAsync(
                static (st, _) =>
                {
                    st.Ran.Value = true;
                    return new ValueTask<int>(st.Success ? 1 : -1);
                },
                (Ran: ran, Success: success));
            return (ran.Value, outcome.RetryAfter);
        }

        public async Task<(TaskCompletionSource<int>? Gate, Task? Run)> Begin()
        {
            var ran = new Flag();
            var gate = new TaskCompletionSource<int>();
            var run = _pipeline.TryExecuteAsync(
                static async (st, _) =>
                {
                    st.Ran.Value = true;
                    return await st.Gate.Task;
                },
                (Ran: ran, Gate: gate)).AsTask();
            if (!ran.Value)
            {
                await run;
                return (null, null);
            }

            return (gate, run);
        }

        public Task Isolate() => _manual.IsolateAsync();

        public Task Close() => _manual.CloseAsync();
    }

    private sealed record InFlight(TaskCompletionSource<int> PollyGate, Task PollyRun, TaskCompletionSource<int> OurGate, Task OurRun);

    private static bool Enter(CircuitController controller, bool success)
    {
        if (controller.TryEnter(out _) == Admission.Rejected)
        {
            return false;
        }

        Record(controller, success);
        return true;
    }

    private static void Record(CircuitController controller, bool success)
    {
        if (success)
        {
            controller.RecordSuccess(CircuitController.NotMeasured, null);
        }
        else
        {
            controller.RecordFailure(null, CircuitController.NotMeasured, null);
        }
    }

    // Polly computes RetryAfter = blockedUntil − now, which goes negative in a stalled half-open period;
    // ours clamps it to zero.
    private static TimeSpan? Clamp(TimeSpan? retryAfter) => retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter;

    public static TheoryData<int> Batches() => [0, 1, 2, 3];

    [Theory]
    [MemberData(nameof(Batches))]
    public async Task RandomScripts_MatchPolly_StepByStep(int batch)
    {
        var failures = new List<string>();
        var perBatch = Scripts / 4;
        for (var seed = batch * perBatch; seed < (batch + 1) * perBatch && failures.Count == 0; seed++)
        {
            await RunScript(seed, failures);
        }

        failures.Should().Equal([]);
    }

    private static async Task RunScript(int seed, List<string> failures)
    {
        var rng = new Random(seed);
        var o = new Settings(
            FailureRatio: new[] { 0.1, 0.5, 0.75, 1.0 }[rng.Next(4)],
            MinimumThroughput: new[] { 2, 4, 10 }[rng.Next(3)],
            SamplingDuration: TimeSpan.FromMilliseconds(new[] { 500, 1000, 3000 }[rng.Next(3)]),
            BreakDuration: TimeSpan.FromMilliseconds(new[] { 500, 1000 }[rng.Next(2)]));
        var time = new FakeTimeProvider();
        var polly = new PollySide(o, time);
        var ours = new OurSide(o, time);
        var controller = new CircuitController(time, o.FailureRatio, o.MinimumThroughput, o.SamplingDuration, o.BreakDuration, stripes: 1 + rng.Next(8));
        var inflight = new List<InFlight>();
        var trace = new List<string>();
        var failP = 30 + rng.Next(50);

        for (var step = 0; step < Steps; step++)
        {
            var r = rng.Next(100);
            string op;
            if (r < 55)
            {
                var ok = rng.Next(100) >= failP;
                op = ok ? "call S" : "call F";
                var p = await polly.Call(ok);
                var u = await ours.Call(ok);
                var c = Enter(controller, ok);
                if (p.Admitted != u.Admitted || p.Admitted != c)
                {
                    failures.Add($"seed {seed} step {step}: admitted polly={p.Admitted} ours={u.Admitted} controller={c}; last ops: {string.Join(", ", trace.TakeLast(12))}");
                    break;
                }

                if (Clamp(p.RetryAfter) != u.RetryAfter)
                {
                    failures.Add($"seed {seed} step {step}: retry-after polly={p.RetryAfter} ours={u.RetryAfter}");
                    break;
                }
            }
            else if (r < 63 && inflight.Count < 3)
            {
                op = "begin";
                var p = await polly.Begin();
                var u = await ours.Begin();
                var c = controller.TryEnter(out _) != Admission.Rejected;
                if ((p.Gate is not null) != (u.Gate is not null) || (p.Gate is not null) != c)
                {
                    failures.Add($"seed {seed} step {step}: begin admitted polly={p.Gate is not null} ours={u.Gate is not null} controller={c}");
                    break;
                }

                if (p.Gate is not null)
                {
                    inflight.Add(new InFlight(p.Gate, p.Run!, u.Gate!, u.Run!));
                }
            }
            else if (r < 71 && inflight.Count > 0)
            {
                var ok = rng.Next(100) >= failP;
                op = ok ? "end S" : "end F";
                var i = rng.Next(inflight.Count);
                var call = inflight[i];
                inflight.RemoveAt(i);
                call.PollyGate.SetResult(ok ? 1 : -1);
                await call.PollyRun;
                call.OurGate.SetResult(ok ? 1 : -1);
                await call.OurRun;
                Record(controller, ok);
            }
            else if (r < 97)
            {
                var ms = rng.Next(4) switch
                {
                    0 => rng.Next(1, 20),
                    1 => rng.Next(20, 200),
                    2 => (int)o.BreakDuration.TotalMilliseconds,
                    _ => rng.Next(200, 1500),
                };
                op = $"+{ms}ms";
                time.Advance(TimeSpan.FromMilliseconds(ms));
            }
            else if (r < 98)
            {
                op = "isolate";
                await polly.Isolate();
                await ours.Isolate();
                controller.Isolate();
            }
            else
            {
                op = "close";
                await polly.Close();
                await ours.Close();
                controller.Close();
            }

            trace.Add(op);
            if (polly.State != ours.State || polly.State != controller.State)
            {
                failures.Add($"seed {seed} step {step} after '{op}': polly={polly.State} ours={ours.State} controller={controller.State}; last ops: {string.Join(", ", trace.TakeLast(12))}");
                break;
            }
        }

        foreach (var call in inflight)
        {
            call.PollyGate.TrySetResult(1);
            call.OurGate.TrySetResult(1);
            await call.PollyRun;
            await call.OurRun;
        }
    }
}
