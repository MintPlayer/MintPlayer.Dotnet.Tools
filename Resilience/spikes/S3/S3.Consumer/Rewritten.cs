namespace Spike.S3;

// The generator's rewrite strings (S3SiteReport.Sites[*].Rewrite), pasted verbatim into a partial part of
// the SAME class: this is what an analyzer code fix would do in place, so private members (Work, Helper,
// _offset) stay accessible. It compiling at all is part of the result. The rewritten calls take 3 args,
// so the generator does not touch them.
public sealed partial class Shapes
{
    public int R_ReadOnlyLocals(CancellationToken ct)
    {
        int id = 40;
        string name = "ab";
        return Pipeline.ExecuteAsync(static (__s, c) => Work(__s.id, __s.name, c), (id: id, name: name), ct).Result;
    }

    public int R_CapturedParameters(int id, string name, CancellationToken ct)
        => Pipeline.ExecuteAsync(static (__s, c) => Work(__s.id, __s.name, c), (id: id, name: name), ct).Result;

    public int R_ThisImplicit(CancellationToken ct)
        => Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(__s._offset + __s.Helper()), this, ct).Result;

    public int R_ThisExplicitAndLocals(CancellationToken ct)
    {
        int id = 2;
        return Pipeline.ExecuteAsync(static (__s, c) => Work(__s.self._offset + __s.id, nameof(id), c), (id: id, self: this), ct).Result;
    }

    // NAIVE (wrong) rewrites of the write-blocked shapes: they must produce a DIFFERENT result.
    public async Task<int> R_MutatedAfterCall(CancellationToken ct)
    {
        int n = 1;
        var gate = new TaskCompletionSource();
        var vt = Pipeline.ExecuteAsync(static async (__s, c) => { await __s.gate.Task; return __s.n; }, (n: n, gate: gate), ct);
        n = 2;
        gate.SetResult();
        return await vt;
    }

    public int R_MutatedInsideLambda(CancellationToken ct)
    {
        int count = 0;
        var r = Pipeline.ExecuteAsync(static (__s, c) => { __s++; return new ValueTask<int>(__s); }, count, ct).Result;
        return r * 10 + count;
    }

    public int R_MutableStructCapture(CancellationToken ct)
    {
        var counter = new MutableCounter();
        var a = Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(__s.Next()), counter, ct).Result;
        return a * 10 + counter.Next();
    }

    public int R_RefToCapturedInsideLambda(CancellationToken ct)
    {
        int counter = 0;
        var r = Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(Interlocked.Increment(ref __s)), counter, ct).Result;
        return r * 10 + counter;
    }

    public int R_ReadonlyStructCapture(CancellationToken ct)
    {
        var price = new Money(250);
        return Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(__s.Cents + 1), price, ct).Result;
    }

    public int R_NestedStaticLambda(CancellationToken ct)
    {
        int id = 3;
        return Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(Apply(static () => 1) + __s), id, ct).Result;
    }

    public async Task<int> R_AsyncLambda(CancellationToken ct)
    {
        int id = 8;
        string name = "abc";
        return await Pipeline.ExecuteAsync(static async (__s, c) => { await Task.Yield(); return __s.id + __s.name.Length; }, (id: id, name: name), ct);
    }

    public int R_GenericMethod<TItem>(TItem item, Func<TItem, int> selector, CancellationToken ct)
        => Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(__s.selector(__s.item)), (selector: selector, item: item), ct).Result;

    public TItem R_GenericResult<TItem>(TItem item, CancellationToken ct)
        => Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<TItem>(__s), item, ct).Result;

    public int R_OutVarInsideLambda(CancellationToken ct)
    {
        string text = "42";
        return Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(int.TryParse(__s, out var v) ? v : -1), text, ct).Result;
    }

    public int R_SharedWithOtherClosure(CancellationToken ct)
    {
        int id = 3;
        Func<int> other = () => id * 2;
        return Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(__s.id + __s.other()), (id: id, other: other), ct).Result;
    }

    public int R_LoopVariable(CancellationToken ct)
    {
        var sum = 0;
        foreach (var x in new[] { 1, 2, 3 })
            sum += Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(__s * 2), x, ct).Result;
        return sum;
    }

    public int R_TypedLambdaParameter(CancellationToken ct)
    {
        int id = 4;
        string name = "x";
        return Pipeline.ExecuteAsync(static ((int id, string name) __s, CancellationToken c) => Work(__s.id, __s.name, c), (id: id, name: name), ct).Result;
    }

    public int R_InsideOuterLambda(CancellationToken ct)
    {
        int id = 10;
        return Enumerable.Range(0, 3).Sum(i => Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(__s.i + __s.id), (i: i, id: id), ct).Result);
    }

    public int R_AnonymousTypeProjection(CancellationToken ct)
    {
        int id = 5;
        string name = "nm";
        return Pipeline.ExecuteAsync(static (__s, c) => { var o = new { id = __s.id, name = __s.name }; var t = (id: __s.id, name: __s.name); return new ValueTask<int>(o.id + t.name.Length); }, (id: id, name: name), ct).Result;
    }

    public int R_StaticLocalFunction(CancellationToken ct)
    {
        int id = 2;
        static int Twice(int v) => v * 2;
        return Pipeline.ExecuteAsync(static (__s, c) => new ValueTask<int>(Twice(__s)), id, ct).Result;
    }

    public int R_CapturedCtOfOuterMethod(CancellationToken ct)
        => Pipeline.ExecuteAsync(static (__s, _) => new ValueTask<int>(__s.CanBeCanceled ? 1 : 0), ct, ct).Result;
}

