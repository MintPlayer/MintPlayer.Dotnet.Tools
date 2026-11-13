using System.Diagnostics;

namespace MintPlayer.Resilience.Retry;

/// <summary>
/// Backoff arithmetic, ported line for line from Polly 8.8.0 (<c>src/Polly.Core/Retry/RetryHelper.cs</c>)
/// so a migrated pipeline waits exactly as long as before. Shared by the runtime strategy and (M4) the
/// generated pipelines.
/// </summary>
internal static class RetryHelper
{
    private const double JitterFactor = 0.5;

    private const double ExponentialFactor = 2.0;

    // Upper bound that prevents overflow beyond TimeSpan.MaxValue; the 1,000 avoids truncation in the double → long conversion.
    private static readonly double MaxTimeSpanTicks = (double)TimeSpan.MaxValue.Ticks - 1_000;

    public static bool IsValidDelay(TimeSpan delay) => delay >= TimeSpan.Zero;

    /// <summary>The delay before retry number <paramref name="attempt"/> + 1 (0-based attempt of the failed try).</summary>
    /// <param name="type">The backoff type.</param>
    /// <param name="jitter">Whether jitter is applied.</param>
    /// <param name="attempt">The 0-based attempt that just failed.</param>
    /// <param name="baseDelay">The base delay.</param>
    /// <param name="maxDelay">The cap on the computed delay.</param>
    /// <param name="state">Decorrelated-jitter state, carried from one retry to the next of the same execution (starts at 0).</param>
    /// <param name="randomizer">Returns a value in [0, 1).</param>
    public static TimeSpan GetRetryDelay(
        DelayBackoffType type,
        bool jitter,
        int attempt,
        TimeSpan baseDelay,
        TimeSpan? maxDelay,
        ref double state,
        Func<double> randomizer)
    {
        try
        {
            var delay = GetRetryDelayCore(type, jitter, attempt, baseDelay, ref state, randomizer);

            if (maxDelay is TimeSpan maxDelayValue && delay > maxDelayValue)
            {
                return maxDelayValue;
            }

            return delay;
        }
        catch (OverflowException)
        {
            return maxDelay ?? TimeSpan.MaxValue;
        }
    }

    internal static TimeSpan ApplyJitter(TimeSpan delay, Func<double> randomizer)
    {
        var offset = (delay.TotalMilliseconds * JitterFactor) / 2;
        var randomDelay = (delay.TotalMilliseconds * JitterFactor * randomizer()) - offset;
        var newDelay = delay.TotalMilliseconds + randomDelay;

        return TimeSpan.FromMilliseconds(newDelay);
    }

    /// <summary>
    /// Exponential backoff with decorrelated jitter ("DecorrelatedJitterV2", by George Polevoy, with
    /// Polly's pFactor = 4 and rpScalingFactor = 1 / 1.4 adaptations). For attempt t the delay lies
    /// between 0 and <c>baseDelay * 2^(t+1)</c> for t = 0 or 1, with medians near <c>baseDelay * 2^t</c>.
    /// </summary>
    internal static TimeSpan DecorrelatedJitterBackoffV2(int attempt, TimeSpan baseDelay, ref double prev, Func<double> randomizer)
    {
        // A factor used within the formula to help smooth the first calculated delay.
        const double PFactor = 4.0;

        // Scales the median values of the retry times to be near whole seconds (1, 2, 4 … instead of 1.4, 2.8, 5.6 …).
        const double RpScalingFactor = 1 / 1.4d;

        long targetTicksFirstDelay = baseDelay.Ticks;

        double t = attempt + randomizer();
        double next = Math.Pow(ExponentialFactor, t) * Math.Tanh(Math.Sqrt(PFactor * t));

        // At t >= 1024 the above tends to infinity, which would otherwise make the ticks negative (Polly #2163).
        if (double.IsInfinity(next))
        {
            prev = next;
            return TimeSpan.FromTicks((long)MaxTimeSpanTicks);
        }

        double formulaIntrinsicValue = next - prev;
        prev = next;

        long ticks = (long)Math.Min(formulaIntrinsicValue * RpScalingFactor * targetTicksFirstDelay, MaxTimeSpanTicks);

        Debug.Assert(ticks >= 0, "ticks cannot be negative");

        return TimeSpan.FromTicks(ticks);
    }

    private static TimeSpan GetRetryDelayCore(DelayBackoffType type, bool jitter, int attempt, TimeSpan baseDelay, ref double state, Func<double> randomizer)
    {
        if (baseDelay == TimeSpan.Zero)
        {
            return baseDelay;
        }

        if (jitter)
        {
            return type switch
            {
                DelayBackoffType.Constant => ApplyJitter(baseDelay, randomizer),
                DelayBackoffType.Linear => ApplyJitter(TimeSpan.FromMilliseconds((attempt + 1) * baseDelay.TotalMilliseconds), randomizer),
                DelayBackoffType.Exponential => DecorrelatedJitterBackoffV2(attempt, baseDelay, ref state, randomizer),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The retry backoff type is not supported."),
            };
        }

        return type switch
        {
            DelayBackoffType.Constant => baseDelay,
            DelayBackoffType.Linear => (attempt + 1) * baseDelay,
            DelayBackoffType.Exponential => Math.Pow(ExponentialFactor, attempt) * baseDelay,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The retry backoff type is not supported."),
        };
    }
}
