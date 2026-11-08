namespace OpenIGI.Net;

/// <summary>
/// A rate for each sender, by host rather than port, so one machine cycling its ports is still one sender: what a server
/// answers anyone who is not yet a client by, so a flood of requests costs it no more than a trickle does.
/// </summary>
/// <remarks>
/// It keeps at most <see cref="MaxTracked"/> senders. Those quiet for a while are forgotten to make room; when every place is
/// held by a sender heard from just now -- a flood from forged addresses -- a new one is refused rather than let in, which
/// costs the clients already connected nothing: their packets are never asked about here.
/// </remarks>
public sealed class FloodGuard
{
    private const double ForgetSeconds = 10;

    private readonly Dictionary<(NetAddressKind, UInt128, int), (TokenBucket Bucket, double Seen)> _senders = [];

    /// <summary>Initializes a new instance of the <see cref="FloodGuard"/> class.</summary>
    /// <param name="perSecond">What a sender may spend a second.</param>
    /// <param name="burst">What it may spend at once.</param>
    /// <param name="maxTracked">The most senders kept.</param>
    public FloodGuard(double perSecond, double burst, int maxTracked = 8192)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(burst);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTracked);
        PerSecond = perSecond;
        Burst = burst;
        MaxTracked = maxTracked;
    }

    /// <summary>Gets what a sender may spend a second.</summary>
    public double PerSecond { get; }

    /// <summary>Gets what a sender may spend at once.</summary>
    public double Burst { get; }

    /// <summary>Gets the most senders kept.</summary>
    public int MaxTracked { get; }

    /// <summary>Gets how many times a sender was refused.</summary>
    public long Refused { get; private set; }

    /// <summary>Spends from a sender's rate.</summary>
    /// <param name="sender">Who sent it.</param>
    /// <param name="now">The clock, in seconds.</param>
    /// <param name="cost">What it costs.</param>
    /// <returns><see langword="true"/> when the sender could afford it.</returns>
    public bool TrySpend(NetAddress sender, double now, double cost = 1)
    {
        var key = KeyOf(sender);
        if (!_senders.TryGetValue(key, out (TokenBucket Bucket, double Seen) entry))
        {
            if (_senders.Count >= MaxTracked && !MakeRoom(now))
            {
                Refused++;
                return false;
            }

            entry = (new TokenBucket(PerSecond, Burst), now);
        }

        bool spent = entry.Bucket.TrySpend(now, cost);
        _senders[key] = (entry.Bucket, now);
        if (!spent)
        {
            Refused++;
        }

        return spent;
    }

    /// <summary>Takes from a sender's rate whatever it holds, up to a cost: a penalty, such as for a refused join.</summary>
    /// <param name="sender">Who.</param>
    /// <param name="now">The clock, in seconds.</param>
    /// <param name="cost">How much.</param>
    public void Charge(NetAddress sender, double now, double cost)
    {
        var key = KeyOf(sender);
        if (_senders.TryGetValue(key, out (TokenBucket Bucket, double Seen) entry))
        {
            while (cost >= 1 && entry.Bucket.TrySpend(now))
            {
                cost--;
            }
        }
    }

    // A machine on the internet is its host; a room's peer or a loopback line is the whole of its address.
    private static (NetAddressKind, UInt128, int) KeyOf(NetAddress sender) =>
        sender.Kind == NetAddressKind.Ip ? (sender.Kind, sender.Host, 0) : (sender.Kind, sender.Host, sender.Port);

    // Room made by forgetting the senders quiet for a while; false when none are.
    private bool MakeRoom(double now)
    {
        int before = _senders.Count;
        foreach (var key in _senders.Where(s => now - s.Value.Seen > ForgetSeconds).Select(static s => s.Key).ToArray())
        {
            _senders.Remove(key);
        }

        return _senders.Count < before;
    }
}