public static class Rewritten
{
    public static async Task<List<(string Name, object? Actual, object? Expected, bool ExpectMatch)>> RunAll(CancellationToken ct)
    {
        var s = new Shapes(10);
        var list = new List<(string, object?, object?, bool)>();
        void Add<T>(string name, T actual, T closure, bool expectMatch = true) => list.Add((name, actual, closure, expectMatch));

        Add("ReadOnlyLocals", s.R_ReadOnlyLocals(ct), s.ReadOnlyLocals(ct));
        Add("CapturedParameters", s.R_CapturedParameters(40, "ab", ct), s.CapturedParameters(40, "ab", ct));
        Add("ThisImplicit", s.R_ThisImplicit(ct), s.ThisImplicit(ct));
        Add("ThisExplicitAndLocals", s.R_ThisExplicitAndLocals(ct), s.ThisExplicitAndLocals(ct));
        Add("MutatedAfterCall (naive)", await s.R_MutatedAfterCall(ct), await s.MutatedAfterCall(ct), expectMatch: false);
        Add("MutatedInsideLambda (naive)", s.R_MutatedInsideLambda(ct), s.MutatedInsideLambda(ct), expectMatch: false);
        Add("MutableStructCapture (naive)", s.R_MutableStructCapture(ct), s.MutableStructCapture(ct), expectMatch: false);
        Add("RefToCapturedInsideLambda (naive)", s.R_RefToCapturedInsideLambda(ct), s.RefToCapturedInsideLambda(ct), expectMatch: false);
        Add("ReadonlyStructCapture", s.R_ReadonlyStructCapture(ct), s.ReadonlyStructCapture(ct));
        Add("NestedStaticLambda", s.R_NestedStaticLambda(ct), s.NestedStaticLambda(ct));
        Add("AsyncLambda", await s.R_AsyncLambda(ct), await s.AsyncLambda(ct));
        Add("GenericMethod", s.R_GenericMethod("abcd", x => x.Length, ct), s.GenericMethod("abcd", x => x.Length, ct));
        Add("GenericResult", s.R_GenericResult("r", ct), s.GenericResult("r", ct));
        Add("OutVarInsideLambda", s.R_OutVarInsideLambda(ct), s.OutVarInsideLambda(ct));
        Add("SharedWithOtherClosure", s.R_SharedWithOtherClosure(ct), s.SharedWithOtherClosure(ct));
        Add("LoopVariable", s.R_LoopVariable(ct), s.LoopVariable(ct));
        Add("TypedLambdaParameter", s.R_TypedLambdaParameter(ct), s.TypedLambdaParameter(ct));
        Add("InsideOuterLambda", s.R_InsideOuterLambda(ct), s.InsideOuterLambda(ct));
        Add("AnonymousTypeProjection", s.R_AnonymousTypeProjection(ct), s.AnonymousTypeProjection(ct));
        Add("StaticLocalFunction", s.R_StaticLocalFunction(ct), s.StaticLocalFunction(ct));
        Add("CapturedCtOfOuterMethod", s.R_CapturedCtOfOuterMethod(ct), s.CapturedCtOfOuterMethod(ct));
        return list;
    }
}
