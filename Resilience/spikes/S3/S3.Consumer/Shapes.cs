namespace Spike.S3;

public struct MutableCounter
{
    private int _value;
    public int Next() => ++_value;
}

public readonly record struct Money(int Cents);

public class ShapesBase
{
    public virtual int Virt() => 100;
}

/// <summary>One method per capture shape. Each calls Pipeline.ExecuteAsync(ct => ..., ct) exactly once.</summary>
public sealed partial class Shapes : ShapesBase
{
    private readonly int _offset;
    public Shapes(int offset) => _offset = offset;

    private static ValueTask<int> Work(int a, string b, CancellationToken ct) => new(a + b.Length);
    private int Helper() => 1;
    private static int Apply(Func<int> f) => f();
    public override int Virt() => 7;
    private static ValueTask<int> StaticWork(CancellationToken ct) => new(5);
    private ValueTask<int> InstanceWork(CancellationToken ct) => new(_offset);

    public int ReadOnlyLocals(CancellationToken ct)
    {
        int id = 40;
        string name = "ab";
        return Pipeline.ExecuteAsync(c => Work(id, name, c), ct).Result;
    }

    public int CapturedParameters(int id, string name, CancellationToken ct)
        => Pipeline.ExecuteAsync(c => Work(id, name, c), ct).Result;

    public int ThisImplicit(CancellationToken ct)
        => Pipeline.ExecuteAsync(c => new ValueTask<int>(_offset + Helper()), ct).Result;

    public int ThisExplicitAndLocals(CancellationToken ct)
    {
        int id = 2;
        return Pipeline.ExecuteAsync(c => Work(this._offset + id, nameof(id), c), ct).Result;
    }

    public async Task<int> MutatedAfterCall(CancellationToken ct)
    {
        int n = 1;
        var gate = new TaskCompletionSource();
        var vt = Pipeline.ExecuteAsync(async c => { await gate.Task; return n; }, ct);
        n = 2;
        gate.SetResult();
        return await vt; // closure: 2
    }

    public int MutatedInsideLambda(CancellationToken ct)
    {
        int count = 0;
        var r = Pipeline.ExecuteAsync(c => { count++; return new ValueTask<int>(count); }, ct).Result;
        return r * 10 + count; // closure: 11
    }

    public int WrittenBeforeCallOnly(CancellationToken ct)
    {
        int n = 1;
        n += 4;
        return Pipeline.ExecuteAsync(c => new ValueTask<int>(n), ct).Result;
    }

    public int MutableStructCapture(CancellationToken ct)
    {
        var counter = new MutableCounter();
        var a = Pipeline.ExecuteAsync(c => new ValueTask<int>(counter.Next()), ct).Result;
        return a * 10 + counter.Next(); // closure: 12
    }

    public int ReadonlyStructCapture(CancellationToken ct)
    {
        var price = new Money(250);
        return Pipeline.ExecuteAsync(c => new ValueTask<int>(price.Cents + 1), ct).Result;
    }

    public int NestedLambdaUsingCapture(CancellationToken ct)
    {
        int id = 3;
        return Pipeline.ExecuteAsync(c => new ValueTask<int>(Apply(() => id + 1)), ct).Result;
    }

    public int NestedStaticLambda(CancellationToken ct)
    {
        int id = 3;
        return Pipeline.ExecuteAsync(c => new ValueTask<int>(Apply(static () => 1) + id), ct).Result;
    }

    public async Task<int> AsyncLambda(CancellationToken ct)
    {
        int id = 8;
        string name = "abc";
        return await Pipeline.ExecuteAsync(async c => { await Task.Yield(); return id + name.Length; }, ct);
    }

    public int AsyncLambdaSyncCompleting(CancellationToken ct)
    {
        int id = 8;
        return Pipeline.ExecuteAsync(async c => { await Task.CompletedTask; return id; }, ct).Result;
    }

