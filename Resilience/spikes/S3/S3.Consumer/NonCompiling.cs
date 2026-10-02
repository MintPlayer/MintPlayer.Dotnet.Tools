// Shapes the C# compiler itself rejects inside a lambda. Built only with -p:S3NonCompiling=true, to
// record the error codes. None of them can reach the generator as a valid call site.
namespace Spike.S3;

public struct ThisInStruct
{
    private int _field;
    public int CaptureThis(CancellationToken ct)
        => Pipeline.ExecuteAsync(c => new ValueTask<int>(_field), ct).Result; // `this` of a struct
}

public static class ByRefCaptures
{
    public static int RefLocal(CancellationToken ct)
    {
        int x = 1;
        ref int r = ref x;
        return Pipeline.ExecuteAsync(c => new ValueTask<int>(r), ct).Result; // ref local
    }

    public static int SpanLocal(CancellationToken ct)
    {
        Span<int> span = stackalloc int[1];
        return Pipeline.ExecuteAsync(c => new ValueTask<int>(span[0]), ct).Result; // ref struct local
    }

    public static int RefParameter(ref int p, CancellationToken ct)
        => Pipeline.ExecuteAsync(c => new ValueTask<int>(p), ct).Result; // ref parameter

    public static int InParameter(in int p, CancellationToken ct)
        => Pipeline.ExecuteAsync(c => new ValueTask<int>(p), ct).Result; // in parameter

    public static int SpanParameter(ReadOnlySpan<char> s, CancellationToken ct)
        => Pipeline.ExecuteAsync(c => new ValueTask<int>(s.Length), ct).Result; // ref struct parameter
}
