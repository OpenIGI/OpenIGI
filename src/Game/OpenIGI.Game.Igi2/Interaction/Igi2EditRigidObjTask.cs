using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Paths;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// A placed model, I.G.I. 2's <c>EditRigidObj</c>: the level's props, which stand where they are put unless a spline path's guide
/// moves them.
/// </summary>
/// <remarks>
/// <para>
/// Its tick (<c>EditRigidObj_RunHandler</c>, <c>igi2 0x5B4860</c>) runs its attached object's, which is no task here, then its
/// children's. What else it does is drawing: its draw queue, shadow protection for one noted as a wall, and portal clipping.
/// </para>
/// <para>
/// A guide turns and moves it; its solids go with it, and its children attached to it follow. A level's queue below the normal
/// one is raised to it when read (<c>EditRigidObj_UpdateInternalDataHandler</c>, <c>0x5B4950</c>); the world scene draws the
/// queue as authored.
/// </para>
/// </remarks>
public sealed partial class Igi2EditRigidObjTask
    : Igi2Task, IIgi2PlacedTask, IIgi2Mover, IAttachParent, IIgi2PathRider, IIgi2CameraTarget
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "EditRigidObj";

    private Vector3d _position;
    private float[] _orientation = Igi2Pose.Rotation(0f, 0f, 0f);
    private Vector3d _lastPosition;
    private float[] _lastOrientation = Igi2Pose.Rotation(0f, 0f, 0f);
    private int _movedTick = int.MinValue;
    private Igi2Pose? _built;
    private IReadOnlyList<Igi2RigidObject> _solids = [];

    /// <summary>Gets or sets where it is placed.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets which way it is placed facing, as level data's three angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the draw queue it is drawn in.</summary>
    [ScriptParam(ScriptParamType.Int32, "RenderGroup")]
    public int RenderGroup { get; set; }

    /// <summary>Gets or sets a value indicating whether shadows are kept off it.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Protect from shadows")]
    public bool IsProtectFromShadows { get; set; }

    /// <summary>Gets or sets a value indicating whether it is drawn whatever the portals say.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Excluded from clipping against portals")]
    public bool IsExcludedFromPortalClipping { get; set; }

    /// <summary>Gets or sets the level's models, which its solids' attachments are read from.</summary>
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets the magic object types its attachments were built through, which it moves them through.</summary>
    public World.Igi2MagicObjTypes? Magic { get; set; }

    /// <summary>Gets the guide that moves it, whose velocity is its parent speed, or <see langword="null"/>.</summary>
    public Igi2SplinePathGuideTask? Guide { get; private set; }

    /// <summary>Gets its parent speed, <c>tAttachContext.ptParentSpeed</c>: its guide's velocity, or nought without one.</summary>
    public Vector3f ParentSpeed => Guide?.Velocity ?? default;

    /// <inheritdoc/>
    public Igi2Pose Built => _built ??= Igi2Pose.FromAngles(Position, Orientation);

    /// <inheritdoc/>
    public Vector3d CurrentPosition => _position;

    /// <inheritdoc/>
    public IReadOnlyList<float> CurrentOrientation => _orientation;

    /// <inheritdoc/>
    public Vector3d LastPosition => _lastPosition;

    /// <inheritdoc/>
    public IReadOnlyList<float> LastOrientation => _lastOrientation;

    /// <inheritdoc/>
    /// <remarks><c>RigidDynCubeObj_GetCameraTargetInfoHandler</c> (<c>igi2 0x58DCE0</c>): where it stands now.</remarks>
    public Vector3d CameraTargetPosition => _position;

    /// <inheritdoc/>
    public IReadOnlyList<float> CameraTargetOrientation => _orientation;

    /// <inheritdoc/>
    public Vector3d AttachPosition => _position;

    /// <inheritdoc/>
    public IReadOnlyList<float> AttachOrientation => _orientation;

    /// <summary>Takes the solids the collision world built of it, which it carries from now on.</summary>
    /// <param name="solids">Its solids, as <see cref="Igi2CollisionWorld.SolidsOf"/> gives them.</param>
    public void Bind(IReadOnlyList<Igi2RigidObject> solids) => _solids = solids ?? throw new ArgumentNullException(nameof(solids));

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
    }

    /// <inheritdoc/>
    /// <remarks>Where it stood when first moved in a tick is kept as where it stood at the tick's start, for drawing between.</remarks>
    public void Ride(Vector3d position, IReadOnlyList<float> orientation, int tick)
    {
        ArgumentNullException.ThrowIfNull(orientation);
        if (tick != _movedTick)
        {
            _movedTick = tick;
            _lastPosition = _position;
            _lastOrientation = [.. _orientation];
        }

        _orientation = [.. orientation];
        _position = position;
        if (_solids.Count > 0 && Models is { } models)
        {
            float[] m = _orientation;
            Igi2CollisionWorld.Repose(_solids, models, Model, Igi2Pose.FromMatrix(_position, new Matrix3f(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8])), Magic);
        }

        foreach (GameTask child in Children)
        {
            (child as IIgi2Attached)?.UpdateAttachment();
        }
    }

    /// <inheritdoc/>
    public void Connect(Igi2SplinePathGuideTask guide) => Guide = guide ?? throw new ArgumentNullException(nameof(guide));

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Position" => Position,
            "Orientation" => Orientation,
            "Model" => Model,
            "RenderGroup" => RenderGroup,
            "Protect from shadows" => IsProtectFromShadows,
            "Excluded from clipping against portals" => IsExcludedFromPortalClipping,
            _ => null,
        };
        return value is not null;
    }

    /// <inheritdoc/>
    /// <remarks>Its frame taken from its parameters' angles, where it stands until something moves it.</remarks>
    protected override void OnParsed()
    {
        Vector3f angles = Orientation;
        _orientation = Igi2Pose.Rotation(angles.X, angles.Y, angles.Z);
        _position = Position;
        _lastPosition = _position;
        _lastOrientation = [.. _orientation];
    }
}