    public int GenericMethod<TItem>(TItem item, Func<TItem, int> selector, CancellationToken ct)
        => Pipeline.ExecuteAsync(c => new ValueTask<int>(selector(item)), ct).Result;

    public TItem GenericResult<TItem>(TItem item, CancellationToken ct)
        => Pipeline.ExecuteAsync(c => new ValueTask<TItem>(item), ct).Result;

    public int StaticMethodGroup(CancellationToken ct) => Pipeline.ExecuteAsync(StaticWork, ct).Result;

    public int InstanceMethodGroup(CancellationToken ct) => Pipeline.ExecuteAsync(InstanceWork, ct).Result;

    public int OutVarInsideLambda(CancellationToken ct)
    {
        string text = "42";
        return Pipeline.ExecuteAsync(c => new ValueTask<int>(int.TryParse(text, out var v) ? v : -1), ct).Result;
    }

    public int RefToCapturedInsideLambda(CancellationToken ct)
    {
        int counter = 0;
        var r = Pipeline.ExecuteAsync(c => new ValueTask<int>(Interlocked.Increment(ref counter)), ct).Result;
        return r * 10 + counter; // closure: 11
    }

    public int SharedWithOtherClosure(CancellationToken ct)
    {
        int id = 3;
        Func<int> other = () => id * 2;
        return Pipeline.ExecuteAsync(c => new ValueTask<int>(id + other()), ct).Result;
    }

    public int LoopVariable(CancellationToken ct)
    {
        var sum = 0;
        foreach (var x in new[] { 1, 2, 3 })
            sum += Pipeline.ExecuteAsync(c => new ValueTask<int>(x * 2), ct).Result;
        return sum;
    }

    public int NonCapturingLambda(CancellationToken ct) => Pipeline.ExecuteAsync(c => new ValueTask<int>(9), ct).Result;

    public int BaseAccess(CancellationToken ct) => Pipeline.ExecuteAsync(c => new ValueTask<int>(base.Virt()), ct).Result;

    public int TypedLambdaParameter(CancellationToken ct)
    {
        int id = 4;
        string name = "x";
        return Pipeline.ExecuteAsync((CancellationToken c) => Work(id, name, c), ct).Result;
    }

    public int AnonymousMethod(CancellationToken ct)
    {
        int id = 6;
        return Pipeline.ExecuteAsync(delegate (CancellationToken c) { return new ValueTask<int>(id); }, ct).Result;
    }

    public int InsideOuterLambda(CancellationToken ct)
    {
        int id = 10;
        return Enumerable.Range(0, 3).Sum(i => Pipeline.ExecuteAsync(c => new ValueTask<int>(i + id), ct).Result);
    }

    public int AnonymousTypeProjection(CancellationToken ct)
    {
        int id = 5;
        string name = "nm";
        return Pipeline.ExecuteAsync(c => { var o = new { id, name }; var t = (id, name); return new ValueTask<int>(o.id + t.name.Length); }, ct).Result;
    }

    public int NonStaticLocalFunction(CancellationToken ct)
    {
        int id = 2;
        int Twice() => id * 2;
        return Pipeline.ExecuteAsync(c => new ValueTask<int>(Twice()), ct).Result;
    }

    public int StaticLocalFunction(CancellationToken ct)
    {
        int id = 2;
        static int Twice(int v) => v * 2;
        return Pipeline.ExecuteAsync(c => new ValueTask<int>(Twice(id)), ct).Result;
    }

    public int CapturedCtOfOuterMethod(CancellationToken ct)
        => Pipeline.ExecuteAsync(_ => new ValueTask<int>(ct.CanBeCanceled ? 1 : 0), ct).Result;
}

public struct StructShapes
{
    private int _field;
    public StructShapes(int f) => _field = f;

    public int LocalsInStructMethod(CancellationToken ct)
    {
        int id = _field; // copy `this` state into a local: capturing the local is legal in a struct
        return Pipeline.ExecuteAsync(c => new ValueTask<int>(id + 1), ct).Result;
    }
}
