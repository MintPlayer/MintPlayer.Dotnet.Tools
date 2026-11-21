using System.Threading.RateLimiting;
using Polly;
using Polly.CircuitBreaker;

namespace S5;

public enum Scenario { CircuitOpen, ConcurrencyLimited, FixedWindowLimited }

/// <summary>Builds each rejection scenario for us and for Polly with the same BCL limiter configuration.</summary>
public static class Scenarios
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static RateLimiter ExhaustedConcurrency()
    {
        var l = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 });
        _ = l.AttemptAcquire(1); // held forever: zero permits left
        return l;
    }

    public static RateLimiter ExhaustedFixedWindow()
    {
        var l = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1, QueueLimit = 0, Window = TimeSpan.FromHours(1), AutoReplenishment = false,
        });
        _ = l.AttemptAcquire(1);
        return l;
    }

    private static Exception Thrown(string msg)
    {
        try { throw new HttpRequestException(msg); } catch (Exception e) { return e; }
    }

    public static OurPipeline Ours(Scenario s)
    {
        switch (s)
        {
            case Scenario.CircuitOpen:
                var b = new Breaker(TimeProvider.System);
                b.Trip(TimeSpan.FromHours(1), Thrown("downstream failed"));
                return new OurPipeline(b, null, Timeout);
            case Scenario.ConcurrencyLimited:
                return new OurPipeline(null, ExhaustedConcurrency(), Timeout);
            default:
                return new OurPipeline(null, ExhaustedFixedWindow(), Timeout);
        }
    }

    public static ResiliencePipeline<int> Polly(Scenario s)
    {
        var builder = new ResiliencePipelineBuilder<int>();
        switch (s)
        {
            case Scenario.CircuitOpen:
                builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<int>
                {
                    ShouldHandle = static a => new(a.Outcome.Exception is not null),
                    FailureRatio = 0.5,
                    MinimumThroughput = 2,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromHours(1),
                });
                break;
            case Scenario.ConcurrencyLimited:
                builder.AddRateLimiter(ExhaustedConcurrency());
                break;
            default:
                builder.AddRateLimiter(ExhaustedFixedWindow());
                break;
        }
        builder.AddTimeout(Timeout);
        var p = builder.Build();

        if (s == Scenario.CircuitOpen)
        {
            for (var i = 0; i < 3; i++)
            {
                try { p.Execute<int>(static _ => throw new HttpRequestException("downstream failed")); } catch { }
            }
            try { p.Execute(static _ => 1); throw new InvalidOperationException("Polly circuit did not open"); }
            catch (BrokenCircuitException) { }
        }
        return p;
    }
}
