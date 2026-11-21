using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;

namespace MintPlayer.Resilience.Tests;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that reports every finite timer it creates. <c>Task.Delay(delay, provider)</c>
/// creates exactly one such timer, so a test can wait until a retry has scheduled its delay, read the
/// delay, and advance the clock by exactly that much. Timeout CTSs are created with an infinite due
/// time and re-armed through <c>Change</c>, so they are not reported.
/// </summary>
internal sealed class DelayRecordingTimeProvider : FakeTimeProvider
{
    private readonly SemaphoreSlim _scheduled = new(0);
    private readonly ConcurrentQueue<TimeSpan> _delays = new();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        if (dueTime != global::System.Threading.Timeout.InfiniteTimeSpan)
        {
            _delays.Enqueue(dueTime);
            _scheduled.Release();
        }

        return timer;
    }

    /// <summary>Waits until the next finite timer is created and returns its due time.</summary>
    public async Task<TimeSpan> NextDelayAsync()
    {
        if (!await _scheduled.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("No delay was scheduled within 10 s.");
        }

        _delays.TryDequeue(out var delay);
        return delay;
    }

    /// <summary>Waits for the next delay and advances the clock by exactly that amount.</summary>
    public async Task<TimeSpan> AdvanceNextDelayAsync()
    {
        var delay = await NextDelayAsync();
        Advance(delay);
        return delay;
    }
}

/// <summary>A result type whose disposal can be observed.</summary>
internal sealed class DisposableResult(int value) : IDisposable
{
    public int Value { get; } = value;

    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}

/// <summary>A deterministic randomizer that returns the given values in turn (the last one repeats).</summary>
internal sealed class SequenceRandomizer(params double[] values)
{
    private int _index;

    public double Next() => values[Math.Min(_index++, values.Length - 1)];
}
