using MintPlayer.Resilience.CircuitBreaker;
using MintPlayer.Resilience.Pipeline;

namespace MintPlayer.Resilience.Retry;

/// <summary>
/// A retry budget (beyond Polly, Finagle / Envoy style): caps retries at a share of recent traffic, so
/// that when a dependency fails every caller does not multiply its load by <c>MaxRetryAttempts + 1</c>.
/// One instance is meant to be shared by every retry strategy (and every pipeline) that calls the same
/// dependency: set it as <see cref="RetryStrategyOptions{TResult}.Budget"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every execution of a retry strategy that uses the budget deposits one request. A retry is allowed when
/// the retries already made in the last <see cref="TimeToLive"/> stay within
/// <c>MinRetriesPerSecond × TimeToLive + RetryRatio × requests in the last TimeToLive</c>. So with the
/// defaults (ratio 0.2, 10 per second, 10 s), over any 10 s at most 100 retries plus one retry per five
/// requests are made. The reserve lets low-traffic callers still retry.
/// </para>
/// <para>
/// When the budget is exhausted the retry strategy stops and returns the last outcome, as if it had been
/// the last attempt, after raising <see cref="RetryStrategyOptions{TResult}.OnBudgetExhausted"/>.
/// </para>
/// <para>
/// The window is kept in 10 segments of <c>TimeToLive / 10</c>, so counts expire one segment at a time.
/// It is lock-free (compare-and-swap on packed counters) and never allocates. A retry is counted first
/// and taken back when the budget turns out to be spent, so the cap is never exceeded; under contention a
/// retry can be refused while another refused retry is being taken back.
/// </para>
/// </remarks>
public sealed class RetryBudget
{
    private const int SegmentCount = 10;

    private readonly MicroClock _clock;
    private readonly long _segmentMicros;
    private readonly double _reserve;
    private readonly SegmentedCounter _requests = new(SegmentCount);
    private readonly SegmentedCounter _retries = new(SegmentCount);

    /// <summary>Initializes a budget.</summary>
    /// <param name="retryRatio">The retries allowed per request, over the window. Default 0.2; valid 0 to 1000.</param>
    /// <param name="minRetriesPerSecond">The retries per second allowed whatever the traffic. Default 10; valid 0 to 1 000 000.</param>
    /// <param name="timeToLive">How long a request or a retry counts. Default 10 s; valid 1 s to 1 hour.</param>
    /// <param name="timeProvider">The clock; default <see cref="TimeProvider.System"/>. The budget is shared across pipelines, so it has its own.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of its range.</exception>
    public RetryBudget(double retryRatio = 0.2, int minRetriesPerSecond = 10, TimeSpan? timeToLive = null, TimeProvider? timeProvider = null)
    {
        var ttl = timeToLive ?? TimeSpan.FromSeconds(10);
        if (!(retryRatio >= 0 && retryRatio <= 1000))
        {
            throw new ArgumentOutOfRangeException(nameof(retryRatio), retryRatio, "The retry ratio must be between 0 and 1000.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(minRetriesPerSecond);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minRetriesPerSecond, 1_000_000);
        if (ttl < TimeSpan.FromSeconds(1) || ttl > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), ttl, "The time to live must be between 1 second and 1 hour.");
        }

        RetryRatio = retryRatio;
        MinRetriesPerSecond = minRetriesPerSecond;
        TimeToLive = ttl;
        _clock = new MicroClock(timeProvider ?? TimeProvider.System);
        _segmentMicros = MicroClock.Micros(ttl) / SegmentCount;
        _reserve = minRetriesPerSecond * ttl.TotalSeconds;
    }

    /// <summary>Gets the retries allowed per request, over the window.</summary>
    public double RetryRatio { get; }

    /// <summary>Gets the retries per second allowed whatever the traffic.</summary>
    public int MinRetriesPerSecond { get; }

    /// <summary>Gets how long a request or a retry counts.</summary>
    public TimeSpan TimeToLive { get; }

    /// <summary>Gets how many retries the budget would allow right now (a snapshot; concurrent callers may take them first).</summary>
    public int Balance
    {
        get
        {
            var segment = Segment();
            var balance = Allowed(_requests.Sum(segment)) - _retries.Sum(segment);
            return (int)Math.Clamp(Math.Floor(balance), 0, int.MaxValue);
        }
    }

    /// <summary>Records one request. The retry strategy calls it once per execution.</summary>
    public void Deposit()
    {
        while (!_requests.TryIncrement(Segment()))
        {
        }
    }

    /// <summary>Takes one retry from the budget. The retry strategy calls it before each retry.</summary>
    /// <returns><see langword="true"/> when the retry may go ahead; <see langword="false"/> when the budget is spent (nothing is taken).</returns>
    public bool TryWithdraw()
    {
        long segment;
        do
        {
            segment = Segment();
        }
        while (!_retries.TryIncrement(segment));

        if (_retries.Sum(segment) <= Allowed(_requests.Sum(segment)))
        {
            return true;
        }

        _retries.Decrement(segment);
        return false;
    }

    private double Allowed(long requests) => _reserve + RetryRatio * requests;

    private long Segment() => _clock.Now() / _segmentMicros;
}
