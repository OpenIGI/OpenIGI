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
    private double _leadAdjustTicks;

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

    /// <summary>Gets or sets how far behind the server remote things are drawn, in seconds (<c>cl_interp</c>).</summary>
    public double InterpolationDelay { get; set; } = 0.1;

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
            return;
        }

        _offsetTicks += (sample - _offsetTicks) * Smoothing;
    }

    /// <summary>Gets the tick the server is on now, fractional.</summary>
    /// <param name="now">The local time, in seconds.</param>
    /// <returns>The estimated tick.</returns>
    public double ServerTick(double now) => (now * TickRate) + _offsetTicks;

    /// <summary>Gets the tick remote things are drawn at now, fractional.</summary>
    /// <param name="now">The local time, in seconds.</param>
    /// <returns>The tick to interpolate the snapshots to.</returns>
    public double InterpolationTick(double now) => ServerTick(now) - (InterpolationDelay * TickRate);

    /// <summary>Gets the tick a command sampled now should be stamped for.</summary>
    /// <param name="now">The local time, in seconds.</param>
    /// <param name="roundTripTime">The connection's round trip, in seconds.</param>
    /// <returns>The tick the server should run it on.</returns>
    public long CommandTick(double now, double roundTripTime) =>
        (long)Math.Ceiling(ServerTick(now) + (roundTripTime * 0.5 * TickRate) + CommandBufferTicks + _leadAdjustTicks);

    /// <summary>Takes how far ahead of its tick the server says this client's newest command arrived.</summary>
    /// <param name="leadTicks">The lead, in ticks.</param>
    /// <remarks>
    /// The round trip the stamps are reckoned from carries both ends' frame time, so it overstates the wire; steered by what
    /// the server sees, the commands settle <see cref="CommandBufferTicks"/> ahead whatever the estimate's error, as Source
    /// has the server tell a client its commands' slack.
    /// </remarks>
    public void ReportCommandLead(double leadTicks)
    {
        _leadAdjustTicks += (CommandBufferTicks - leadTicks) * LeadSteering;
        _leadAdjustTicks = Math.Clamp(_leadAdjustTicks, -TickRate, TickRate);
    }

    /// <summary>Gets or sets how much of the error in a reported lead each report corrects, 0 to 1.</summary>
    public double LeadSteering { get; set; } = 0.1;
}
