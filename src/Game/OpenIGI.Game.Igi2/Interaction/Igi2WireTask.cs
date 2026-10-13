using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// A wire strung between two points, I.G.I. 2's <c>Wire</c> (wire.c): climbed hand over hand while it is near level, ridden down while
/// it slopes, its model stretched between its ends and its first two magic vertices its ends. The level's scripts follow it through
/// <c>isInUse</c> and <c>isLastInUse</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Wire_UpdateInternalDataHandler</c>: at its ends' midpoint, its Y pointed down the span and scaled by its length over a metre
/// (<see cref="WireTransform"/>). <c>Wire_GetOrientation</c>: the frame along its magic vertices, which is its own. <c>Wire_IsSliding</c>:
/// its tilt, taken as an angle of nought to a full turn, more than two degrees.
/// </para>
/// <para>
/// <c>Wire_EvaluateActivateHandler</c>: the point on it nearest the view between its ends is offered, while its useable expression,
/// where it has one, holds. <c>Wire_ActivateHandler</c>: in use. <c>Wire_RunHandler</c>: what was in use is last in use; its children do
/// not run.
/// </para>
/// </remarks>
public sealed partial class Igi2WireTask : Igi2Task, IIgi2PlacedTask, IIgi2Activatable
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Wire";

    /// <summary>The tilt beyond which a wire is ridden down rather than climbed, two degrees.</summary>
    public const float SlideTilt = 0.03490658476948738f;

    private Igi2LevelExpression? _useable;
    private WireTransform _transform;
    private float[] _frame = Igi2Pose.Rotation(0f, 0f, 0f);
    private byte _isInUse;
    private byte _isLastInUse;

    /// <summary>Gets or sets where it starts.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Start position")]
    public Vector3d Start { get; set; }

    /// <summary>Gets or sets where it stops.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Stop position")]
    public Vector3d Stop { get; set; }

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether it may be used.</summary>
    [ScriptParam(ScriptParamType.VarString, "Useable expression")]
    public string UseableExpression { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether it is in use, <c>isInUse</c>.</summary>
    public bool IsInUse => _isInUse != 0;

    /// <summary>Gets a value indicating whether it was in use last tick, <c>isLastInUse</c>.</summary>
    public bool IsLastInUse => _isLastInUse != 0;

    /// <summary>Gets where it is: its ends' midpoint.</summary>
    public Vector3d Position => _transform.PositionUnits;

    /// <summary>Gets its frame, row by row, <c>Wire_GetOrientation</c>.</summary>
    public IReadOnlyList<float> Frame => _frame;

    /// <summary>Gets or sets the level's models.</summary>
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets the level's run, which its useable expression is evaluated in.</summary>
    public Igi2LevelRun? Level { get; set; }

    /// <inheritdoc/>
    public Vector3d Centre => Position;

    /// <inheritdoc/>
    public float Radius => (float)(((Stop - Start).Length * 0.5) + (Models?.Invoke(Model)?.NativeMarkerBound ?? 0f));

    /// <inheritdoc/>
    public Igi2ActivateKind Kind => Igi2ActivateKind.Wire;

    /// <summary>Gets a value indicating whether it is ridden down rather than climbed, <c>Wire_IsSliding</c>.</summary>
    public bool IsSliding => Igi2Pose.Angles(_frame).Alpha is var alpha && Math.Abs(alpha) > SlideTilt;

    /// <summary>Strings it between its ends, <c>Wire_UpdateInternalDataHandler</c>.</summary>
    public void Prepare()
    {
        if (!WireTransform.TryCreate(Start, Stop, Models?.Invoke(Model), out _transform))
        {
            return;
        }

        Matrix3f o = _transform.Orientation;
        _frame = [o.M11, o.M12, o.M13, o.M21, o.M22, o.M23, o.M31, o.M32, o.M33];
    }

    /// <summary>A magic vertex in its own frame, its Y stretched with the wire, <c>GameFunctions_GetMagicVertexLocal</c>.</summary>
    /// <param name="index">The vertex.</param>
    /// <returns>The vertex.</returns>
    public Vector3f LocalMagicVertex(int index)
    {
        Vector3f v = Models?.Invoke(Model) is { } model && model.TryGetMagicVertex(index, out Vector3f vertex) ? vertex : default;
        return new Vector3f(v.X, ((Fpu)v.Y * _transform.ScaleY).F, v.Z);
    }

    /// <summary>A point in its frame, each axis summed Z, then Y, then X.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The point in its frame.</returns>
    public (double X, double Y, double Z) Local(Vector3d point)
    {
        float[] m = _frame;
        Vector3d p = Position;
        Fpu dx = (Fpu)point.X - p.X, dy = (Fpu)point.Y - p.Y, dz = (Fpu)point.Z - p.Z;
        return (
            ((((Fpu)m[6] * dz) + ((Fpu)m[3] * dy)) + ((Fpu)m[0] * dx)).Value,
            ((((Fpu)m[7] * dz) + ((Fpu)m[4] * dy)) + ((Fpu)m[1] * dx)).Value,
            ((((Fpu)m[8] * dz) + ((Fpu)m[5] * dy)) + ((Fpu)m[2] * dx)).Value);
    }

    /// <summary>A point of its frame in the world.</summary>
    /// <param name="local">The point in its frame.</param>
    /// <returns>The point.</returns>
    public Vector3d World(Vector3d local)
    {
        float[] m = _frame;
        Vector3d p = Position;
        return new Vector3d(
            (((((Fpu)m[2] * local.Z) + ((Fpu)m[0] * local.X)) + ((Fpu)m[1] * local.Y)) + p.X).Value,
            (((((Fpu)m[5] * local.Z) + ((Fpu)m[3] * local.X)) + ((Fpu)m[4] * local.Y)) + p.Y).Value,
            (((((Fpu)m[8] * local.Z) + ((Fpu)m[6] * local.X)) + ((Fpu)m[7] * local.Y)) + p.Z).Value);
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Level = level;
        _isLastInUse = _isInUse;
    }

    /// <inheritdoc/>
    public void EvaluateActivate(Igi2EvaluateActivate context)
    {
        ArgumentNullException.ThrowIfNull(context);
        (_, double y, _) = Local(context.ViewPosition);
        float first = LocalMagicVertex(0).Y, last = LocalMagicVertex(1).Y;
        double along = first <= y ? (last >= y ? y : last) : first;
        Vector3d nearest = World(new Vector3d(0.0, along, 0.0));
        if (UseableExpression.Length > 0 && Level is { } level)
        {
            _useable ??= new Igi2LevelExpression(UseableExpression, Name, level.Natives);
            if (!_useable.IsEmpty && level.Evaluate(_useable) == 0.0)
            {
                return;
            }
        }

        context.Resolve(nearest, this);
    }

    /// <inheritdoc/>
    public void Activate(int activateTime) => _isInUse = 1;

    /// <summary>Lets go of it, as the player getting off does.</summary>
    public void Release() => _isInUse = 0;

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Start position" => Start,
            "Stop position" => Stop,
            "Model" => Model,
            _ => null,
        };
        return value is not null;
    }

    /// <inheritdoc/>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isInUse", () => _isInUse, value => _isInUse = (byte)value, sizeof(byte));
        variables.BindInt(this, "isLastInUse", () => _isLastInUse, value => _isLastInUse = (byte)value, sizeof(byte));
    }

    /// <inheritdoc/>
    protected override void OnParsed() => Prepare();
}
