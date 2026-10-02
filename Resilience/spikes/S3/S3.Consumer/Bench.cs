namespace Spike.S3;

/// <summary>
/// Allocation cases. Each runs n call sites in a loop, with the captured variable declared INSIDE the loop,
/// so a closure (if any) is created per call. The callback completes synchronously.
/// "Plain" = [NoIntercept]; "Intercepted" = identical code, generator interceptor applied;
/// "Rewritten" = the generator's source rewrite pasted in (what a code fix would produce).
/// </summary>
public sealed class Bench
{
    private readonly int _offset = 1;

    public static long NonCapturing(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) s += Pipeline.ExecuteAsync(c => new ValueTask<int>(1), default).Result;
        return s;
    }

    [NoIntercept]
    public static long Local_Plain(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; s += Pipeline.ExecuteAsync(c => new ValueTask<int>(id), default).Result; }
        return s;
    }

    public static long Local_Intercepted(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; s += Pipeline.ExecuteAsync(c => new ValueTask<int>(id), default).Result; }
        return s;
    }

    public static long Local_Rewritten(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; s += Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(__s), id, default).Result; }
        return s;
    }

    [NoIntercept]
    public long This_Plain(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) s += Pipeline.ExecuteAsync(c => new ValueTask<int>(_offset), default).Result;
        return s;
    }

    public long This_Intercepted(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) s += Pipeline.ExecuteAsync(c => new ValueTask<int>(_offset), default).Result;
        return s;
    }

    public long This_Rewritten(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) s += Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(__s._offset), this, default).Result;
        return s;
    }

    [NoIntercept]
    public long TwoLocalsThis_Plain(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; string name = "ab"; s += Pipeline.ExecuteAsync(c => new ValueTask<int>(id + name.Length + _offset), default).Result; }
        return s;
    }

    public long TwoLocalsThis_Intercepted(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; string name = "ab"; s += Pipeline.ExecuteAsync(c => new ValueTask<int>(id + name.Length + _offset), default).Result; }
        return s;
    }

    public long TwoLocalsThis_Rewritten(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; string name = "ab"; s += Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(__s.id + __s.name.Length + __s.self._offset), (id: id, name: name, self: this), default).Result; }
        return s;
    }

    [NoIntercept]
    public static long AsyncLambda_Plain(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; s += Pipeline.ExecuteAsync(async c => { await Task.CompletedTask; return id; }, default).Result; }
        return s;
    }

    public static long AsyncLambda_Intercepted(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; s += Pipeline.ExecuteAsync(async c => { await Task.CompletedTask; return id; }, default).Result; }
        return s;
    }

    public static long AsyncLambda_Rewritten(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; s += Pipeline.ExecuteAsync(static async (__s, c) => { await Task.CompletedTask; return __s; }, id, default).Result; }
        return s;
    }

    [NoIntercept]
    public static long Shared_Plain(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; Func<int> other = () => id; s += Pipeline.ExecuteAsync(c => new ValueTask<int>(id), default).Result; if (s < 0) s += other(); }
        return s;
    }

    public static long Shared_Rewritten(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; Func<int> other = () => id; s += Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(__s), id, default).Result; if (s < 0) s += other(); }
        return s;
    }

    [NoIntercept]
    public static async Task<long> AsyncCaller_Plain(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; s += await Pipeline.ExecuteAsync(c => new ValueTask<int>(id), default); }
        return s;
    }

    public static async Task<long> AsyncCaller_Intercepted(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; s += await Pipeline.ExecuteAsync(c => new ValueTask<int>(id), default); }
        return s;
    }

    public static async Task<long> AsyncCaller_Rewritten(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; s += await Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(__s), id, default); }
        return s;
    }

    public static long InlinablePipeline_Local(int n)
    {
        long s = 0;
        for (var i = 0; i < n; i++) { int id = i; s += InlinablePipeline.ExecuteAsync(c => new ValueTask<int>(id), default).Result; }
        return s;
    }
}
