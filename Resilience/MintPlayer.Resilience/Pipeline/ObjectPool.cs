using System.Collections.Concurrent;

namespace MintPlayer.Resilience.Pipeline;

/// <summary>
/// A bounded, thread-safe pool: one lock-free fast slot plus a shared queue. 0 B in steady state,
/// including when a rented object is returned on another thread (S2).
/// </summary>
internal sealed class ObjectPool<T>(Func<T> factory)
    where T : class
{
    private static readonly int MaxRetained = Math.Max(16, Environment.ProcessorCount * 4);

    private readonly ConcurrentQueue<T> _items = new();
    private T? _fast;
    private int _count;

    public T Get()
    {
        var item = _fast;
        if (item is not null && Interlocked.CompareExchange(ref _fast, null, item) == item)
        {
            return item;
        }

        if (_items.TryDequeue(out item))
        {
            Interlocked.Decrement(ref _count);
            return item;
        }

        return factory();
    }

    public void Return(T item)
    {
        if (_fast is null && Interlocked.CompareExchange(ref _fast, item, null) is null)
        {
            return;
        }

        if (Interlocked.Increment(ref _count) <= MaxRetained)
        {
            _items.Enqueue(item);
            return;
        }

        Interlocked.Decrement(ref _count);
    }
}

/// <summary>
/// Pooled <see cref="CancellationTokenSource"/> instances bound to one <see cref="TimeProvider"/>, reset
/// with <see cref="CancellationTokenSource.TryReset"/>. Shared by the runtime interpreter and (M4) the
/// generated pipelines.
/// </summary>
internal sealed class CancellationTokenSourcePool
{
    private static readonly CancellationTokenSourcePool SystemPool = new(TimeProvider.System);

    private readonly ObjectPool<CancellationTokenSource> _pool;

    private CancellationTokenSourcePool(TimeProvider timeProvider)
        => _pool = new(() => new CancellationTokenSource(System.Threading.Timeout.InfiniteTimeSpan, timeProvider));

    /// <summary>The pool for <paramref name="timeProvider"/>: shared for the system clock, a new one otherwise.</summary>
    public static CancellationTokenSourcePool For(TimeProvider timeProvider)
        => ReferenceEquals(timeProvider, TimeProvider.System) ? SystemPool : new(timeProvider);

    /// <summary>Rents a source that cancels itself after <paramref name="delay"/> (never, for an infinite delay).</summary>
    public CancellationTokenSource Rent(TimeSpan delay)
    {
        var source = _pool.Get();
        if (delay != System.Threading.Timeout.InfiniteTimeSpan)
        {
            source.CancelAfter(delay);
        }

        return source;
    }

    /// <summary>Returns a source. A cancelled source cannot be reset and is disposed instead.</summary>
    public void Return(CancellationTokenSource source)
    {
        if (source.TryReset())
        {
            _pool.Return(source);
        }
        else
        {
            source.Dispose();
        }
    }
}

/// <summary>The result type of a void execution. A class, so the interpreter runs on shared generic code.</summary>
internal sealed class VoidResult
{
    public static readonly VoidResult Instance = new();

    private VoidResult()
    {
    }

    public override string ToString() => "void";
}
