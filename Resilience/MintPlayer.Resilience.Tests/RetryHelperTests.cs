using MintPlayer.Resilience.Retry;

namespace MintPlayer.Resilience.Tests;

/// <summary>Pins the backoff arithmetic to Polly 8.8.0's formulas.</summary>
public class RetryHelperTests
{
    private static TimeSpan Delay(DelayBackoffType type, bool jitter, int attempt, TimeSpan baseDelay, TimeSpan? maxDelay = null, Func<double>? randomizer = null)
    {
        double state = 0;
        return RetryHelper.GetRetryDelay(type, jitter, attempt, baseDelay, maxDelay, ref state, randomizer ?? (() => 0.5));
    }

    [Theory]
    [InlineData(DelayBackoffType.Constant, 0, 100)]
    [InlineData(DelayBackoffType.Constant, 5, 100)]
    [InlineData(DelayBackoffType.Linear, 0, 100)]
    [InlineData(DelayBackoffType.Linear, 2, 300)]
    [InlineData(DelayBackoffType.Exponential, 0, 100)]
    [InlineData(DelayBackoffType.Exponential, 3, 800)]
    public void WithoutJitter_MatchesPolly(DelayBackoffType type, int attempt, int expectedMs)
        => Delay(type, false, attempt, TimeSpan.FromMilliseconds(100)).Should().Be(TimeSpan.FromMilliseconds(expectedMs));

    [Theory]
    [InlineData(DelayBackoffType.Constant)]
    [InlineData(DelayBackoffType.Linear)]
    [InlineData(DelayBackoffType.Exponential)]
    public void ZeroBaseDelay_IsAlwaysZero(DelayBackoffType type)
    {
        Delay(type, false, 3, TimeSpan.Zero).Should().Be(TimeSpan.Zero);
        Delay(type, true, 3, TimeSpan.Zero).Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0.0, 750)]
    [InlineData(0.5, 1000)]
    [InlineData(1.0, 1250)]
    public void ConstantJitter_IsPlusMinus25Percent(double random, int expectedMs)
        => Delay(DelayBackoffType.Constant, true, 0, TimeSpan.FromSeconds(1), randomizer: () => random).Should().Be(TimeSpan.FromMilliseconds(expectedMs));

    [Theory]
    [InlineData(0.0, 1500)]
    [InlineData(1.0, 2500)]
    public void LinearJitter_AppliesToTheLinearDelay(double random, int expectedMs)
        => Delay(DelayBackoffType.Linear, true, 1, TimeSpan.FromSeconds(1), randomizer: () => random).Should().Be(TimeSpan.FromMilliseconds(expectedMs));

    [Fact]
    public void RandomJitter_StaysWithinBounds()
    {
        var baseDelay = TimeSpan.FromSeconds(1);
        for (var i = 0; i < 10_000; i++)
        {
            var constant = Delay(DelayBackoffType.Constant, true, 0, baseDelay, randomizer: Random.Shared.NextDouble);
            constant.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(750));
            constant.Should().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(1250));
        }
    }

    [Fact]
    public void DecorrelatedJitter_StaysWithinPollysDocumentedBounds_AndIsNeverNegative()
    {
        // Polly: for try t the delay lies between 0 and f * 2^(t+1) for t = 0 or 1, and f * (2^(t+1) - 2^(t-1)) for t >= 2.
        var baseDelay = TimeSpan.FromSeconds(1);
        for (var run = 0; run < 2_000; run++)
        {
            double state = 0;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var delay = RetryHelper.GetRetryDelay(DelayBackoffType.Exponential, true, attempt, baseDelay, null, ref state, Random.Shared.NextDouble);
                var upper = attempt < 2
                    ? baseDelay * Math.Pow(2, attempt + 1)
                    : baseDelay * (Math.Pow(2, attempt + 1) - Math.Pow(2, attempt - 1));

                delay.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
                delay.Should().BeLessThanOrEqualTo(upper);
            }
        }
    }

    [Fact]
    public void DecorrelatedJitter_MatchesThePollyFormula()
    {
        double state = 0;
        var randoms = new SequenceRandomizer(0.25, 0.5, 0.75);
        var baseDelay = TimeSpan.FromMilliseconds(200);

        double prev = 0;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var random = new[] { 0.25, 0.5, 0.75 }[attempt];
            var t = attempt + random;
            var next = Math.Pow(2.0, t) * Math.Tanh(Math.Sqrt(4.0 * t));
            var expected = TimeSpan.FromTicks((long)((next - prev) * (1 / 1.4d) * baseDelay.Ticks));
            prev = next;

            RetryHelper.GetRetryDelay(DelayBackoffType.Exponential, true, attempt, baseDelay, null, ref state, randoms.Next).Should().Be(expected);
        }
    }

    [Fact]
    public void MaxDelay_CapsJitteredAndPlainDelays()
    {
        Delay(DelayBackoffType.Exponential, false, 10, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)).Should().Be(TimeSpan.FromSeconds(5));
        Delay(DelayBackoffType.Constant, true, 0, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), () => 1.0).Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Overflow_ReturnsMaxDelay_OrTimeSpanMaxValue()
    {
        Delay(DelayBackoffType.Exponential, false, 1000, TimeSpan.FromDays(1), TimeSpan.FromHours(1)).Should().Be(TimeSpan.FromHours(1));
        Delay(DelayBackoffType.Exponential, false, 1000, TimeSpan.FromDays(1)).Should().Be(TimeSpan.MaxValue);
    }

    [Fact]
    public void DecorrelatedJitter_AtHugeAttempts_DoesNotGoNegative()
    {
        double state = 0;
        var delay = RetryHelper.GetRetryDelay(DelayBackoffType.Exponential, true, 2000, TimeSpan.FromSeconds(1), null, ref state, () => 0.5);
        delay.Should().BeGreaterThan(TimeSpan.Zero);
    }
}
