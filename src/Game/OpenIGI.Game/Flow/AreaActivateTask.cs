using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;
using OpenIGI.Game.Saves;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Flow;

/// <summary>
/// A box that counts, each tick, the things inside it that match its criteria: the engine's <c>AreaActivate</c>, exporting
/// <c>nActive</c>.
/// </summary>
/// <remarks>
/// <para>
/// One type in both games, declared under <c>EditorRigidObj</c> with the same four parameters and the same handlers
/// (Project I.G.I. <c>AreaActivate_Open</c>, run handler <c>0x5048A0</c>; I.G.I. 2 <c>AreaActivateQTask</c>). Its run
/// handler clears the count, adds what the criterion of each bit in its mask finds (<see cref="AreaCriteria"/>), and then
/// runs its children, which so read this tick's count.
/// </para>
/// <para>
/// The mask is its <c>Criteria</c> parameter, a line of script evaluated once (<c>Script_EvalExpr</c>). The box test is the original's: the point taken into the box's
/// frame and held against half its dimensions, a face at a time. This class does it in Project I.G.I.'s arithmetic; a title
/// whose build rounds differently overrides <see cref="IsInside"/>.
/// </para>
/// </remarks>
public partial class AreaActivateTask : TickedTask, IExchangedState
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "AreaActivate";

    private Matrix3f _frame = Matrix3f.Identity;
    private Vector3f _half;
    private int? _mask;

    /// <summary>Gets or sets the box's centre.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the box's orientation, as three angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the box's dimensions along its own axes.</summary>
    [ScriptParam(ScriptParamType.Real32x3, "Dimensions")]
    public Vector3f Dimensions { get; set; }

    /// <summary>Gets or sets the script that gives the criteria mask.</summary>
    [ScriptParam(ScriptParamType.VarString, "Criteria")]
    public string Criteria { get; set; } = string.Empty;

    /// <summary>Gets or sets how many matching things were inside at its last run, <c>nActive</c>.</summary>
    public int Active { get; set; }

    /// <summary>Gets the criteria mask, nought until the area has run.</summary>
    public int Mask => _mask ?? 0;

    /// <summary>Works out the box from its parameters, as the type's parameter-changed handler does (<c>0x5045F0</c>).</summary>
    public virtual void Prepare()
    {
        _frame = Matrix3f.FromEngineEulerAngles(Orientation.X, Orientation.Y, Orientation.Z);
        _half = Dimensions * 0.5f;
    }

    /// <summary>Gets whether a point is inside the box (<c>AreaActivate_Contains</c>).</summary>
    /// <param name="point">The point, in world units.</param>
    /// <returns>Whether it is inside, a face counting as inside.</returns>
    public virtual bool IsInside(Vector3d point)
    {
        double dx = point.X - Position.X, dy = point.Y - Position.Y, dz = point.Z - Position.Z;
        Matrix3f m = _frame;
        float x = (float)((dz * m.M31) + (dy * m.M21) + (dx * m.M11));
        float y = (float)((dz * m.M32) + (dy * m.M22) + (dx * m.M12));
        float z = (float)((dz * m.M33) + (dy * m.M23) + (dx * m.M13));

        // Each test passes when the point is not beyond the face, which a value that is not a number never is.
        return !(x > _half.X) && !(y > _half.Y) && !(z > _half.Z)
            && !(-_half.X > x) && !(-_half.Y > y) && !(-_half.Z > z);
    }

    /// <inheritdoc/>
    public override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Recount(level);
        level.RunChildren(this);
    }

    /// <summary>Counts what is inside afresh: the run handler's own work, before it runs the area's children.</summary>
    /// <param name="level">The level's run, whose criteria count.</param>
    /// <exception cref="ArgumentNullException"><paramref name="level"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A run with no variables cannot read the mask, and one nothing has registered a criterion with has nothing to count by:
    /// neither counts, and the count is left as it was. Not retail, whose one run always had both.
    /// </remarks>
    public void Recount(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (!level.EvaluatesExpressions || !CountsUnder(level))
        {
            return;
        }

        _mask ??= Criteria.Length == 0 ? 0 : (int)LevelExpression.Truncate(level.EvaluateLine(Criteria));
        Count(level, _mask.Value);
    }

    /// <inheritdoc/>
    /// <remarks>What a save keeps of it is the count (I.G.I. 2's serialize list).</remarks>
    public void Exchange(StateExchange exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        Active = exchange.Int(Active);
    }

    /// <summary>Gets whether a run has what this area counts by.</summary>
    /// <param name="level">The run.</param>
    /// <returns>Whether its criteria are registered.</returns>
    protected virtual bool CountsUnder(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return level.Areas.HasAny;
    }

    /// <summary>Counts what is inside and stores it.</summary>
    /// <param name="level">The level's run, whose criteria count.</param>
    /// <param name="mask">The criteria mask.</param>
    protected virtual void Count(LevelRun level, int mask)
    {
        ArgumentNullException.ThrowIfNull(level);
        Active = level.Areas.Count(this, mask);
    }

    /// <inheritdoc/>
    protected override void OnParsed() => Prepare();

    /// <inheritdoc/>
    /// <remarks>Project I.G.I. <c>AreaActivate_Activate</c> (<c>0x504530</c>); I.G.I. 2's register-symbols handler.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nActive", () => Active, value => Active = value);
    }
}
