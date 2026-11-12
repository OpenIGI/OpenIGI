using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Flow;

/// <summary>Something an area may count: a human, as the criteria that count humans see it.</summary>
public interface IIgi2AreaOccupant
{
    /// <summary>Gets its team, <c>tHumanParameters.eTeam</c> (<c>Human_s + 0x1B4</c>).</summary>
    int Team { get; }

    /// <summary>Gets a value indicating whether it is dead, <c>isDead</c> (<c>+0x4A4</c>).</summary>
    bool IsDead { get; }

    /// <summary>Gets a value indicating whether it is active, <c>isActive</c> (<c>+0x4A5</c>).</summary>
    bool IsActive { get; }

    /// <summary>Gets a value indicating whether an AI drives it, <c>ptHumanAIQTask</c> (<c>+0x7A8</c>) set.</summary>
    bool IsAi { get; }

    /// <summary>Gets where its root bone is, <c>BoneDynCubeObj_GetRootPosition</c> (<c>igi2 0x5680F0</c>).</summary>
    Vector3d RootPosition { get; }

    /// <summary>Gets a value indicating whether its mind is a side-kick's, the <c>HumanAI</c>'s first flag (<c>AIFunction_SetSideKick</c>).</summary>
    bool IsSideKick => false;

    /// <summary>Gets where a side-kick is counted at, its root as it holds what it drops (<c>bonedyncubeobj.c sub_580CA0</c>).</summary>
    Vector3d SideKickPosition => RootPosition;
}

/// <summary>
/// What an area counts, I.G.I. 2's <c>AreaActivate</c> criteria: each a bit of an area's mask, registered as a script constant
/// by the module that answers it (<c>AreaActivateQTask_RegisterCriteria</c>, <c>igi2 0x5935E0</c>), in the order <c>J_Open</c>
/// opens them.
/// </summary>
public static class Igi2AreaCriteria
{
    /// <summary>The vehicles inside, <c>CRITERIA_VEHICLE</c> (<c>Vehicle_Open</c>, which <c>H_Open</c> opens first).</summary>
    public const int Vehicle = 1 << 0;

    /// <summary>The camera inside, <c>CRITERIA_CAMERA</c> (<c>GameScreen_Open</c>).</summary>
    public const int Camera = 1 << 1;

    /// <summary>Every active human inside, <c>CRITERIA_HUMAN</c> (<c>Human_Open</c>).</summary>
    public const int Human = 1 << 2;

    /// <summary>The first of the eight teams' bits, <c>CRITERIA_HUMAN0</c> to <c>CRITERIA_HUMAN7</c> (<c>Human_Open</c>).</summary>
    public const int FirstTeam = 1 << 3;

    /// <summary>The number of teams with a bit of their own.</summary>
    public const int Teams = 8;

    /// <summary>The player inside, <c>CRITERIA_HUMANPLAYER</c> (<c>HumanPlayer_Open</c>).</summary>
    public const int HumanPlayer = 1 << 11;

    /// <summary>The side-kicks inside, <c>CRITERIA_HUMANAI_SIDEKICK</c> (<c>HumanAI_Open</c>).</summary>
    public const int SideKick = 1 << 12;

    /// <summary>The number of criteria registered.</summary>
    public const int Count = 13;

    /// <summary>The bit of a team's criterion.</summary>
    /// <param name="team">The team, nought to seven.</param>
    /// <returns>Its bit, <c>Human_abAreaActivateCriteriaHuman[team]</c>.</returns>
    public static int Team(int team) => (uint)team < Teams ? FirstTeam << team : 0;

    /// <summary>Registers the criteria's names as the constants a level's criteria are written in.</summary>
    /// <param name="natives">The namespace.</param>
    public static void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        natives.RegisterConstant("CRITERIA_VEHICLE", Vehicle);
        natives.RegisterConstant("CRITERIA_CAMERA", Camera);
        natives.RegisterConstant("CRITERIA_HUMAN", Human);
        for (int team = 0; team < Teams; team++)
        {
            natives.RegisterConstant("CRITERIA_HUMAN" + team, Team(team));
        }

        natives.RegisterConstant("CRITERIA_HUMANPLAYER", HumanPlayer);
        natives.RegisterConstant("CRITERIA_HUMANAI_SIDEKICK", SideKick);
    }
}

