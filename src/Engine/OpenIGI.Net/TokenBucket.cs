namespace OpenIGI.Net;

/// <summary>A budget refilled at a steady rate up to a burst: so much a second, and no more saved up than the burst.</summary>
/// <remarks>What a server lets each client spend on things it relays to everyone -- chat, marks, voice -- so one can't flood the rest.</remarks>
public sealed class TokenBucket
{
    private double _tokens;
    private double _at = double.NaN;

    /// <summary>Initializes a new instance of the <see cref="TokenBucket"/> class, full.</summary>
    /// <param name="perSecond">What it refills a second.</param>
    /// <param name="burst">The most it holds.</param>
    public TokenBucket(double perSecond, double burst)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(burst);
        PerSecond = perSecond;
        Burst = burst;
        _tokens = burst;
    }

    /// <summary>Gets what it refills a second.</summary>
    public double PerSecond { get; }

    /// <summary>Gets the most it holds.</summary>
    public double Burst { get; }

    /// <summary>Gets how many spends it turned down.</summary>
    public long Refused { get; private set; }

    /// <summary>Spends from the budget if there is enough.</summary>
    /// <param name="now">The current time in seconds.</param>
    /// <param name="cost">What it costs; one by default.</param>
    /// <returns><see langword="true"/> when it was spent; <see langword="false"/> when the budget is short and nothing was.</returns>
    public bool TrySpend(double now, double cost = 1)
    {
        if (!double.IsNaN(_at))
        {
            _tokens = Math.Min(Burst, _tokens + (Math.Max(0, now - _at) * PerSecond));
        }

        _at = now;
        if (_tokens < cost)
        {
            Refused++;
            return false;
        }

        _tokens -= cost;
        return true;
    }
}
