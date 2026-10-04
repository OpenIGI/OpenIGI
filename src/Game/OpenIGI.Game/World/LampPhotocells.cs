using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// The photocells that put a level's lamps on at dusk and off at dawn.
/// </summary>
/// <remarks>
/// <para>
/// A lamp that follows the daylight is not switched by the clock but by how light it is, through a sensor
/// with a threshold, a delay and a dead band -- and it is the spread in those three that makes a real
/// dusk look the way it does: a few lamps blink on while the sky still glows, then most of a yard in a
/// ragged run over a minute or so, then a straggler well after dark. Every lamp switching at one sun
/// height reads as a level being toggled.
/// </para>
/// <para>
/// So the lamps are wired into <em>circuits</em>, as a real site is. Lamps that stand near one another
/// share one: one photocell and one contactor, so they get their supply at the same moment and then each
/// takes its own time to strike. Some fittings carry a cell of their own, and switch alone. Each circuit
/// has its own threshold -- a dirty cell, one shaded by a wall, one facing the last of the western sky --
/// a dead band above it so a cloud passing at dusk does not flick the yard off and on, and a reaction
/// delay re-rolled every time it trips. On top of that every lamp adds a little jitter of its own.
/// </para>
/// <para>
/// The light it is given is the sine of the sun's elevation, so the thresholds are sun heights: a
/// threshold of nought trips as the sun touches the horizon. That keeps the spread honest at any speed
/// the clock runs: the same ragged dusk takes a minute of a forty-minute day, and a fraction of a second
/// of the scrub keys, without either one needing to be tuned for.
/// </para>
/// <para>
/// It only ever touches <see cref="LampSwitchboard.SetSupply"/>, the daylight's side of each circuit. The
/// wall switches and the faults stay the player's.
/// </para>
/// </remarks>
public sealed class LampPhotocells
{
    /// <summary>How far apart lamps can stand and still share a circuit, in metres.</summary>
    /// <remarks>
    /// About the size of a yard or a building's frontage: a row of lamps along one wall comes on together,
    /// the yard across the road on another circuit.
    /// </remarks>
    public const double CircuitMetres = 36.0;

    /// <summary>The share of lamps that carry their own photocell rather than sharing their circuit's.</summary>
    private const float OwnCellShare = 0.3f;

    private readonly LampSwitchboard _board;
    private readonly Cell[] _cells;
    private readonly Circuit[] _circuits;
    private bool _settled;

    /// <summary>Initializes a new instance of the <see cref="LampPhotocells"/> class.</summary>
    /// <param name="board">The lamps to switch.</param>
    /// <param name="fixtures">Where they stand, in the board's order.</param>
    /// <param name="seed">Mixed into every circuit's and lamp's numbers; a level's own, so levels differ.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The fixtures are not the board's.</exception>
    public LampPhotocells(LampSwitchboard board, IReadOnlyList<LightFixture> fixtures, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(fixtures);

        if (fixtures.Count != board.Count)
        {
            throw new ArgumentException("The fixtures must be the board's, one for each lamp.", nameof(fixtures));
        }

        _board = board;
        _cells = new Cell[fixtures.Count];

        var circuits = new List<Circuit>();
        var byPlace = new Dictionary<(long X, long Y, long Z), int>();
        uint rng = ((uint)seed * 0x2C1B3C6Du) ^ 0x297A2D39u;
        rng |= 1u;

        for (int i = 0; i < fixtures.Count; i++)
        {
            uint own = (((uint)seed * 0x9E3779B1u) + ((uint)(i + 1) * 0xC2B2AE35u)) | 1u;
            Vector3d at = fixtures[i].Position;

            int circuit;
            if (NextFloat(ref own) < OwnCellShare)
            {
                circuit = circuits.Count;
                circuits.Add(NewCircuit(ref rng));
            }
            else
            {
                // Vertically a little tighter than across: two floors of one building are often separate
                // circuits, two ends of one yard rarely are.
                (long, long, long) place = (
                    (long)Math.Floor(at.X / CircuitMetres),
                    (long)Math.Floor(at.Y / CircuitMetres),
                    (long)Math.Floor(at.Z / (CircuitMetres / 3.0)));

                if (!byPlace.TryGetValue(place, out circuit))
                {
                    circuit = circuits.Count;
                    circuits.Add(NewCircuit(ref rng));
                    byPlace[place] = circuit;
                }
            }

            _cells[i] = new Cell
            {
                Circuit = circuit,
                Follows = true,
                Target = true,
                Pending = -1f,
                Rng = own,
            };
        }

        _circuits = [.. circuits];
    }

