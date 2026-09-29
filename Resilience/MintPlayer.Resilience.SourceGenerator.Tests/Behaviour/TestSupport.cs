using Microsoft.Extensions.Time.Testing;

namespace MintPlayer.Resilience.SourceGenerator.Tests.Behaviour;

/// <summary>What one scripted attempt does.</summary>
public enum Step
{
    /// <summary>Returns the step's value.</summary>
    Return,

    /// <summary>Throws an <see cref="InvalidOperationException"/> (handled by the sample predicates).</summary>
    Throw,

    /// <summary>Throws an <see cref="ArgumentException"/> (handled by no sample predicate).</summary>
    ThrowUnhandled,

    /// <summary>Waits on the attempt's token until it is cancelled (a timeout fires).</summary>
    Hang,
}

/// <summary>A scripted callback: attempt n does step n (the last step repeats), and the attempts are counted.</summary>
public sealed class Script
{
    private readonly (Step Step, int Value)[] _steps;
    private int _next;

    public Script(params (Step Step, int Value)[] steps) => _steps = steps;

    public int Attempts => Volatile.Read(ref _next);

    public static (Step, int) Ok(int value) => (Step.Return, value);

    public static (Step, int) Fail => (Step.Throw, 0);

    public static (Step, int) Unhandled => (Step.ThrowUnhandled, 0);

    public static (Step, int) Hang => (Step.Hang, 0);

    /// <summary>The callback shape every overload under test takes: a static lambda over this script.</summary>
    public static readonly Func<Script, CancellationToken, ValueTask<int>> Callback = static (script, ct) => script.InvokeAsync(ct);

    public async ValueTask<int> InvokeAsync(CancellationToken cancellationToken)
    {
        var index = Interlocked.Increment(ref _next) - 1;
        var (step, value) = _steps[Math.Min(index, _steps.Length - 1)];
        switch (step)
        {
            case Step.Throw:
                throw new InvalidOperationException($"scripted failure {index}");
            case Step.ThrowUnhandled:
                throw new ArgumentException($"scripted unhandled failure {index}");
            case Step.Hang:
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return value;
            default:
                return value;
        }
    }
}

/// <summary>Drives executions that wait on a <see cref="FakeTimeProvider"/> (retry delays, timeouts) to completion.</summary>
public static class FakeClock
{
    /// <summary>
    /// Advances <paramref name="time"/> in 10 ms steps until <paramref name="execution"/> completes, letting continuations
    /// run between steps. Both pipelines of a parity test are driven this way, so they see the same timeline.
    /// </summary>
    public static async Task<T> DriveAsync<T>(FakeTimeProvider time, ValueTask<T> execution)
    {
        var task = execution.AsTask();
        for (var i = 0; i < 20_000 && !task.IsCompleted; i++)
        {
            await Task.WhenAny(task, Task.Delay(2)).ConfigureAwait(false);
            if (!task.IsCompleted)
            {
                time.Advance(TimeSpan.FromMilliseconds(10));
            }
        }

        if (!task.IsCompleted)
        {
            throw new TimeoutException("The execution did not complete within 200 s of fake time.");
        }

        return await task.ConfigureAwait(false);
    }

    /// <summary>A fresh fake clock at a fixed start, so both pipelines of a comparison start from the same instant.</summary>
    public static FakeTimeProvider Create() => new(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
}

/// <summary>Formats outcomes the same way for both pipelines of a comparison.</summary>
public static class Transcript
{
    public static string Of(Outcome<int> outcome, int attempts) => outcome.IsRejected
        ? $"rejected:{outcome.Rejection} attempts={attempts}"
        : outcome.IsSuccess
            ? $"result:{outcome.Result} attempts={attempts}"
            : $"exception:{outcome.Exception!.GetType().Name} attempts={attempts}";
}
