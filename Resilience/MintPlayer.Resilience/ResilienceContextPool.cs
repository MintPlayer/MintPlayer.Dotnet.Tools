using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience;

/// <summary>A pool of <see cref="ResilienceContext"/> instances.</summary>
public abstract class ResilienceContextPool
{
    /// <summary>Gets the process-wide shared pool.</summary>
    public static ResilienceContextPool Shared { get; } = new SharedPool();

    /// <summary>Rents a context.</summary>
    /// <param name="cancellationToken">The cancellation token of the execution.</param>
    /// <returns>A context; give it back with <see cref="Return"/>.</returns>
    public ResilienceContext Get(CancellationToken cancellationToken = default) => Get(null, cancellationToken);

    /// <summary>Rents a context with an operation key.</summary>
    /// <param name="operationKey">The operation key.</param>
    /// <param name="cancellationToken">The cancellation token of the execution.</param>
    /// <returns>A context; give it back with <see cref="Return"/>.</returns>
    public ResilienceContext Get(string? operationKey, CancellationToken cancellationToken = default) => Get(operationKey, null, cancellationToken);

    /// <summary>Rents a context with an operation key and a synchronization-context preference.</summary>
    /// <param name="operationKey">The operation key.</param>
    /// <param name="continueOnCapturedContext">Whether to continue on the captured context; <see langword="null"/> means <see langword="false"/>.</param>
    /// <param name="cancellationToken">The cancellation token of the execution.</param>
    /// <returns>A context; give it back with <see cref="Return"/>.</returns>
    public ResilienceContext Get(string? operationKey, bool? continueOnCapturedContext, CancellationToken cancellationToken = default)
        => Get(new ResilienceContextCreationArguments(operationKey, continueOnCapturedContext, cancellationToken));

    /// <summary>Rents a context with a synchronization-context preference.</summary>
    /// <param name="continueOnCapturedContext">Whether to continue on the captured context.</param>
    /// <param name="cancellationToken">The cancellation token of the execution.</param>
    /// <returns>A context; give it back with <see cref="Return"/>.</returns>
    public ResilienceContext Get(bool continueOnCapturedContext, CancellationToken cancellationToken = default)
        => Get(new ResilienceContextCreationArguments(null, continueOnCapturedContext, cancellationToken));

    /// <summary>Rents a context.</summary>
    /// <param name="arguments">The values to initialize the context with.</param>
    /// <returns>A context; give it back with <see cref="Return"/>.</returns>
    public abstract ResilienceContext Get(ResilienceContextCreationArguments arguments);

    /// <summary>Returns a context to the pool. It must not be used afterwards.</summary>
    /// <param name="context">The context to return.</param>
    public abstract void Return(ResilienceContext context);

    private sealed class SharedPool : ResilienceContextPool
    {
        private readonly ObjectPool<ResilienceContext> _pool = new(static () => new ResilienceContext());

        public override ResilienceContext Get(ResilienceContextCreationArguments arguments)
        {
            var context = _pool.Get();
            context.OperationKey = arguments.OperationKey;
            context.CancellationToken = arguments.CancellationToken;
            context.ContinueOnCapturedContext = arguments.ContinueOnCapturedContext ?? false;
            return context;
        }

        public override void Return(ResilienceContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Reset();
            _pool.Return(context);
        }
    }
}

/// <summary>The values a <see cref="ResilienceContext"/> is created with.</summary>
public readonly struct ResilienceContextCreationArguments
{
    /// <summary>Initializes the arguments.</summary>
    /// <param name="operationKey">The operation key.</param>
    /// <param name="continueOnCapturedContext">Whether to continue on the captured context; <see langword="null"/> means <see langword="false"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ResilienceContextCreationArguments(string? operationKey, bool? continueOnCapturedContext, CancellationToken cancellationToken)
    {
        OperationKey = operationKey;
        ContinueOnCapturedContext = continueOnCapturedContext;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the operation key.</summary>
    public string? OperationKey { get; }

    /// <summary>Gets whether to continue on the captured context.</summary>
    public bool? ContinueOnCapturedContext { get; }

    /// <summary>Gets the cancellation token.</summary>
    public CancellationToken CancellationToken { get; }
}
