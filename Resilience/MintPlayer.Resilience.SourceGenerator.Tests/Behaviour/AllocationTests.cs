using System.Runtime.CompilerServices;
using ResilienceSamples;

namespace MintPlayer.Resilience.SourceGenerator.Tests.Behaviour;

/// <summary>
/// The generated paths allocate nothing (plan S1/S2): the sync path, the sync-completing async path, and the pooled async
/// path when the callback really suspends (0 B above the callback's own state-machine box). Measured after a warm-up
/// that fills the pools and the timers of the pooled CTSs; the collection runs alone.
/// </summary>
[Collection(nameof(GeneratedPipelines))]
public class AllocationTests
{
    private const int Warmup = 500;
    private const int Iterations = 5_000;

    private static readonly Func<int, CancellationToken, ValueTask<int>> Callback = static (state, _) => new ValueTask<int>(state + 1);

    private static long MeasureThread(Action action)
    {
        for (var i = 0; i < Warmup; i++)
        {
            action();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            action();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void Expect(ValueTask<int> task, int expected)
    {
        if (!task.IsCompletedSuccessfully || task.Result != expected)
        {
            throw new InvalidOperationException("The execution did not complete synchronously with the expected result.");
        }
    }

    [Fact]
    public void ExecuteAsync_SyncCompletingHappyPath_AllocatesNothing()
    {
        AllocationPipeline.UseTimeProvider(TimeProvider.System);

        MeasureThread(() => Expect(AllocationPipeline.ExecuteAsync(Callback, 41), 42)).Should().Be(0);
    }

    [Fact]
    public void TryExecuteAsync_SyncCompletingHappyPath_AllocatesNothing()
    {
        AllocationPipeline.UseTimeProvider(TimeProvider.System);

        MeasureThread(() =>
        {
            var task = AllocationPipeline.TryExecuteAsync(Callback, 41);
            if (!task.IsCompletedSuccessfully || task.Result.Result != 42)
            {
                throw new InvalidOperationException("The execution did not complete synchronously with the expected result.");
            }
        }).Should().Be(0);
    }

    [Fact]
    public void Execute_SyncPath_AllocatesNothing()
    {
        AllocationPipeline.UseTimeProvider(TimeProvider.System);

        MeasureThread(() =>
        {
            if (AllocationPipeline.Execute(static (state, _) => state + 1, 41) != 42)
            {
                throw new InvalidOperationException("Unexpected result.");
            }
        }).Should().Be(0);
    }

    [Fact]
    public void Execute_OneRetry_AllocatesNothing()
    {
        RetryFallbackPipeline.UseTimeProvider(TimeProvider.System);

        MeasureThread(() =>
        {
            // The first attempt returns the handled -1, the retry succeeds.
            var box = new StrongBox<int>();
            if (RetryFallbackPipeline.Execute(static (b, _) => ++b.Value == 1 ? -1 : 7, box) != 7)
            {
                throw new InvalidOperationException("Unexpected result.");
            }
        }).Should().Be(Iterations * StrongBoxSize(), "only the test's own state box allocates");
    }

    [Fact]
    public void Fallback_AllocatesNothing()
    {
        RetryFallbackPipeline.UseTimeProvider(TimeProvider.System);

        // Every attempt returns the handled -1: 4 attempts, then the fallback's 0.
        MeasureThread(() => Expect(RetryFallbackPipeline.ExecuteAsync(static (_, _) => new ValueTask<int>(-1), 0), 0)).Should().Be(0);
    }

    [Fact]
    public void EmptyPipeline_AllocatesNothing()
        => MeasureThread(() => Expect(EmptyPipeline.ExecuteAsync(Callback, 1), 2)).Should().Be(0);

    [Fact]
    public void GenericPipeline_SyncCompletingPath_AllocatesNothing()
    {
        GenericPipeline.UseTimeProvider(TimeProvider.System);

        // A reference-type result: the breaker of a generic pipeline sees results as object (as in Polly's non-generic
        // pipeline), so a value-type result is boxed whenever its predicate runs.
        MeasureThread(() =>
        {
            var task = GenericPipeline.ExecuteAsync(static (s, _) => new ValueTask<string>(s), "text");
            if (!task.IsCompletedSuccessfully || !ReferenceEquals(task.Result, "text"))
            {
                throw new InvalidOperationException("The execution did not complete synchronously with the expected result.");
            }
        }).Should().Be(0);
        MeasureThread(() => GenericPipeline.Execute(static (_, _) => { }, 0)).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_SuspendingCallback_AllocatesNothingAboveTheCallback()
    {
        AllocationPipeline.UseTimeProvider(TimeProvider.System);
        static async ValueTask<int> Suspending(int state, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return state + 1;
        }

        var callbackOnly = await MeasureTotalAsync(static () => Suspending(1, default));
        var throughPipeline = await MeasureTotalAsync(static () => AllocationPipeline.ExecuteAsync(static (s, ct) => Suspending(s, ct), 1));

        // S2 measured exactly +0 B; a few bytes of thread-pool noise per thousand operations are tolerated.
        var perOperation = (double)(throughPipeline - callbackOnly) / Iterations;
        perOperation.Should().BeLessThanOrEqualTo(1.0, $"the pooled flat method must add nothing to the callback's own {callbackOnly / (double)Iterations:F0} B box");
    }

    private static async Task<long> MeasureTotalAsync(Func<ValueTask<int>> operation)
    {
        for (var i = 0; i < Warmup; i++)
        {
            await operation();
        }

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < Iterations; i++)
        {
            await operation();
        }

        return GC.GetTotalAllocatedBytes(precise: true) - before;
    }

    private static long StrongBoxSize()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(new StrongBox<int>());
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