    /// <summary>Gets the board this switches.</summary>
    public LampSwitchboard Board => _board;

    /// <summary>Gets how many circuits the lamps were wired into.</summary>
    public int CircuitCount => _circuits.Length;

    /// <summary>Gets a value indicating whether the lamps have been put in their state for the light yet.</summary>
    public bool IsSettled => _settled;

    /// <summary>Returns the circuit a lamp is on.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <returns>The circuit's index, or -1 for an index that names no lamp.</returns>
    public int CircuitOf(int index) => (uint)index < (uint)_cells.Length ? _cells[index].Circuit : -1;

    /// <summary>Returns whether a lamp follows the daylight, or is simply kept on.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <returns><see langword="true"/> when the daylight decides whether it is fed.</returns>
    public bool Follows(int index) => (uint)index < (uint)_cells.Length && _cells[index].Follows;

    /// <summary>Returns the sun heights a lamp's circuit trips at, as sines of the elevation.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <returns>The height under which it comes on, and the height over which it goes off again.</returns>
    public (float On, float Off) ThresholdsOf(int index)
    {
        if ((uint)index >= (uint)_cells.Length)
        {
            return (0f, 0f);
        }

        Circuit circuit = _circuits[_cells[index].Circuit];
        return (circuit.OnSine, circuit.OffSine);
    }

    /// <summary>Puts a lamp on the daylight's circuit, or takes it off so it is simply kept on.</summary>
    /// <param name="index">The fixture's index.</param>
    /// <param name="follows">Whether the daylight is to decide.</param>
    /// <remarks>
    /// Takes effect on the next <see cref="Update"/>, through the same delays as anything else, so a lamp
    /// taken off the daylight at noon comes on the way a lamp does rather than appearing.
    /// </remarks>
    public void SetFollows(int index, bool follows)
    {
        if ((uint)index < (uint)_cells.Length)
        {
            _cells[index].Follows = follows;
        }
    }

    /// <summary>Asks for the next <see cref="Update"/> to snap every lamp to the light, with nothing animated.</summary>
    /// <remarks>
    /// For a new level, or a level's clock being put back to its opening hour: a state to be in, not a
    /// change to be watched.
    /// </remarks>
    public void Unsettle() => _settled = false;

    /// <summary>Puts every lamp straight into the state the light calls for, silently and at once.</summary>
    /// <param name="light">The sine of the sun's elevation, less whatever the weather takes off it.</param>
    public void Settle(float light)
    {
        for (int c = 0; c < _circuits.Length; c++)
        {
            // Mid-band, so a level opening inside a circuit's dead band opens it the way the hour reads
            // rather than the way it would have been left by a dusk nobody watched.
            ref Circuit circuit = ref _circuits[c];
            circuit.Wants = light < (circuit.OnSine + circuit.OffSine) * 0.5f;
        }

        for (int i = 0; i < _cells.Length; i++)
        {
            ref Cell cell = ref _cells[i];
            bool desired = !cell.Follows || _circuits[cell.Circuit].Wants;

            cell.Target = desired;
            cell.Pending = -1f;
            _board.SetSupplyImmediately(i, desired);
        }

        _settled = true;
    }

