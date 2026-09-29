using Polly;
using Polly.CircuitBreaker;

namespace S4;

/// <summary>Polly 8.8.0 pipeline with ONLY a circuit breaker; failures are handled results (-1), no throws.</summary>
public sealed class PollyBreaker
{
    public readonly ResiliencePipeline<int> Pipeline;
    private readonly CircuitBreakerStateProvider _state = new();
    private readonly CircuitBreakerManualControl _manual = new();
    private readonly Dictionary<int, (TaskCompletionSource<int> Tcs, Task<Outcome<int>> Run)> _inflight = new();
    private int _nextId;

    public PollyBreaker(CbOptions o, TimeProvider time)
    {
        Pipeline = new ResiliencePipelineBuilder<int> { TimeProvider = time }
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
            {
                FailureRatio = o.FailureRatio,
                MinimumThroughput = o.MinimumThroughput,
                SamplingDuration = o.SamplingDuration,
                BreakDuration = o.BreakDuration,
                ShouldHandle = static args => ValueTask.FromResult(args.Outcome.Result < 0),
                StateProvider = _state,
                ManualControl = _manual,
            })
            .Build();
    }

    public CbState State => (CbState)(int)_state.CircuitState;

    /// <summary>Returns whether the callback ran (i.e. was admitted).</summary>
    public async Task<bool> Call(bool success)
    {
        var ctx = ResilienceContextPool.Shared.Get();
        var ran = new Flag();
        await Pipeline.ExecuteOutcomeAsync(static (_, st) =>
        {
            st.Box.Value = true;
            return ValueTask.FromResult(Outcome.FromResult(st.Success ? 1 : -1));
        }, ctx, (Box: ran, Success: success));
        ResilienceContextPool.Shared.Return(ctx);
        return ran.Value;
    }

    /// <summary>Starts an in-flight call. Returns its id, or -1 when rejected.</summary>
    public async Task<int> Begin()
    {
        var ctx = ResilienceContextPool.Shared.Get();
        var ran = new Flag();
        var tcs = new TaskCompletionSource<int>();
        var run = Pipeline.ExecuteOutcomeAsync(static async (_, st) =>
        {
            st.Box.Value = true;
            return Outcome.FromResult(await st.Tcs.Task);
        }, ctx, (Box: ran, Tcs: tcs)).AsTask();

        if (!ran.Value)
        {
            await run;
            return -1;
        }

        var id = _nextId++;
        _inflight[id] = (tcs, run);
        return id;
    }

    public async Task End(int id, bool success)
    {
        var (tcs, run) = _inflight[id];
        _inflight.Remove(id);
        tcs.SetResult(success ? 1 : -1);
        await run;
    }

    public Task Isolate() => _manual.IsolateAsync();
    public Task Close() => _manual.CloseAsync();
}

public sealed class Flag { public bool Value; }
