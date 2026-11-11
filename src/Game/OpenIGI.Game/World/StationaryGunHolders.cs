using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.World;

/// <summary>
/// Project I.G.I.'s <c>AIStationaryGunHolder</c>: a fixed model that watches through a cone of its own and carries
/// <c>AIStationaryGun</c> attachments, which turn to the first enemy it sees and fire.
/// </summary>
/// <remarks>
/// <para>
/// Its update (<c>0x46E5B0</c>): the team expression, where it has one, sets its side and the on expression whether it
/// watches. While on, its record sweeps from the holder's own position and orientation; turned off, the record forgets
/// everything and starts again on the counter's next phase. Then its children run, then its guns.
/// </para>
/// <para>
/// The record (<c>0x46E7F0</c>) tracks live enemies only (flags 4), humans only, a cone every fourth tick and a ray every
/// eighth. Its cone is "Viewcone Alpha" and "Viewcone Gamma" in radians, not halved, and "Viewcone Length" metres. Mission
/// 14's holders are on while their room's variable says so, and are on the player's side once the terminal that runs
/// them is hacked (team <c>!Terminal_301.isHacked</c>): from then on their enemies are the guards.
/// </para>
/// </remarks>
internal sealed class StationaryGunHolders(CutsceneContext context, VehicleArmaments armament)
{
    /// <summary>The holder's script type name.</summary>
    public const string TypeName = "AIStationaryGunHolder";

    private const double DegreesToRadians = 0.017453292;

    private readonly CutsceneContext _context = context;
    private readonly VehicleArmaments _armament = armament;
    private readonly Dictionary<GameTask, HolderState> _holders = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets or sets every human a holder's record is offered, in the global detection list's order.</summary>
    public Func<IReadOnlyList<AiDetectable>>? Subjects { get; set; }

    /// <summary>Gets the holders, for tests and diagnostics.</summary>
    public IEnumerable<GameTask> Holders => _holders.Keys;

    /// <summary>Gives a holder its record, on the phase the load assigned it (<see cref="AiVisionPhases"/>).</summary>
    public void Register(GameTask holder, int phase) =>
        _holders[holder] = new HolderState(
            new AiDetectionRecord(AiDetectionRecord.LiveEnemies, phase, coneTicks: 4, rayTicks: 8));

    /// <summary>Gets a holder's side and whether it watches, as its last update left them.</summary>
    public (int Team, bool On)? StateOf(GameTask holder) =>
        _holders.TryGetValue(holder, out HolderState? state) ? (state.Team, state.On) : null;

    /// <summary>Gets a holder's record, for tests and diagnostics.</summary>
    public AiDetectionRecord? RecordOf(GameTask holder) =>
        _holders.TryGetValue(holder, out HolderState? state) ? state.Record : null;

    /// <summary>One holder update (<c>0x46E5B0</c>).</summary>
    /// <param name="holder">The holder task.</param>
    /// <param name="runChildren">Runs the holder's children.</param>
    public void Tick(GameTask holder, Action runChildren)
    {
        ArgumentNullException.ThrowIfNull(runChildren);
        if (holder is not DeclaredTask declared || !_holders.TryGetValue(holder, out HolderState? state))
        {
            runChildren();
            return;
        }

        bool wasOn = state.On;

        // round_int under the game's chop rounding mode: the expressions' values are truncated.
        if (_context.Expressions.TryEvaluateReal(TextOrEmpty(declared, "Team expression"), holder, out double team))
        {
            state.Team = (int)team;
        }

        if (_context.Expressions.TryEvaluateReal(TextOrEmpty(declared, "On expression"), holder, out double on))
        {
            state.On = (byte)(int)on != 0;
        }

        IReadOnlyList<AiDetectable> offered = Subjects?.Invoke() ?? [];
        if (state.On && _context.TaskTransforms.TryGetValue(holder, out CutsceneTransformState? transform))
        {
            AiEyeSnapshot eye = new(
                transform.Position,
                transform.Orientation,
                new AiSnapshotCone(
                    RealConverted(declared, "Viewcone Alpha", 360f) * DegreesToRadians,
                    RealConverted(declared, "Viewcone Gamma", 360f) * DegreesToRadians,
                    RealConverted(declared, "Viewcone Length", 10f) * WorldScene.UnitsPerMetre),
                default,
                secondaryEnabled: false);

            // The sweep compares its phase against the flow tick count, which the walk increments after it runs.
            Vector3d from = eye.Position;
            state.Record.Update(
                _context.SimulationTick - 1,
                eye,
                state.Team,
                offered,
                human => _armament.PartSees(holder, from, human.Mark));
        }
        else if (wasOn)
        {
            state.Record.Reset(_armament.NextVisionPhase());
        }

        runChildren();
        _armament.TickGunHolderGuns(holder, state.Record.FirstSeenEnemy(offered)?.Origin);
    }

    private sealed class HolderState(AiDetectionRecord record)
    {
        public AiDetectionRecord Record { get; } = record;

        // 0x46E210: team 1, off.
        public int Team { get; set; } = 1;

        public bool On { get; set; }
    }
}
