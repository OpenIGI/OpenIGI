namespace OpenIGI.Game.Multiplayer;

/// <summary>Where a shooter's client drew the other players when it fired: the moment a shot is traced at on the server.</summary>
/// <remarks>
/// <para>
/// A client draws everyone else <see cref="InterpolationDelay"/> behind the server and fires at what it sees. Tracing its
/// shot against where the targets stand on the server when the shot arrives would miss a running man by his own width
/// several times over. So the server puts each target back where the shooter's client had it, the shooter's command tick
/// less the interpolation delay, traces, and puts them back: Source's lag compensation.
/// </para>
/// <para>
/// The history is the server's own recent snapshots, sampled by the same code a client draws with, so the two agree by
/// construction. No more than <see cref="MaxUnlag"/> is ever rewound.
/// </para>
/// </remarks>
/// <param name="history">The server's recent snapshots.</param>
/// <param name="tickRate">The server's ticks per second.</param>
public sealed class LagCompensation(SnapshotInterpolator history, int tickRate)
{
    /// <summary>Gets the server's recent snapshots.</summary>
    public SnapshotInterpolator History { get; } = history ?? throw new ArgumentNullException(nameof(history));

    /// <summary>Gets the server's ticks per second.</summary>
    public int TickRate { get; } = tickRate;

    /// <summary>Gets or sets how far behind the server clients draw the others, in seconds (<c>cl_interp</c>).</summary>
    public double InterpolationDelay { get; set; } = 0.1;

    /// <summary>Gets or sets the most a shot may be rewound, in seconds (<c>sv_maxunlag</c>).</summary>
    public double MaxUnlag { get; set; } = 1.0;

    /// <summary>Gets the moment a command was aimed at: its tick less the interpolation delay, within <see cref="MaxUnlag"/>.</summary>
    /// <param name="commandTick">The tick the shooter's command ran on.</param>
    /// <returns>The fractional tick to sample the history at.</returns>
    public double AimedAt(long commandTick) => commandTick - (Math.Clamp(InterpolationDelay, 0, MaxUnlag) * TickRate);

    /// <summary>Gets where a target stood at the moment a command was aimed at.</summary>
    /// <param name="commandTick">The tick the shooter's command ran on.</param>
    /// <param name="target">The target's entity id.</param>
    /// <param name="human">Where it stood then.</param>
    /// <returns><see langword="false"/> when the history does not reach back that far for it.</returns>
    public bool TargetAt(long commandTick, int target, out ReplicatedHuman human) =>
        History.Sample(AimedAt(commandTick), target, out human);

    /// <summary>Gets the moment a command was aimed at from the tick its client says it was drawing.</summary>
    /// <param name="commandTick">The tick the shooter's command ran on.</param>
    /// <param name="viewTick">The tick the client drew the others at, or <see cref="long.MinValue"/> when not known.</param>
    /// <returns>The tick to sample the history at.</returns>
    /// <remarks>
    /// The command is stamped ahead of the server by the client's lead, so its own tick less the interpolation misses what the
    /// shooter saw by that lead; the view tick it carries does not. A view tick from the future, or further back than
    /// <see cref="MaxUnlag"/>, is not believed: the first is clamped to the command, the second to the limit, as Source clamps
    /// its correction to <c>sv_maxunlag</c>.
    /// </remarks>
    public double AimedAt(long commandTick, long viewTick) =>
        viewTick == long.MinValue
            ? AimedAt(commandTick)
            : Math.Clamp(viewTick, commandTick - (MaxUnlag * TickRate), commandTick);

    /// <summary>Gets a player where the shooter's client drew it when it fired, from the view tick its command carried.</summary>
    /// <param name="commandTick">The tick the shooter's command ran on.</param>
    /// <param name="viewTick">The tick the client drew the others at, or <see cref="long.MinValue"/>.</param>
    /// <param name="target">The player's entity id.</param>
    /// <param name="human">It as it was then.</param>
    /// <returns><see langword="false"/> when the history does not reach back that far or the player was not there.</returns>
    public bool TargetAt(long commandTick, long viewTick, int target, out ReplicatedHuman human) =>
        History.Sample(AimedAt(commandTick, viewTick), target, out human);
}