    /// <summary>Lets the photocells see the light, and the lamps follow on their own delays.</summary>
    /// <param name="seconds">Real seconds since the last call.</param>
    /// <param name="light">The sine of the sun's elevation, less whatever the weather takes off it.</param>
    /// <remarks>
    /// Real time rather than the day's clock for the delays, because a photocell's delay is a property of
    /// the sensor and not of the sun: it is a second or two however fast the sky is moving. The first call
    /// after construction or <see cref="Unsettle"/> settles instead.
    /// </remarks>
    public void Update(float seconds, float light)
    {
        if (!_settled)
        {
            Settle(light);
            return;
        }

        float step = Math.Clamp(seconds, 0f, 0.25f);

        for (int c = 0; c < _circuits.Length; c++)
        {
            ref Circuit circuit = ref _circuits[c];

            // The dead band: on below the one height, off only once above the other.
            bool wants = circuit.Wants ? light < circuit.OffSine : light < circuit.OnSine;

            if (wants != circuit.Wants)
            {
                circuit.Wants = wants;
                circuit.Delay = ReactionDelay(ref circuit.Rng, wants);
            }
        }

        for (int i = 0; i < _cells.Length; i++)
        {
            ref Cell cell = ref _cells[i];
            bool desired = !cell.Follows || _circuits[cell.Circuit].Wants;

            if (desired != cell.Target)
            {
                cell.Target = desired;

                // The light came back before the delay ran out: as if it had never tripped.
                cell.Pending = desired == _board.IsSupplied(i)
                    ? -1f
                    : (cell.Follows ? _circuits[cell.Circuit].Delay : 0f) + Jitter(ref cell.Rng, desired);
            }

            if (cell.Pending >= 0f)
            {
                cell.Pending -= step;

                if (cell.Pending <= 0f)
                {
                    cell.Pending = -1f;
                    _board.SetSupply(i, cell.Target);
                }
            }
        }
    }

    /// <summary>Wires a new circuit: its photocell's thresholds and its own generator.</summary>
    /// <remarks>
    /// <para>
    /// A street-lighting photocell switches on at a few tens of lux, which is a sun a few degrees under the
    /// horizon, and most of these trip between four degrees under and two over. About one in eight is an
    /// early one -- shaded, or dirty enough to read dusk an hour soon -- and comes on with the sun still
    /// several degrees up, which is the lamp somebody always notices burning in daylight.
    /// </para>
    /// <para>
    /// The dead band is a couple of times the switch-on light, as on real cells, so the morning trips at a
    /// higher sun than the evening did.
    /// </para>
    /// </remarks>
    private static Circuit NewCircuit(ref uint rng)
    {
        uint own = (rng = (rng * 0x2C9277B5u) + 0xAC564B05u) | 1u;
        float onSine = -0.07f + (NextFloat(ref own) * 0.11f);

        if (NextFloat(ref own) < 0.12f)
        {
            onSine += 0.04f + (NextFloat(ref own) * 0.05f);
        }

        return new Circuit
        {
            OnSine = onSine,
            OffSine = onSine + 0.025f + (NextFloat(ref own) * 0.045f),
            Rng = own,
        };
    }

    /// <summary>How long a circuit's photocell waits after it trips before its contactor closes or opens.</summary>
    /// <remarks>
    /// Real cells are built to wait, so a car's headlights do not put a street out. Mostly a second or two
    /// here; about one trip in ten is a lazy one that takes far longer, which is the circuit still dark
    /// well after its neighbours are lit.
    /// </remarks>
    private static float ReactionDelay(ref uint rng, bool on)
    {
        float roll = NextFloat(ref rng);
        float delay = NextFloat(ref rng) < 0.1f
            ? 5f + (roll * 9f)
            : 0.3f + (roll * roll * 3.2f);

        // Going off at dawn the cell has the same delay, but nobody is waiting for it.
        return on ? delay : delay * 0.8f;
    }

    /// <summary>One lamp's own share of the wait: its ballast, its starter, its place on the cable.</summary>
    private static float Jitter(ref uint rng, bool on) =>
        on ? 0.05f + (NextFloat(ref rng) * 0.9f) : NextFloat(ref rng) * 0.35f;

    private static float NextFloat(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;

        return (state >> 8) * (1f / 16777216f);
    }

    /// <summary>One photocell and the contactor it drives.</summary>
    private struct Circuit
    {
        /// <summary>The sun height under which it switches its lamps on.</summary>
        public float OnSine;

        /// <summary>The sun height over which it switches them off again.</summary>
        public float OffSine;

        /// <summary>Whether the cell currently reads night.</summary>
        public bool Wants;

        /// <summary>The wait it rolled when it last tripped, in seconds.</summary>
        public float Delay;

        public uint Rng;
    }

    /// <summary>One lamp's place on the wiring.</summary>
    private struct Cell
    {
        public int Circuit;

        /// <summary>Whether the daylight decides this lamp, or it is simply kept fed.</summary>
        public bool Follows;

        /// <summary>The supply this lamp is on its way to.</summary>
        public bool Target;

        /// <summary>Seconds until the supply changes, or negative with nothing pending.</summary>
        public float Pending;

        public uint Rng;
    }
}
