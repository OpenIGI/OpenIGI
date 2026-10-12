using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>What a ladder tells the human that found it: whether it was found from the top.</summary>
public interface IIgi2LadderClimber
{
    /// <summary>Sets a value indicating whether the ladder was found from its top, <c>isLadderActivateTop</c>.</summary>
    bool IsLadderActivateTop { set; }
}

/// <summary>
/// A ladder, I.G.I. 2's <c>Ladder</c> (ladder.c): a rigid object the player climbs. Its model's magic vertices mark it: the first its
/// top for finding it from above, the second its foot and the third its top for climbing, the zeroth its foot for sliding down.
/// </summary>
/// <remarks>
/// <para>
/// <c>Ladder_UpdateSteps</c>: its steps are the height between its second and third vertices over 1228.8 units, chopped.
/// </para>
/// <para>
/// <c>Ladder_EvaluateActivateHandler</c>, from its foot: a view less than one and a half metres ahead of it along its Y (or less
/// than three quarters behind it) and within 0.7 m to either side, facing it within a quarter turn, between its foot and its
/// first vertex in height, takes it outright when a line from the view to it at the view's height is clear. Then, from its top: a
/// view on the climbing side of its first vertex that resolves it, with a clear line to that vertex, takes it from the top.
/// A blocked line forgets the best target found so far.
/// </para>
/// </remarks>
public sealed partial class Igi2LadderTask : Igi2Task, IIgi2PlacedTask, IIgi2Activatable
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Ladder";

    /// <summary>The steps a unit of height makes, <c>1 / 1228.8</c>.</summary>
    public const double StepsPerUnit = 0.0008138020833333334;

    /// <summary>The groups its lines of sight look through, <c>Forest_bQTaskGroup | 0x8000000</c>.</summary>
    public const uint SightGroups = Igi2CollisionWorld.ForestGroup | 0x8000000;

    private float[] _frame = Igi2Pose.Rotation(0f, 0f, 0f);

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets where it stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its turn, as three Euler angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets its steps, <c>nSteps</c>.</summary>
    public int Steps { get; private set; }

    /// <summary>Gets its frame, row by row.</summary>
    public IReadOnlyList<float> Frame => _frame;

    /// <summary>Gets or sets the level's models.</summary>
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets what its lines of sight are traced through.</summary>
    public Detection.Igi2DetectionWorld? Sight { get; set; }

    /// <inheritdoc/>
    public Vector3d Centre => Position;

    /// <inheritdoc/>
    public float Radius => Models?.Invoke(Model)?.NativeMarkerBound ?? 0f;

    /// <inheritdoc/>
    public Igi2ActivateKind Kind => Igi2ActivateKind.Ladder;

    /// <summary>Gets or sets which of the solids met are its own, which its lines of sight pass through.</summary>
    /// <remarks>A placed ladder's are those built under its id; one an attach point makes passes its own test.</remarks>
    public Func<object, bool>? IsOwnSolid { get; set; }

    /// <summary>
    /// Makes a ladder of an attach point of the magic type, as <c>Ladder_CreateHandler</c> does with a creation context: its model,
    /// position and frame the point's.
    /// </summary>
    /// <param name="placement">The attach point's object.</param>
    /// <param name="models">The level's models.</param>
    /// <returns>The ladder.</returns>
    public static Igi2LadderTask FromAttachPoint(Collision.Igi2MagicPlacement placement, Func<string, MefModel?> models)
    {
        ArgumentNullException.ThrowIfNull(placement);
        Igi2LadderTask ladder = new()
        {
            Model = placement.Type.Model,
            Position = placement.Pose.Position,
            Models = models,
            IsOwnSolid = owner => owner.Equals(placement.Solid),
            _isFramed = true,
            _frame = placement.Pose.ToArray(),
        };
        ladder.Prepare();
        return ladder;
    }

    private bool _isFramed;

    /// <summary>Readies its frame and its steps, <c>Ladder_UpdateInternalDataHandler</c> and <c>Ladder_UpdateSteps</c>.</summary>
    public void Prepare()
    {
        if (!_isFramed)
        {
            _frame = Igi2Pose.Rotation(Orientation.X, Orientation.Y, Orientation.Z);
        }

        MefModel? model = Models?.Invoke(Model);
        Vector3f foot = default, top = default;
        if (model is not null)
        {
            model.TryGetMagicVertex(2, out foot);
            model.TryGetMagicVertex(3, out top);
        }

        Fpu height = Fpu.Abs((Fpu)foot.Z - top.Z);
        Steps = Fpu.ToInt32(height * StepsPerUnit);
    }

    /// <summary>A magic vertex of its model, where it stands in the world.</summary>
    /// <param name="index">The vertex.</param>
    /// <returns>The vertex's position.</returns>
    public Vector3d MagicVertex(int index) => Igi2MagicVertex.Position(Models?.Invoke(Model), index, _frame, Position);

    /// <inheritdoc/>
    public void EvaluateActivate(Igi2EvaluateActivate context)
    {
        ArgumentNullException.ThrowIfNull(context);
        float[] m = _frame;
        Vector3d view = context.ViewPosition;
        Fpu dx = (Fpu)view.X - Position.X, dy = (Fpu)view.Y - Position.Y, dz = (Fpu)view.Z - Position.Z;
        Fpu across = (((Fpu)m[6] * dz) + ((Fpu)m[3] * dy)) + ((Fpu)m[0] * dx);
        Fpu ahead = (((Fpu)m[4] * dy) + ((Fpu)m[7] * dz)) + ((Fpu)m[1] * dx);
        bool isNear = ahead.Value >= 0.0
            ? ahead.Value < 6144.0 && ahead.Value < Igi2EvaluateActivate.Reach
            : ahead.Value > -3072.0 && -ahead.Value < Igi2EvaluateActivate.Reach;
        if (isNear && across.Value > -2867.2 && across.Value < 2867.2)
        {
            IReadOnlyList<float> v = context.ViewOrientation;
            float sign = ahead.Value < 0.0 ? -1f : 1f;
            Vector3f facing = new(v[1] * sign, v[4] * sign, v[7] * sign);
            Vector3f toward = new(-m[1], -m[4], -m[7]);
            Vector3d top = MagicVertex(1);
            if (Angle(facing, toward) < 0.785398185f && view.Z < top.Z && view.Z > Position.Z)
            {
                if (!IsClear(context, new Vector3d(Position.X, Position.Y, view.Z)))
                {
                    return;
                }

                context.Take(this);
                if (context.Activator is IIgi2LadderClimber climber)
                {
                    climber.IsLadderActivateTop = false;
                }
            }
        }

        Vector3d high = MagicVertex(1);
        Fpu side = (((Fpu)m[4] * ((Fpu)view.Y - high.Y)) + ((Fpu)m[7] * ((Fpu)view.Z - high.Z))) + ((Fpu)m[1] * ((Fpu)view.X - high.X));
        if (side.Value < 0.0 && context.Resolve(high, this))
        {
            if (!IsClear(context, high))
            {
                return;
            }

            context.Take(this);
            if (context.Activator is IIgi2LadderClimber climber)
            {
                climber.IsLadderActivateTop = true;
            }
        }
    }

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Position" => Position,
            "Orientation" => Orientation,
            "Model" => Model,
            _ => null,
        };
        return value is not null;
    }

    /// <inheritdoc/>
    protected override void OnParsed() => Prepare();

    /// <summary>The angle between two directions, <c>Real32x3_Angle</c>: nought for a zero one.</summary>
    private static float Angle(Vector3f a, Vector3f b)
    {
        if ((a.X == 0f && a.Y == 0f && a.Z == 0f) || (b.X == 0f && b.Y == 0f && b.Z == 0f))
        {
            return 0f;
        }

        Fpu dot = (((Fpu)a.Z * b.Z) + ((Fpu)a.Y * b.Y)) + ((Fpu)a.X * b.X);
        Fpu lengths = Fpu.Sqrt((((Fpu)a.X * a.X) + ((Fpu)a.Y * a.Y)) + ((Fpu)a.Z * a.Z))
            * Fpu.Sqrt((((Fpu)b.X * b.X) + ((Fpu)b.Y * b.Y)) + ((Fpu)b.Z * b.Z));
        double cosine = (dot / lengths).Value;
        return cosine >= 1.0 ? 0f : cosine > -1.0 ? (float)Math.Acos(cosine) : 3.1415927f;
    }

    /// <summary>
    /// Whether a line from the view to a point is clear and lets everything through, passing through the human looking and the
    /// ladder; a blocked one forgets the best target found so far.
    /// </summary>
    private bool IsClear(Igi2EvaluateActivate context, Vector3d to)
    {
        object? human = context.Activator;
        bool clear = Sight is not { } sight
            || (sight.LineOfSight(
                    context.ViewPosition,
                    to,
                    SightGroups,
                    owner => ReferenceEquals(owner, human) || (IsOwnSolid?.Invoke(owner) ?? (owner is Igi2CollisionOwner { LevelId: var id } && id == Id)),
                    out float transparency)
                && transparency == 1f);
        if (!clear)
        {
            context.Clear();
        }

        return clear;
    }
}
