using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Runtime;

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
public sealed partial class Igi2AreaActivateTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "AreaActivate";

    private float[] _frame = Igi2Pose.Rotation(0f, 0f, 0f);
    private Vector3f _half;
    private int? _mask;
    private int _active;
    private int _clientId;

    /// <summary>Gets or sets the box's centre.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the box's orientation.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the box's size along its three axes.</summary>
    [ScriptParam(ScriptParamType.Real32x3, "Dimensions")]
    public Vector3f Dimensions { get; set; }

    /// <summary>Gets or sets the script naming what it counts.</summary>
    [ScriptParam(ScriptParamType.VarString, "Criteria")]
    public string Criteria { get; set; } = string.Empty;

    /// <summary>Gets the number counted inside, <c>nActive</c>.</summary>
    public int Active => _active;

    /// <summary>Gets its criteria's mask, read from <see cref="Criteria"/> on its first tick, or nought before.</summary>
    public int Mask => _mask ?? 0;

    /// <summary>Gets or sets a value indicating whether it no longer counts, <c>+0xBF</c> (<c>AreaActivateQTask_DisableDetection</c>).</summary>
    public bool IsDetectionDisabled { get; set; }

    /// <summary>Gets or sets the humans of the level, <c>Human_ptQTaskList</c>, in its order.</summary>
    public IReadOnlyList<IIgi2AreaOccupant> Humans { get; set; } = [];

    /// <summary>Gets or sets the level's task 0 when it is the player, or <see langword="null"/>.</summary>
    public IIgi2AreaOccupant? Player { get; set; }

    /// <summary>Gets or sets where the game screen's camera stands, or <see langword="null"/> while there is none.</summary>
    public Func<Vector3d?> CameraPosition { get; set; } = static () => null;

    /// <summary>Gets or sets the level's vehicles, <c>GameFunctions_ptVehicleQTaskList</c>, each by where it is.</summary>
    public Func<IEnumerable<Vector3d>> VehiclePositions { get; set; } = static () => [];

    /// <summary>
    /// Readies the box, <c>AreaActivateQTask_UpdateInternalDataHandler</c> (<c>igi2 0x592CA0</c>): its frame from its angles and its
    /// half dimensions. Its bounds for the world's object tree are not needed here.
    /// </summary>
    public void Prepare()
    {
        Vector3f angles = Orientation;
        _frame = Igi2Pose.Rotation(angles.X, angles.Y, angles.Z);
        Vector3f d = Dimensions;
        _half = new Vector3f(((Fpu)d.X * 0.5).F, ((Fpu)d.Y * 0.5).F, ((Fpu)d.Z * 0.5).F);
    }

    /// <summary>Whether a point is inside the box, <c>AreaActivateQTask_IsInsideArea</c> (<c>igi2 0x593190</c>).</summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> within half the dimensions along each of the box's axes, edges included.</returns>
    public bool IsInside(Vector3d point)
    {
        float[] m = _frame;
        Fpu dx = (Fpu)point.X - Position.X, dy = (Fpu)point.Y - Position.Y, dz = (Fpu)point.Z - Position.Z;
        float x = ((((Fpu)dz * m[6]) + ((Fpu)dy * m[3])) + ((Fpu)dx * m[0])).F;
        float y = ((((Fpu)dz * m[7]) + ((Fpu)dy * m[4])) + ((Fpu)dx * m[1])).F;
        float z = ((((Fpu)dz * m[8]) + ((Fpu)dy * m[5])) + ((Fpu)dx * m[2])).F;

        // Each test passes when the point is not beyond the face, which a value that is not a number never is.
        return !(x > _half.X) && !(y > _half.Y) && !(z > _half.Z)
            && !(-_half.X > x) && !(-_half.Y > y) && !(-_half.Z > z);
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);

        // Script_ParseLineInt32 reads the criteria once its parameters are; here once the level's constants are at hand.
        _mask ??= Criteria.Length == 0
            ? 0
            : (int)Igi2LevelExpression.Truncate(level.Evaluate(new Igi2LevelExpression(Criteria, Name, level.Natives)));
        int mask = _mask.Value;
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
            _active = sum;
        }

        level.RunChildren(this);
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nActive", () => _active, value => _active = value);
        variables.BindInt(this, "nClientID", () => _clientId, value => _clientId = value);
    }

    /// <inheritdoc/>
    protected override void OnParsed() => Prepare();

    private int Count(int criterion, int mask)
    {
        int hits = 0;
        if (criterion == Igi2AreaCriteria.HumanPlayer)
        {
            return Player is { } player && IsInside(player.RootPosition) ? 1 : 0;
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
