using System.Runtime.CompilerServices;

namespace MintPlayer.Assertions.Tests.Performance;

/// <summary>
/// Measures bytes allocated per operation, deterministically.
/// </summary>
/// <remarks>
/// <para>
/// Allocation rather than wall-clock, deliberately. Bytes are a property of the emitted IL and the
/// object graph, not of the machine: the equivalency benchmark's 20.34 KB reproduced to the byte
/// across runs, where its wall-clock swung 40% between two consecutive runs on a loaded machine. A
/// byte gate therefore works on a shared CI runner; a time gate does not.
/// </para>
/// <para>
/// The mechanics below are not ceremony — each one removes a specific way the measurement lies:
/// </para>
/// <list type="bullet">
/// <item><b>Warm-up.</b> Tiered compilation promotes a method after ~30 calls and the promoted code
/// allocates differently. Measuring before promotion measures the wrong code.</item>
/// <item><b>A full collect before the window.</b> Otherwise a collection triggered by earlier work
/// lands inside the measurement.</item>
/// <item><b>A volatile sink.</b> Without it the JIT may elide the very allocation under test.</item>
/// <item><b>Two samples, take the minimum.</b> A background GC or a finalizer landing in one window
/// inflates it; it can never deflate one, so the minimum is the honest reading.</item>
/// <item><b>NoInlining.</b> Keeps the harness frame out of the measured code.</item>
/// </list>
/// </remarks>
internal static class AllocationProbe
{
    private const int WarmUp = 2_000;
    private const int Iterations = 20_000;

    /// <summary>Keeps the JIT from eliding an allocation whose result is otherwise unused.</summary>
    private static volatile object? sink;

    /// <summary>Bytes allocated per call of <paramref name="action"/>.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long BytesPerOp(Action action)
        => Math.Min(SampleOnce(action), SampleOnce(action));

    /// <summary>Bytes allocated per call of <paramref name="func"/>, whose result is sunk.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long BytesPerOp(Func<object?> func)
        => Math.Min(SampleOnce(() => sink = func()), SampleOnce(() => sink = func()));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long SampleOnce(Action action)
    {
        for (var i = 0; i < WarmUp; i++) action();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++) action();
        var after = GC.GetAllocatedBytesForCurrentThread();

        return (after - before) / Iterations;
    }
}
