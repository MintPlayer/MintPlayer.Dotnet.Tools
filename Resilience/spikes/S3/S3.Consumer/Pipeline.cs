using System.Runtime.CompilerServices;

namespace Spike.S3;

/// <summary>Measurement control: the generator never intercepts call sites inside a method marked with this.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NoInterceptAttribute : Attribute { }

/// <summary>
/// Stand-in for a generated pipeline. NoInlining models the real generated flat method, which is far too
/// large to inline; it also stops the JIT from stack-allocating a delegate that would otherwise not escape.
/// </summary>
public static partial class Pipeline
{
    public static int PlainCalls, StateCalls, Intercepted;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> callback, CancellationToken cancellationToken)
    {
        PlainCalls++;
        return callback(cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ValueTask<T> ExecuteAsync<T, TState>(Func<TState, CancellationToken, ValueTask<T>> callback, TState state, CancellationToken cancellationToken)
    {
        StateCalls++;
        return callback(state, cancellationToken);
    }
}

/// <summary>Same API, inlinable, never intercepted: to see whether JIT escape analysis removes the closure by itself.</summary>
public static class InlinablePipeline
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> callback, CancellationToken cancellationToken)
        => callback(cancellationToken);
}
