namespace OpenIGI.Net;

/// <summary>A client's estimate of the server's tick, from the ticks its snapshots carry.</summary>
/// <remarks>
/// <para>
/// Every snapshot names the server tick it was taken on. Arriving at local time <c>t</c>, it says the server was at that
/// tick half a round trip earlier. The estimate keeps the offset between the two clocks and steers it towards each new
/// sample a little at a time, so jitter moves it smoothly; a sample further off than <see cref="SnapThreshold"/> (a hitch,
/// a level change) is taken at once.
/// </para>
/// <para>
/// Two times are read off it. Remote things are drawn at <see cref="InterpolationTick"/>, behind the newest snapshot by
/// the interpolation delay, so there is nearly always a snapshot either side to blend between. The client's own commands
/// are stamped <see cref="CommandTick"/>, ahead of the server by half a round trip and a small buffer, so each arrives
/// just before the server runs that tick. This is Source's split between <c>cl_interp</c> and the predicted tickbase.
/// </para>
/// </remarks>
public sealed class TickClockSync
{
    private bool _hasSample;
    private double _offsetTicks;
    private long _lastSampleTick = long.MinValue;
    private double _spacingTicks = 1;
    private double _jitterTicks;
    private double _appliedDelay = -1;

    /// <summary>Initializes a new instance of the <see cref="TickClockSync"/> class.</summary>
    /// <param name="tickRate">The server's ticks per second.</param>
    public TickClockSync(int tickRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        TickRate = tickRate;
    }

    /// <summary>Gets the server's ticks per second.</summary>
    public int TickRate { get; }

    /// <summary>Gets or sets how far the estimate moves towards each sample, 0 to 1.</summary>
    public double Smoothing { get; set; } = 0.05;

    /// <summary>Gets or sets how many ticks off a sample must be to be taken at once.</summary>
    public double SnapThreshold { get; set; } = 8;

    /// <summary>Gets or sets how far behind the server remote things are drawn at least, in seconds (<c>cl_interp</c>).</summary>
    /// <remarks>The delay applied (<see cref="AppliedInterpolationDelay"/>) is never less, and grows with what the line does.</remarks>
    public double InterpolationDelay { get; set; } = 0.1;

    /// <summary>Gets or sets how many snapshot spacings the delay covers at least (<c>cl_interp_ratio</c>), so one lost is bridged.</summary>
    public double InterpolationRatio { get; set; } = 2;

    /// <summary>Gets or sets the longest the delay grows to, in seconds (Source's 0.5 s bound on <c>cl_interp</c>).</summary>
    public double MaxInterpolationDelay { get; set; } = 0.5;

    /// <summary>Gets the ticks between the snapshots arriving, on average.</summary>
    public double SnapshotSpacingTicks => _spacingTicks;

    /// <summary>Gets how far each snapshot's arrival strays from the clock's estimate, on average, in ticks.</summary>
    public double ArrivalJitterTicks => _jitterTicks;

    /// <summary>Gets how far behind the server remote things are drawn now, in seconds.</summary>
    /// <remarks>
    /// The larger of <see cref="InterpolationDelay"/> and <see cref="InterpolationRatio"/> snapshot spacings, and twice the
    /// arrival jitter beyond half a tick on top -- so a slow or jittery line still has a snapshot either side to blend
    /// between instead of extrapolating -- within <see cref="MaxInterpolationDelay"/>. It eases towards that, faster growing
    /// than shrinking (running dry is worse than drawing a little later), so the others never jump.
    /// </remarks>
    public double AppliedInterpolationDelay => _appliedDelay < 0 ? InterpolationDelay : _appliedDelay;

    /// <summary>Gets or sets the ticks of slack commands arrive with ahead of their tick.</summary>
    public double CommandBufferTicks { get; set; } = 2;

    /// <summary>Gets a value indicating whether a snapshot has been seen.</summary>
    public bool HasSample => _hasSample;

    /// <summary>Takes the tick of a snapshot that just arrived.</summary>
    /// <param name="serverTick">The tick it was taken on.</param>
    /// <param name="now">The local time it arrived, in seconds.</param>
    /// <param name="roundTripTime">The connection's round trip, in seconds.</param>
    public void AddSample(long serverTick, double now, double roundTripTime)
    {
        double sample = serverTick + (roundTripTime * 0.5 * TickRate) - (now * TickRate);
        if (!_hasSample || Math.Abs(sample - _offsetTicks) > SnapThreshold)
        {
            _offsetTicks = sample;
            _hasSample = true;
            _lastSampleTick = serverTick;
            Adapt();
            return;
        }

        _jitterTicks += (Math.Abs(sample - _offsetTicks) - _jitterTicks) * Smoothing;
        _offsetTicks += (sample - _offsetTicks) * Smoothing;
        if (serverTick > _lastSampleTick)
        {
            _spacingTicks += (Math.Min(serverTick - _lastSampleTick, SnapThreshold) - _spacingTicks) * Smoothing;
            _lastSampleTick = serverTick;
        }

        Adapt();
    }

    // Eases the delay applied towards what the line now needs.
    private void Adapt()
    {
        double floor = Math.Max(InterpolationDelay, InterpolationRatio * _spacingTicks / TickRate);
        double target = Math.Clamp(floor + (2 * Math.Max(0, _jitterTicks - 0.5) / TickRate), InterpolationDelay, Math.Max(InterpolationDelay, MaxInterpolationDelay));
        if (_appliedDelay < 0)
        {
            _appliedDelay = target;
            return;
        }

        _appliedDelay += (target - _appliedDelay) * (target > _appliedDelay ? 0.1 : 0.02);
    }

    /// <summary>Gets the tick the server is on now, fractional.</summary>
    /// <param name="now">The local time, in seconds.</param>
    /// <returns>The estimated tick.</returns>
    public double ServerTick(double now) => (now * TickRate) + _offsetTicks;

    /// <summary>Gets the tick remote things are drawn at now, fractional.</summary>
    /// <param name="now">The local time, in seconds.</param>
    /// <returns>The tick to interpolate the snapshots to.</returns>
    public double InterpolationTick(double now) => ServerTick(now) - (AppliedInterpolationDelay * TickRate);

    /// <summary>Gets the tick a command sampled now should be stamped for.</summary>
    /// <param name="now">The local time, in seconds.</param>
    /// <param name="roundTripTime">The connection's round trip, in seconds.</param>
    /// <returns>The tick the server should run it on.</returns>
    public long CommandTick(double now, double roundTripTime) =>
        (long)Math.Ceiling(ServerTick(now) + (roundTripTime * 0.5 * TickRate) + CommandBufferTicks);

}
