namespace MintPlayer.Resilience;

/// <summary>How the delay between retries grows.</summary>
public enum DelayBackoffType
{
    /// <summary>The same delay before every retry.</summary>
    Constant,

    /// <summary>The delay grows linearly: <c>Delay * (attempt + 1)</c>.</summary>
    Linear,

    /// <summary>The delay doubles: <c>Delay * 2^attempt</c>; with jitter, Polly's decorrelated jitter.</summary>
    Exponential,
}
