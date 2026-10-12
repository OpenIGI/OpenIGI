using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>What a squad tells a soldier to do, <c>AISquadOrder_e</c>.</summary>
public enum Igi2AiSquadOrderKind
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Look at a position.</summary>
    LookAtPosition = 1,

    /// <summary>Look at where an event happened.</summary>
    LookAtEvent = 2,

    /// <summary>Look roughly the way an event came from.</summary>
    LookInApproxDirection = 3,

    /// <summary>Walk or run to a node.</summary>
    MoveToNode = 4,

    /// <summary>Fire on the squad's targets.</summary>
    Fire = 5,

    /// <summary>Keep a place in the formation around the leader.</summary>
    Formation = 6,

    /// <summary>Go and see what happened.</summary>
    Inquire = 7,

    /// <summary>Hold while the other team advances.</summary>
    WaitForLeapfrog = 8,

    /// <summary>Use something: an alarm, a gun.</summary>
    Activate = 9,

    /// <summary>Walk a patrol.</summary>
    Patrol = 10,

    /// <summary>Throw a grenade at the target.</summary>
    ThrowGrenade = 11,

    /// <summary>Take cover from a danger.</summary>
    TakeCover = 12,

    /// <summary>Sweep the eyes over the surroundings.</summary>
    VisualSweep = 13,
}

/// <summary>
/// An order a squad gives one of its soldiers, <c>AISquadOrderContext_s</c>: its kind, the ticks before it is acted on, and what
/// the kind needs (the C keeps these in one union; each kind reads only its own).
/// </summary>
public sealed class Igi2AiSquadOrder
{
    /// <summary>Gets or sets the kind.</summary>
    public Igi2AiSquadOrderKind Kind { get; set; }

    /// <summary>Gets or sets the ticks before the soldier acts on it, <c>iDelay</c>.</summary>
    public int Delay { get; set; }

    /// <summary>Gets or sets the position to look at, inquire at, or take cover from.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the direction the danger came from, for <see cref="Igi2AiSquadOrderKind.TakeCover"/>.</summary>
    public Vector3f Direction { get; set; }

    /// <summary>Gets or sets the node to move to, the task to activate, or the patrol to walk.</summary>
    public int Node { get; set; }

    /// <summary>Gets or sets a value indicating whether to run there.</summary>
    public bool IsRun { get; set; }

    /// <summary>Gets or sets the rank the look takes, or its ticks for a rough look (<c>nTimer</c>).</summary>
    public int Timer { get; set; }

    /// <summary>Gets or sets the sectors a rough look divides the circle into, <c>nSectorResolution</c>.</summary>
    public int SectorResolution { get; set; }

    /// <summary>Gets or sets the leader to keep formation on.</summary>
    public Igi2HumanAi? Leader { get; set; }

    /// <summary>Gets or sets the place in formation, relative to the leader.</summary>
    public Vector3f RelativePosition { get; set; }

    /// <summary>Gets or sets the way to face in formation, relative to the leader.</summary>
    public Vector3f RelativeDirection { get; set; }

    /// <summary>Gets or sets the targets to fire on.</summary>
    public IReadOnlyList<Igi2AiSquadTarget> Targets { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether to crouch for the sweep.</summary>
    public bool IsCrouch { get; set; }
}

/// <summary>An enemy a squad knows of, <c>AISquadEnemyTargetData_s</c>: where it was last reported, and for how long it has not been.</summary>
public sealed class Igi2AiSquadTarget
{
    /// <summary>Initializes a new instance of the <see cref="Igi2AiSquadTarget"/> class.</summary>
    /// <param name="enemy">The enemy.</param>
    /// <param name="position">Where it was reported.</param>
    public Igi2AiSquadTarget(IIgi2Detectable enemy, Vector3d position)
    {
        Enemy = enemy;
        LastPosition = position;
    }

    /// <summary>Gets the enemy.</summary>
    public IIgi2Detectable Enemy { get; }

    /// <summary>Gets or sets where it was last reported.</summary>
    public Vector3d LastPosition { get; set; }

    /// <summary>Gets or sets the ticks since it was last reported, <c>nTimer</c>.</summary>
    public int Timer { get; set; }
}

/// <summary>
/// The squad formations, <c>AISquad_aatFormationData[5][8]</c> as <c>AISquad_InitFormations</c> builds them: each member's place
/// (in formation-distance units: left, forward, up) and facing, relative to the leader at index nought.
/// </summary>
public static class Igi2AiFormations
{
    /// <summary>No formation.</summary>
    public const int None = 0;

    /// <summary>A V behind the leader.</summary>
    public const int V = 1;

    /// <summary>A ring around the leader.</summary>
    public const int Diamond = 2;

    /// <summary>A column, alternating sides.</summary>
    public const int StaggeredColumn = 3;

    /// <summary>A V ahead of the leader.</summary>
    public const int InvertedV = 4;

    private static readonly (Vector3f Position, Vector3f Direction)[,] Data = Build();

    /// <summary>Gets a member's place and facing in a formation.</summary>
    /// <param name="formation">The formation.</param>
    /// <param name="member">The member's index in its team.</param>
    /// <returns>The place and facing.</returns>
    public static (Vector3f Position, Vector3f Direction) Of(int formation, int member) => Data[formation, member];

    private static (Vector3f, Vector3f)[,] Build()
    {
        var data = new (Vector3f, Vector3f)[5, 8];
        for (int i = 1; i < 8; i++)
        {
            float sign = (i & 1) != 0 ? -1f : 1f;
            float half = (i + 1) / 2;
            data[V, i] = (new Vector3f(half * sign, -half, 0f), new Vector3f(0f, 1f, 0f));

            double angle = i * 0.7853981852531433;
            float across = (float)-Math.Sin(angle);
            float along = (float)Math.Cos(angle);
            data[Diamond, i] = (new Vector3f(across, (float)(along - 1.0), 0f), new Vector3f(across, along, 0f));

            data[StaggeredColumn, i] = (new Vector3f(sign, -i, 0f), new Vector3f(sign, 1f, 0f));
            data[InvertedV, i] = (new Vector3f(half * sign, (float)((i + 1) * 0.5), 0f), new Vector3f(0f, 1f, 0f));
        }

        return data;
    }
}
