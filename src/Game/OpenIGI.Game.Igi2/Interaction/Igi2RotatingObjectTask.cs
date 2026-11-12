using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Objects;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>Something that turns where it stands -- a fan, a dish, a beacon: I.G.I. 2's <c>RotatingObject</c>.</summary>
/// <remarks>
/// <c>RotatingObject_RunHandler</c> is the rule both games share (<see cref="RotationRule"/>): switched off by its "On
/// expression", or shot out where it is "Destructable", its target speeds are zeroed for good; each axis's speed steps toward
/// its target by its acceleration, each angle gains its speed, and the object faces its placed orientation turned by the
/// three. It starts at its authored speeds. Its "Rotate sound" is played with how hard it is turning, and what the level
/// put beneath it is carried round with it.
/// </remarks>
public sealed partial class Igi2RotatingObjectTask : Igi2Task, IIgi2PlacedTask, IIgi2Mover, IAttachParent, IWeaponHitTarget
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "RotatingObject";

    private readonly float[] _level = new float[1];
    private Matrix3f _placed = Matrix3f.Identity;
    private float[] _orientation = EngineEuler.Rotation(0f, 0f, 0f);
    private float[] _lastOrientation = EngineEuler.Rotation(0f, 0f, 0f);
    private Igi2Pose? _built;
    private IReadOnlyList<Igi2RigidObject> _solids = [];
    private LevelExpression? _on;
    private Igi2Sound? _sound;
    private bool _soundAsked;
    private float _targetX;
    private float _targetY;
    private float _targetZ;

    /// <summary>Gets or sets where the level placed it.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets which way the level placed it.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound it makes turning, played with how hard it turns.</summary>
    [ScriptParam(ScriptParamType.String16, "Rotate sound")]
    public string RotateSound { get; set; } = string.Empty;

    /// <summary>Gets or sets its speed about X, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Degrees per second X-axis")]
    public float SpeedX { get; set; }

    /// <summary>Gets or sets its speed about Y, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Degrees per second Y-axis")]
    public float SpeedY { get; set; }

    /// <summary>Gets or sets its speed about Z, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Degrees per second Z-axis")]
    public float SpeedZ { get; set; }

    /// <summary>Gets or sets how fast its speed about X changes, in degrees a second each second.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Acceleration, degrees per second X-axis")]
    public float AccelerationX { get; set; }

    /// <summary>Gets or sets how fast its speed about Y changes.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Acceleration, degrees per second Y-axis")]
    public float AccelerationY { get; set; }

    /// <summary>Gets or sets how fast its speed about Z changes.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Acceleration, degrees per second Z-axis")]
    public float AccelerationZ { get; set; }

    /// <summary>Gets or sets a value indicating whether a round stops it for good.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Destructable")]
    public bool Destructable { get; set; }

    /// <summary>Gets or sets the expression that keeps it turning; empty is always.</summary>
    [ScriptParam(ScriptParamType.VarString, "On expression")]
    public string OnScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the level's sounds.</summary>
    [LevelService]
    public Igi2Sounds? Sounds { get; set; }

    /// <summary>Gets or sets what finds a model by name, which its solids are turned with.</summary>
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets the magic object types its model's magic vertices make.</summary>
    public World.Igi2MagicObjTypes? Magic { get; set; }

    /// <summary>Gets a value indicating whether a round has stopped it.</summary>
    public bool IsDestroyed { get; private set; }

    /// <summary>Gets its speed about each axis now, in degrees a second.</summary>
    public Vector3f Speed { get; private set; }

    /// <summary>Gets how far it has turned about each axis, in degrees.</summary>
    public Vector3f Angles { get; private set; }

    /// <inheritdoc/>
    public Igi2Pose Built => _built ??= Igi2Pose.FromAngles(Position, Orientation);

    /// <inheritdoc/>
    public Vector3d CurrentPosition => Position;

    /// <inheritdoc/>
    public IReadOnlyList<float> CurrentOrientation => _orientation;

    /// <inheritdoc/>
    public Vector3d LastPosition => Position;

    /// <inheritdoc/>
    public IReadOnlyList<float> LastOrientation => _lastOrientation;

    /// <inheritdoc/>
    public Vector3d AttachPosition => Position;

    /// <inheritdoc/>
    public IReadOnlyList<float> AttachOrientation => _orientation;

    /// <summary>Gives it the solids the world built for it, which turn with it.</summary>
    /// <param name="solids">The solids.</param>
    /// <exception cref="ArgumentNullException"><paramref name="solids"/> is <see langword="null"/>.</exception>
    public void Bind(IReadOnlyList<Igi2RigidObject> solids) => _solids = solids ?? throw new ArgumentNullException(nameof(solids));

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _on ??= Igi2LevelExpressions.Create(OnScript, Name, level.Natives);
        bool switchedOff = !_on.IsEmpty && unchecked((byte)LevelExpression.Truncate(level.Evaluate(_on))) == 0;
        if (switchedOff || IsDestroyed)
        {
            _targetX = 0f;
            _targetY = 0f;
            _targetZ = 0f;
        }

        Vector3f speed = new(
            RotationRule.StepSpeed(Speed.X, _targetX, AccelerationX),
            RotationRule.StepSpeed(Speed.Y, _targetY, AccelerationY),
            RotationRule.StepSpeed(Speed.Z, _targetZ, AccelerationZ));
        Speed = speed;
        Angles = new Vector3f(
            RotationRule.StepAngle(Angles.X, speed.X),
            RotationRule.StepAngle(Angles.Y, speed.Y),
            RotationRule.StepAngle(Angles.Z, speed.Z));

        _lastOrientation = _orientation;
        Matrix3f now = RotationRule.Orientation(_placed, Angles.X, Angles.Y, Angles.Z);
        _orientation = [now.M11, now.M12, now.M13, now.M21, now.M22, now.M23, now.M31, now.M32, now.M33];

        // Its sound is made once, and played with how hard it turns.
        if (!_soundAsked)
        {
            _soundAsked = true;
            _sound = RotateSound.Length > 0 ? Sounds?.New(RotateSound, Position, parameters: () => _level) : null;
        }

        _level[0] = RotationRule.SoundLevel(speed.X, speed.Y, speed.Z, SpeedX, SpeedY, SpeedZ);

        if (_solids.Count > 0 && Models is { } models)
        {
            Igi2CollisionWorld.Repose(_solids, models, Model, Igi2Pose.FromMatrix(Position, now), Magic);
        }

        // RigidDynCubeObj_UpdateAttachments: what it carries goes round with it.
        Igi2Attachment.UpdateChildren(this);
    }

    /// <inheritdoc/>
    /// <remarks><c>RotatingObject_WeaponHitHandler</c>: a round stops one that is "Destructable", and nothing starts it again.</remarks>
    public void WeaponHit(in WeaponHit hit)
    {
        if (Destructable)
        {
            IsDestroyed = true;
        }
    }

    /// <inheritdoc/>
    /// <remarks><c>RotatingObject_UpdateInternalDataHandler</c>: it starts at its authored speeds, not from rest.</remarks>
    protected override void OnParsed()
    {
        float[] placed = EngineEuler.Rotation(Orientation.X, Orientation.Y, Orientation.Z);
        _placed = new Matrix3f(placed[0], placed[1], placed[2], placed[3], placed[4], placed[5], placed[6], placed[7], placed[8]);
        _orientation = placed;
        _lastOrientation = placed;
        _targetX = SpeedX;
        _targetY = SpeedY;
        _targetZ = SpeedZ;
        Speed = new Vector3f(SpeedX, SpeedY, SpeedZ);
        Angles = default;
        IsDestroyed = false;
        _sound = null;
        _soundAsked = false;
    }
}
