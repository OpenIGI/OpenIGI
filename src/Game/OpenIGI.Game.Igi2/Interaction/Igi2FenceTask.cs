using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>Something a human's position is told to each tick, <c>GameFunctions_ptNotifyQTaskList</c> (<c>Human_Notify</c>).</summary>
public interface IIgi2Notified
{
    /// <summary>Tells it where a human is, <c>GameFunctions_eNotifyQTaskEvent</c>.</summary>
    /// <param name="human">The human.</param>
    /// <param name="position">Where it is: its feet raised a metre.</param>
    /// <param name="radius">Its sphere's radius.</param>
    void Notify(Igi2Human human, Vector3d position, float radius);
}

/// <summary>Something an electric fence shocks, <c>GameFunctions_eElectricDamageQTaskEvent</c>.</summary>
public interface IIgi2ElectricTarget
{
    /// <summary>Takes a shock.</summary>
    void ElectricDamage();
}

/// <summary>
/// A fence, I.G.I. 2's <c>Fence</c> (fence.c): a rigid object turned by its heading alone, which the player climbs over while it is
/// dead, and which shocks the human against it and hums while its electric expression holds. Its model's first two magic vertices mark
/// its ends along its X.
/// </summary>
/// <remarks>
/// <para>
/// <c>Fence_RunHandler</c>: its electric expression, chopped to a byte, or nought with none, says whether it is live; a fence that
/// comes alive makes an ambient area of <c>elfence_loop</c> two metres up over its middle, as long as it is with a metre and a half to
/// spare, 1.75 m deep and two high, fading over ten metres; one that dies deletes it; then its children run.
/// </para>
/// <para>
/// <c>Fence_NotifyHandler</c>: live, it shocks a human whose raised feet are within 0.6 m of its ends along it, 0.6 m of it across,
/// and between a metre below it and three above. <c>Fence_EvaluateActivateHandler</c>: dead and climbable, it is taken outright by a
/// view across it within reach and between its ends, facing it within a sixth of a turn.
/// </para>
/// </remarks>
public sealed partial class Igi2FenceTask : Igi2Task, IIgi2PlacedTask, IIgi2Fence, IIgi2Notified
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Fence";

    /// <summary>The sound a live fence hums with.</summary>
    public const string HumSound = "elfence_loop";

    /// <summary>The seconds a climb's activation counts, <c>Fence_GetActivateTime</c>.</summary>
    public const float ActivateTime = 6f;

    private LevelExpression? _electric;
    private float[] _frame = Igi2Pose.Rotation(0f, 0f, 0f);
    private Igi2AmbientAreaTask? _hum;

    /// <summary>Gets or sets where it stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its heading.</summary>
    [ScriptParam(ScriptParamType.Angle, "Gamma")]
    public float Gamma { get; set; }

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it can be climbed.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Climbable")]
    public bool IsClimbable { get; set; } = true;

    /// <summary>Gets or sets the expression that says whether it is live.</summary>
    [ScriptParam(ScriptParamType.VarString, "Electric Expression")]
    public string ElectricExpression { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether it is live.</summary>
    public bool IsElectric { get; private set; }

    /// <summary>Gets its hum, while it is live.</summary>
    public Igi2AmbientAreaTask? Hum => _hum;

    /// <summary>Gets its frame, row by row: a turn by its heading.</summary>
    public IReadOnlyList<float> Frame => _frame;

    /// <summary>Gets or sets the level's models.</summary>
    [LevelService]
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets what a hum it makes is given: the sounds, the listener and the run stream.</summary>
    public Action<Igi2AmbientAreaTask>? EquipHum { get; set; }

    /// <inheritdoc/>
    public Vector3d Centre => Position;

    /// <inheritdoc/>
    public float Radius => Models?.Invoke(Model)?.NativeMarkerBound ?? 0f;

    /// <inheritdoc/>
    public Igi2ActivateKind Kind => Igi2ActivateKind.Fence;

    /// <summary>Turns it by its heading, <c>Fence_UpdateInternalDataHandler</c>.</summary>
    public void Prepare()
    {
        float sin = Fpu.Sin(Gamma).F, cos = Fpu.Cos(Gamma).F;
        _frame = [cos, -sin, 0f, sin, cos, 0f, 0f, 0f, 1f];
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _electric ??= Igi2LevelExpressions.Create(ElectricExpression, Name, level.Natives);
        IsElectric = !_electric.IsEmpty && (byte)LevelExpression.Truncate(level.Evaluate(_electric)) != 0;
        if (_hum is not null)
        {
            if (!IsElectric)
            {
                _hum.RemoveFromWorld();
                _hum = null;
            }
        }
        else if (IsElectric)
        {
            _hum = MakeHum();
        }

        level.RunChildren(this);
        _hum?.Run(level);
    }

    /// <inheritdoc/>
    public void Notify(Igi2Human human, Vector3d position, float radius)
    {
        ArgumentNullException.ThrowIfNull(human);
        if (!IsElectric)
        {
            return;
        }

        (double x, double y, double z) = Local(position);
        (Vector3f left, Vector3f right) = Ends();
        if (((Fpu)left.X - 2457.60009765625).Value < x && ((Fpu)right.X + 2457.60009765625).Value > x
            && Math.Abs(y) < 2457.60009765625 && z < 12288.0 && z > -4096.0)
        {
            (human as IIgi2ElectricTarget)?.ElectricDamage();
        }
    }

    /// <inheritdoc/>
    public void EvaluateActivate(Igi2EvaluateActivate context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsElectric || !IsClimbable)
        {
            return;
        }

        float[] m = _frame;
        Vector3d view = context.ViewPosition;
        Fpu dx = (Fpu)view.X - Position.X, dy = (Fpu)view.Y - Position.Y, dz = (Fpu)view.Z - Position.Z;
        double x = ((((Fpu)m[6] * dz) + ((Fpu)m[3] * dy)) + ((Fpu)m[0] * dx)).Value;
        double y = ((((Fpu)m[7] * dz) + ((Fpu)m[1] * dx)) + ((Fpu)m[4] * dy)).Value;
        (Vector3f left, Vector3f right) = Ends();
        if (!(Math.Abs(y) < Igi2EvaluateActivate.Reach && left.X < x && right.X > x))
        {
            return;
        }

        IReadOnlyList<float> v = context.ViewOrientation;
        Vector3f facing = new(v[1], v[4], v[7]);
        float sign = y > 0.0 ? -1f : 1f;
        Vector3f across = new(m[1] * sign, m[4] * sign, m[7] * sign);
        if (Igi2Pose.Angle(facing, across) < 1.0471975803375244)
        {
            context.Take(this);
        }
    }

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Position" => Position,
            "Model" => Model,
            "Gamma" => Gamma,
            _ => null,
        };
        return value is not null;
    }

    /// <inheritdoc/>
    protected override void OnParsed() => Prepare();

    /// <summary>Its first two magic vertices, its ends, in its own frame.</summary>
    private (Vector3f Left, Vector3f Right) Ends()
    {
        MefModel? model = Models?.Invoke(Model);
        Vector3f left = default, right = default;
        model?.TryGetMagicVertex(0, out left);
        model?.TryGetMagicVertex(1, out right);
        return (left, right);
    }

    /// <summary>A point in its frame, each axis summed Z, then Y, then X.</summary>
    private (double X, double Y, double Z) Local(Vector3d point)
    {
        float[] m = _frame;
        Fpu dx = (Fpu)point.X - Position.X, dy = (Fpu)point.Y - Position.Y, dz = (Fpu)point.Z - Position.Z;
        return (
            ((((Fpu)m[6] * dz) + ((Fpu)m[3] * dy)) + ((Fpu)m[0] * dx)).Value,
            ((((Fpu)m[7] * dz) + ((Fpu)m[4] * dy)) + ((Fpu)m[1] * dx)).Value,
            ((((Fpu)m[8] * dz) + ((Fpu)m[5] * dy)) + ((Fpu)m[2] * dx)).Value);
    }

    /// <summary>Its hum, over its middle two metres up, as long as it is with a metre and a half to spare.</summary>
    private Igi2AmbientAreaTask MakeHum()
    {
        (Vector3f left, Vector3f right) = Ends();
        float[] m = _frame;
        Fpu middle = ((Fpu)right.X + left.X) * 0.5;
        Vector3d at = new(
            ((((Fpu)m[0] * middle) + ((Fpu)m[2] * 8192.0)) + ((Fpu)m[1] * 0.0) + Position.X).Value,
            ((((Fpu)m[3] * middle) + ((Fpu)m[5] * 8192.0)) + ((Fpu)m[4] * 0.0) + Position.Y).Value,
            ((((Fpu)m[6] * middle) + ((Fpu)m[8] * 8192.0)) + ((Fpu)m[7] * 0.0) + Position.Z).Value);
        double length = ((((Fpu)right.X - left.X) * 0.0001220703125) + 1.5).Value;
        Igi2AmbientAreaTask hum = new();
        hum.SetParameters(at, m, new Vector3d(length, 1.75, 2.0), 10.0, HumSound);
        EquipHum?.Invoke(hum);
        hum.Prepare();
        return hum;
    }
}