/// <summary>
/// A box that counts what is inside it, I.G.I. 2's <c>AreaActivate</c>, exporting <c>nActive</c> and <c>nClientID</c>.
/// </summary>
/// <remarks>
/// <para>
/// Its tick (<c>AreaActivateQTask_RunHandler</c>, <c>igi2 0x592FC0</c>): each criterion its mask names counts what it answers for
/// inside, in the criteria's order, and the sum is <c>nActive</c> unless detection is off; then its children run.
/// </para>
/// <para>
/// A point is inside (<c>AreaActivateQTask_IsInsideArea</c>, <c>0x593190</c>) when, taken from the box's centre into its frame,
/// it is within half the box's dimensions along each axis, edges included.
/// </para>
/// <para>
/// A team's criterion counts every human of a team in the mask, the dead too unless an AI drives them
/// (<c>Human_AreaActivateTeamCB</c>, <c>0x5007E0</c>); each team bit the mask holds runs it again, so a mask of two teams
/// counts each human twice. <c>CRITERIA_HUMAN</c> counts the active (<c>Human_AreaActivateAllHumansCB</c>, <c>0x500910</c>);
/// <c>CRITERIA_HUMANPLAYER</c> the level's task 0 when it is the player (<c>HumanPlayer_AreaActivateCB</c>, <c>0x49E290</c>).
/// <c>CRITERIA_CAMERA</c> counts the game screen's camera (<c>GameScreen_CameraInAreaCB</c>), <c>CRITERIA_VEHICLE</c> each car
/// at its position (<c>Vehicle_AeraActivateCB</c>, the list <c>Car_CreateHandler</c> registers in), and
/// <c>CRITERIA_HUMANAI_SIDEKICK</c> each human whose mind is a side-kick's, dead or alive (<c>HumanAI_AreaActivateSideKickCB</c>).
/// </para>
/// </remarks>
public sealed partial class Igi2AreaActivateTask : OpenIGI.Game.Flow.AreaActivateTask
{
    private Igi2OrientedBox _box;
    private int _clientId;

    /// <summary>Gets or sets a value indicating whether the count is held as it is, which a network client's is.</summary>
    public bool IsDetectionDisabled { get; set; }

    /// <summary>Gets or sets the level's humans, in <c>Human_ptQTaskList</c>'s order.</summary>
    public IReadOnlyList<IIgi2AreaOccupant> Humans { get; set; } = [];

    /// <summary>Gets the players the player's criterion counts.</summary>
    public PlayerRoster<IIgi2AreaOccupant> Players { get; } = new();

    /// <summary>Gets or sets what reads the game screen's camera.</summary>
    public Func<Vector3d?> CameraPosition { get; set; } = static () => null;

    /// <summary>Gets or sets what reads where the level's cars are.</summary>
    public Func<IEnumerable<Vector3d>> VehiclePositions { get; set; } = static () => [];

    /// <inheritdoc/>
    /// <remarks>The sequel's arithmetic: its rotation and its products cut to 24 bits.</remarks>
    public override void Prepare() => _box = Igi2OrientedBox.From(Position, Orientation, Dimensions);

    /// <inheritdoc/>
    public override bool IsInside(Vector3d point) => _box.IsInside(point, Position);

    /// <inheritdoc/>
    /// <remarks>The area carries its own occupants, so any run of the title's counts.</remarks>
    protected override bool CountsUnder(OpenIGI.Game.Level.LevelRun level) => true;

    /// <inheritdoc/>
    /// <remarks>Each criterion of the mask counted over the area's own occupants; a client's count is left as its server sent it.</remarks>
    protected override void Count(OpenIGI.Game.Level.LevelRun level, int mask)
    {
        int sum = 0;
        for (int criterion = 0; criterion < Igi2AreaCriteria.Count; criterion++)
        {
            int bit = 1 << criterion;
            if ((mask & bit) != 0)
            {
                sum += Count(bit, mask);
            }
        }

        if (!IsDetectionDisabled)
        {
            Active = sum;
        }
    }

    /// <inheritdoc/>
    /// <remarks>Beside the count, I.G.I. 2 exports <c>nClientID</c>.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        base.OnExportVars(variables);
        variables.BindInt(this, "nClientID", () => _clientId, value => _clientId = value);
    }

    private int Count(int criterion, int mask)
    {
        int hits = 0;
        if (criterion == Igi2AreaCriteria.HumanPlayer)
        {
            // The original's one player counts once; of several, the area is satisfied by any inside.
            foreach (IIgi2AreaOccupant player in Players)
            {
                if (IsInside(player.RootPosition))
                {
                    return 1;
                }
            }

            return 0;
        }

        if (criterion == Igi2AreaCriteria.Camera)
        {
            return CameraPosition() is { } eye && IsInside(eye) ? 1 : 0;
        }

        if (criterion == Igi2AreaCriteria.Vehicle)
        {
            foreach (Vector3d vehicle in VehiclePositions())
            {
                if (IsInside(vehicle))
                {
                    hits++;
                }
            }

            return hits;
        }

        if (criterion == Igi2AreaCriteria.SideKick)
        {
            foreach (IIgi2AreaOccupant human in Humans)
            {
                if (human.IsSideKick && IsInside(human.SideKickPosition))
                {
                    hits++;
                }
            }

            return hits;
        }

        if (criterion == Igi2AreaCriteria.Human)
        {
            foreach (IIgi2AreaOccupant human in Humans)
            {
                if (human.IsActive && IsInside(human.RootPosition))
                {
                    hits++;
                }
            }

            return hits;
        }

        if (criterion is >= Igi2AreaCriteria.FirstTeam and < Igi2AreaCriteria.HumanPlayer)
        {
            foreach (IIgi2AreaOccupant human in Humans)
            {
                if ((Igi2AreaCriteria.Team(human.Team) & mask) == 0 || (human.IsAi && human.IsDead))
                {
                    continue;
                }

                if (IsInside(human.RootPosition))
                {
                    hits++;
                }
            }
        }

        return hits;
    }
}
