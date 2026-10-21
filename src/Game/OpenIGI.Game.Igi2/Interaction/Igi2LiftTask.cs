using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// A lift that carries what stands on it from floor to floor, I.G.I. 2's <c>Lift</c>, exporting <c>nInactive</c>,
/// <c>nCurrentFloor</c>, <c>isMoving</c>, <c>isStart</c> and <c>isStop</c>.
/// </summary>
/// <remarks>
/// <para>
/// Its floors are its <see cref="Igi2FloorTask"/> children, in order. Standing still, its <c>Floor up</c> script sends it to the
/// next floor and its <c>Floor down</c> script to the one before (<c>Lift_RunHandler</c>, <c>igi2 0x46E420</c>); while its
/// <c>Can start</c> script allows, it moves along the way between them (<c>Lift_Move</c>, <c>0x46DBA0</c>): its speed eases toward
/// its top speed by its inertia each tick, and in the last tenth of the way slows with what is left of it.
/// </para>
/// <para>
/// Where it stands follows from how far along it is (<c>Lift_UpdatePosition</c>, <c>0x46D640</c>): the position straight
/// between the floors, the orientation slerped between theirs. What it holds moves with it (<see cref="IIgi2Attached"/>), and a
/// body standing on it rides it as on any moving platform.
/// </para>
/// <para>It is still placed by the world scene, which reads its parameters by name (<see cref="IIgi2PlacedTask"/>).</para>
/// </remarks>
public sealed partial class Igi2LiftTask : Igi2Task, IIgi2PlacedTask, IIgi2Mover, IIgi2AttachParent
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Lift";

    /// <summary>How near the far floor a lift starts to slow, as a share of the way.</summary>
    public const float SlowingShare = 0.1f;

    /// <summary>The least a slowing lift moves a tick, as a share of the way.</summary>
    public const float LeastSpeed = 0.001f;

    // One thirtieth, the float 0x3D088889: a speed in metres a second, made units a tick.
    private const float PerTick = 0.033333335f;

    private float[] _startOrientation = Igi2Pose.Rotation(0f, 0f, 0f);
    private float[] _stopOrientation = Igi2Pose.Rotation(0f, 0f, 0f);
    private float[] _orientation = Igi2Pose.Rotation(0f, 0f, 0f);
    private float[] _lastOrientation = Igi2Pose.Rotation(0f, 0f, 0f);
    private Vector3d _startPosition;
    private Vector3d _stopPosition;
    private Vector3d _position;
    private Vector3d _lastPosition;
    private Igi2FloorTask? _activeFloor;
    private float _speed;
    private float _along;
    private int _inactive;
    private byte _isMovingLastFrame;
    private byte _isMoving;
    private byte _isStart;
    private byte _isStop;
    private int _currentFloor;
    private int _floorCache;
    private byte _isLastCanStart = 1;
    private Vector3f _delta;
    private int _runTick = -1;
    private LevelExpression? _floorUp;
    private LevelExpression? _floorDown;
    private LevelExpression? _canStart;
    private Igi2Sound? _moveSound;
    private IReadOnlyList<Igi2RigidObject> _solids = [];
    private Igi2Pose? _built;
    private bool _started;

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the level id of the floor it starts at, or zero or less for its first.</summary>
    [ScriptParam(ScriptParamType.Int32, "Start floor task ID")]
    public int FirstFloorId { get; set; }

    /// <summary>Gets or sets its top speed, in metres a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max speed")]
    public float MaxSpeed { get; set; } = 5f;

    /// <summary>Gets or sets how much its speed changes a tick toward the top, as a share of the way.</summary>
    [ScriptParam(ScriptParamType.Real32, "Speed inertia")]
    public float SpeedInertia { get; set; } = 0.99f;

    /// <summary>Gets or sets the script that sends it a floor down.</summary>
    [ScriptParam(ScriptParamType.VarString, "Floor down")]
    public string FloorDownScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that sends it a floor up.</summary>
    [ScriptParam(ScriptParamType.VarString, "Floor up")]
    public string FloorUpScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that lets it move.</summary>
    [ScriptParam(ScriptParamType.VarString, "Can start")]
    public string CanStartScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound it makes setting off.</summary>
    [ScriptParam(ScriptParamType.String32, "Start sound")]
    public string StartSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound it makes arriving.</summary>
    [ScriptParam(ScriptParamType.String32, "Stop sound")]
    public string StopSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound it makes while it moves, once its start sound is out.</summary>
    [ScriptParam(ScriptParamType.String32, "Move sound")]
    public string MoveSound { get; set; } = string.Empty;

    /// <summary>Gets or sets where it stands before it has a floor, <c>tStartPos</c>.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Start Position")]
    public Vector3d StartPosition { get; set; }

    /// <summary>Gets or sets which way it faces before it has a floor, as level data's three angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Start Orientation")]
    public Vector3f StartOrientation { get; set; }

    /// <summary>Gets the floor it is at or going to, <c>ptActiveFloor</c>.</summary>
    public Igi2FloorTask? ActiveFloor => _activeFloor;

    /// <summary>Gets how far along the way to its floor it is, 0 to 1, <c>vPos</c>.</summary>
    public float Along => _along;

    /// <summary>Gets how far along the way it moves this tick, <c>vSpeed</c>.</summary>
    public float Speed => _speed;

    /// <summary>Gets the floor it last stood at, counting from its first, <c>nCurrentFloor</c>.</summary>
    public int CurrentFloor => _currentFloor;

    /// <summary>Gets the ticks it has stood idle, <c>nInactive</c>.</summary>
    public int Inactive => _inactive;

    /// <summary>Gets a value indicating whether it moved this tick, <c>isMoving</c>.</summary>
    public bool IsMoving => _isMoving != 0;

    /// <summary>Gets how far it moved this tick, <c>tDeltaPosition</c>.</summary>
    public Vector3f Delta => _delta;

    /// <inheritdoc/>
    public Vector3d CurrentPosition => _position;

    /// <inheritdoc/>
    public IReadOnlyList<float> CurrentOrientation => _orientation;

    /// <inheritdoc/>
    public Vector3d LastPosition => _lastPosition;

    /// <inheritdoc/>
    public IReadOnlyList<float> LastOrientation => _lastOrientation;

    /// <inheritdoc/>
    /// <remarks>Its start position and orientation, as the world scene places a lift.</remarks>
    public Igi2Pose Built => _built ??= Igi2Pose.FromAngles(StartPosition, StartOrientation);

    /// <inheritdoc/>
    public Vector3d AttachPosition => _position;

    /// <inheritdoc/>
    public IReadOnlyList<float> AttachOrientation => _orientation;

    /// <summary>Gets or sets the level's models, which its solids' attachments are read from.</summary>
    [LevelService]
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets the magic object types its attachments were built through, which it moves them through.</summary>
    [LevelService]
    public World.Igi2MagicObjTypes? Magic { get; set; }

    /// <summary>Gets or sets the level's sounds it sounds through, or <see langword="null"/> for none.</summary>
    [LevelService]
    public Igi2Sounds? Sounds { get; set; }

    /// <summary>Takes the solids the collision world built of it, which it carries from now on.</summary>
    /// <param name="solids">Its solids, as <see cref="Igi2CollisionWorld.SolidsOf"/> gives them.</param>
    public void Bind(IReadOnlyList<Igi2RigidObject> solids)
    {
        ArgumentNullException.ThrowIfNull(solids);
        _solids = solids;
        Repose();
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (_runTick == level.Ticks)
        {
            return;
        }

        _runTick = level.Ticks;
        Start(level);
        level.RunChildren(this);

        // Each script's value is taken whole, chopped to an integer, and its low word tested.
        int canStart = 1;
        if (!_canStart!.IsEmpty)
        {
            canStart = (int)LevelExpression.Truncate(level.Evaluate(_canStart));
        }

        int up = _floorUp!.IsEmpty ? 0 : (int)LevelExpression.Truncate(level.Evaluate(_floorUp));
        int down = _floorDown!.IsEmpty ? 0 : (int)LevelExpression.Truncate(level.Evaluate(_floorDown));
        if (_isMoving == 0 && _activeFloor is { } active)
        {
            Igi2FloorTask? next = null;
            bool none = false;
            if (up != 0 && (next = Sibling(active, 1)) is not null)
            {
                _floorCache++;
            }

            if (down != 0)
            {
                next = Sibling(active, -1);
                if (next is null)
                {
                    none = true;
                }
                else
                {
                    _floorCache--;
                }
            }

            if (!none && next is not null)
            {
                _startPosition = active.Position;
                _startOrientation = [.. active.Frame];
                _activeFloor = next;
                _stopPosition = next.Position;
                _stopOrientation = [.. next.Frame];
                _along = 0f;
            }
        }

        _lastPosition = _position;
        _lastOrientation = [.. _orientation];
        _isMovingLastFrame = _isMoving;
        if (canStart != 0)
        {
            _isMoving = Move() ? (byte)1 : (byte)0;
            if (_isMoving == 0)
            {
                _currentFloor = _floorCache;
            }
        }
        else
        {
            _isMoving = 0;
        }

        _delta = new Vector3f(
            ((Fpu)_position.X - _lastPosition.X).F,
            ((Fpu)_position.Y - _lastPosition.Y).F,
            ((Fpu)_position.Z - _lastPosition.Z).F);
        bool wasMoving = _isMovingLastFrame != 0, moving = _isMoving != 0;
        _isStop = wasMoving && !moving ? (byte)1 : (byte)0;
        _isStart = !wasMoving && moving ? (byte)1 : (byte)0;
        Sound(moving);

        if (_along == 0f && !moving && canStart == _isLastCanStart)
        {
            _inactive++;
        }
        else
        {
            _inactive = 0;
        }

        _isLastCanStart = (byte)canStart;
    }

    /// <summary>
    /// Reads its floors again, <c>Lift_UpdateInternalDataHandler</c> (<c>igi2 0x46E8B0</c>): still, and stood at its first floor.
    /// </summary>
    /// <remarks>Each of its floors calls this as it is made, after the lift's own data is read.</remarks>
    public void UpdateInternalData()
    {
        _inactive = 0;
        _speed = 0f;
        SetPositionToFirstFloor();
    }

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Model" => Model,
            "Start floor task ID" => FirstFloorId,
            "Max speed" => MaxSpeed,
            "Speed inertia" => SpeedInertia,
            "Floor down" => FloorDownScript,
            "Floor up" => FloorUpScript,
            "Can start" => CanStartScript,
            "Start sound" => StartSound,
            "Stop sound" => StopSound,
            "Move sound" => MoveSound,
            "Start Position" => StartPosition,
            "Start Orientation" => StartOrientation,
            _ => null,
        };
        return value is not null;
    }

    /// <inheritdoc/>
    /// <remarks><c>Lift_RegisterScriptSymbolsHandler</c> (<c>igi2 0x46E980</c>): two integers and three bytes, all writable.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "nInactive", () => _inactive, value => _inactive = value);
        variables.BindInt(this, "nCurrentFloor", () => _currentFloor, value => _currentFloor = value);
        variables.BindInt(this, "isMoving", () => _isMoving, value => _isMoving = (byte)value, sizeof(byte));
        variables.BindInt(this, "isStart", () => _isStart, value => _isStart = (byte)value, sizeof(byte));
        variables.BindInt(this, "isStop", () => _isStop, value => _isStop = (byte)value, sizeof(byte));
    }

    /// <inheritdoc/>
    /// <remarks>Its start pose is its parameters' until a floor is made; then the floors put it at the first.</remarks>
    protected override void OnParsed()
    {
        _startPosition = StartPosition;
        _startOrientation = Igi2Pose.Rotation(StartOrientation.X, StartOrientation.Y, StartOrientation.Z);
        _built = Igi2Pose.FromAngles(StartPosition, StartOrientation);
        UpdateInternalData();
    }

    // Lift_SetPositionToFirstFloor (igi2 0x46DD50): the floor its start id names, counted among its floors, or its first; it
    // stands there at the end of a way that begins and ends there. Without floors it stands at its start.
    private void SetPositionToFirstFloor()
    {
        _currentFloor = 0;
        _activeFloor = null;
        Igi2FloorTask? first;
        if (FirstFloorId <= 0)
        {
            first = Floors().FirstOrDefault();
        }
        else
        {
            // Retail looks the id up among every task the level has made so far; on mission 1 it is always the lift's own.
            first = FirstFloorId < 4096 ? Floors().FirstOrDefault(f => f.Id == FirstFloorId) : null;
            for (Igi2FloorTask? before = first is null ? null : Sibling(first, -1); before is not null; before = Sibling(before, -1))
            {
                _currentFloor++;
            }
        }

        _activeFloor = first;
        _floorCache = _currentFloor;
        if (first is not null)
        {
            _startPosition = first.Position;
            _startOrientation = [.. first.Frame];
            _stopPosition = first.Position;
            _stopOrientation = [.. first.Frame];
            _along = 1f;
        }

        UpdatePosition();
        _lastPosition = _position;
        _lastOrientation = [.. _orientation];
        _isStart = 0;
        _isStop = 0;
        _isLastCanStart = 1;
        UpdateAttachments();
    }

    // Lift_Move (igi2 0x46DBA0): the top speed as a share of the way a tick, eased toward by the inertia; within the last tenth
    // of the way the speed is what is left of it times ten tops, no less than a thousandth. Along is never past one.
    private bool Move()
    {
        float old = _along;
        float dx = ((Fpu)_stopPosition.X - _startPosition.X).F;
        float dy = ((Fpu)_stopPosition.Y - _startPosition.Y).F;
        Fpu dz = (Fpu)_stopPosition.Z - _startPosition.Z;
        float left = ((Fpu)1f - _along).F;
        Fpu top = ((Fpu)MaxSpeed * 4096f) * PerTick;
        Fpu step = top / Fpu.Sqrt((((Fpu)dx * dx) + ((Fpu)dy * dy)) + (dz * dz));
        float share = left < 0f ? -left : left;
        if (share < SlowingShare)
        {
            Fpu slowing = (step * share) * 10f;
            _speed = slowing.Value > LeastSpeed ? slowing.F : LeastSpeed;
        }
        else if (step.Value > _speed)
        {
            Fpu faster = (Fpu)SpeedInertia + _speed;
            _speed = faster.Value <= step.Value ? faster.F : step.F;
        }
        else if (step.Value < _speed)
        {
            Fpu slower = (Fpu)_speed - SpeedInertia;
            _speed = slower.Value >= step.Value ? slower.F : step.F;
        }

        Fpu along = (Fpu)_speed + _along;
        _along = along.Value < 1.0 ? along.F : 1f;
        UpdatePosition();
        UpdateAttachments();
        return _along != old;
    }

    // Lift_UpdatePosition (igi2 0x46D640): straight between the floors' positions, and between their frames along the arc.
    private void UpdatePosition()
    {
        if (_activeFloor is not null)
        {
            Vector3d a = _startPosition, b = _stopPosition;
            _position = new Vector3d(
                ((((Fpu)b.X - a.X) * _along) + a.X).Value,
                ((((Fpu)b.Y - a.Y) * _along) + a.Y).Value,
                ((((Fpu)b.Z - a.Z) * _along) + a.Z).Value);

            // Quat32_SLERP (igi2 0x5635D0), ported in double precision in the clip sampler.
            Quaternion32 turned = Igi2ClipSampler.Slerp(Igi2Pose.ToQuaternion(_startOrientation), Igi2Pose.ToQuaternion(_stopOrientation), _along);
            _orientation = Igi2Pose.FromQuaternion(turned);
        }
        else
        {
            _position = _startPosition;
            _orientation = [.. _startOrientation];
        }

        Repose();
    }

    // RigidDynCubeObj_UpdateAttachments (igi2 0x58E4D0): every child that holds itself against the lift follows it.
    private void UpdateAttachments()
    {
        foreach (GameTask child in Children)
        {
            (child as IIgi2Attached)?.UpdateAttachment();
        }
    }

    private void Repose()
    {
        if (_solids.Count > 0 && Models is { } models)
        {
            float[] m = _orientation;
            Igi2CollisionWorld.Repose(_solids, models, Model, Igi2Pose.FromMatrix(_position, new Matrix3f(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8])), Magic);
        }
    }

    // Setting off it sounds its start; moving, once that is out, its loop; arriving, its stop. Each follows the lift.
    private void Sound(bool moving)
    {
        string? name = null;
        if (_isStart != 0)
        {
            name = StartSound;
        }
        else if (moving)
        {
            if (_moveSound is not { IsAlive: true })
            {
                name = MoveSound;
            }
        }
        else if (_isStop != 0)
        {
            name = StopSound;
        }

        if (name is null)
        {
            return;
        }

        _moveSound?.Delete();
        _moveSound = Sounds?.New(name, _position, () => _position);
    }

    private IEnumerable<Igi2FloorTask> Floors() => Children.OfType<Igi2FloorTask>();

    // QTask_GetNextSiblingOfType / QTask_GetPrevSiblingOfType (igi2 0x40AE00, 0x40AE30): the nearest floor after or before.
    private Igi2FloorTask? Sibling(Igi2FloorTask floor, int direction)
    {
        IReadOnlyList<GameTask> children = [.. Children];
        int at = -1;
        for (int i = 0; i < children.Count; i++)
        {
            if (ReferenceEquals(children[i], floor))
            {
                at = i;
                break;
            }
        }

        for (int i = at + direction; at >= 0 && i >= 0 && i < children.Count; i += direction)
        {
            if (children[i] is Igi2FloorTask found)
            {
                return found;
            }
        }

        return null;
    }

    private void Start(Igi2LevelRun level)
    {
        if (_started)
        {
            return;
        }

        _floorUp = Igi2LevelExpressions.Create(FloorUpScript, Name, level.Natives);
        _floorDown = Igi2LevelExpressions.Create(FloorDownScript, Name, level.Natives);
        _canStart = Igi2LevelExpressions.Create(CanStartScript, Name, level.Natives);
        _started = true;
    }
}
