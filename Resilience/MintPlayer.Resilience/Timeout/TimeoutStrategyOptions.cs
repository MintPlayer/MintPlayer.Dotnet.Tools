using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Timeout;

/// <summary>Options of the timeout strategy.</summary>
public class TimeoutStrategyOptions : ResilienceStrategyOptions
{
    /// <summary>Initializes the options with Polly's default timeout of 30 s.</summary>
    public TimeoutStrategyOptions() => Name = "Timeout";

    /// <summary>Gets or sets the timeout. Default 30 s; valid 10 ms to 1 day.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a synchronous generator that decides the timeout per execution, overriding
    /// <see cref="Timeout"/>. A zero, negative or infinite value means no timeout for that execution.
    /// </summary>
    public Func<TimeoutGeneratorArguments, TimeSpan>? TimeoutGenerator { get; set; }

    /// <summary>Gets or sets the event raised when the timeout cancels an execution.</summary>
    public Func<OnTimeoutArguments, ValueTask>? OnTimeout { get; set; }

    internal override void Validate() => RequireRange(Timeout, TimeSpan.FromMilliseconds(10), TimeSpan.FromDays(1), nameof(Timeout));

    internal TimeoutStrategyOptions Snapshot() => (TimeoutStrategyOptions)MemberwiseClone();
}

/// <summary>The arguments of <see cref="TimeoutStrategyOptions.TimeoutGenerator"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct TimeoutGeneratorArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    public TimeoutGeneratorArguments(ResilienceContext context) => _source = context;

    internal TimeoutGeneratorArguments(ExecutionFrame frame) => _source = frame;

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);
}

/// <summary>The arguments of <see cref="TimeoutStrategyOptions.OnTimeout"/>.</summary>
/// <remarks>Valid only during the call it is passed to.</remarks>
public readonly struct OnTimeoutArguments
{
    private readonly object? _source;

    /// <summary>Initializes the arguments.</summary>
    /// <param name="context">The context of the execution.</param>
    /// <param name="timeout">The timeout that elapsed.</param>
    public OnTimeoutArguments(ResilienceContext context, TimeSpan timeout)
    {
        _source = context;
        Timeout = timeout;
    }

    internal OnTimeoutArguments(ExecutionFrame frame, TimeSpan timeout)
    {
        _source = frame;
        Timeout = timeout;
    }

    /// <summary>Gets the context of the execution.</summary>
    public ResilienceContext Context => ContextSource.Resolve(_source);

    /// <summary>Gets the timeout that elapsed.</summary>
    public TimeSpan Timeout { get; }
}
