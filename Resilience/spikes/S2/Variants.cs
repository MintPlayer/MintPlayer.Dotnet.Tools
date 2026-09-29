using System.Runtime.CompilerServices;
using S1;

namespace S2;

public static class Variants
{
    public static readonly Func<object?, CancellationToken, ValueTask<int>> AsyncCallback =
        static async (_, _) => { await Task.Yield(); return 42; };

    // Builder alone: no CTS, no breaker. Isolates what PoolingAsyncValueTaskMethodBuilder itself leaves behind.
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<int> PooledWrap(Func<object?, CancellationToken, ValueTask<int>> cb, object? s, CancellationToken ct) =>
        await cb(s, ct).ConfigureAwait(false);

    public static async ValueTask<int> PlainWrap(Func<object?, CancellationToken, ValueTask<int>> cb, object? s, CancellationToken ct) =>
        await cb(s, ct).ConfigureAwait(false);

    // Builder + two pooled CTS (the timeouts), no breaker/retry.
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<int> PooledWithCts(Func<object?, CancellationToken, ValueTask<int>> cb, object? s, CancellationToken ct)
    {
        var outer = CtsPool.Rent(Config.OuterTimeout, ct);
        try
        {
            var inner = CtsPool.Rent(Config.InnerTimeout, outer.Cts.Token);
            try { return await cb(s, inner.Cts.Token).ConfigureAwait(false); }
            finally { CtsPool.Return(inner); }
        }
        finally { CtsPool.Return(outer); }
    }
}
