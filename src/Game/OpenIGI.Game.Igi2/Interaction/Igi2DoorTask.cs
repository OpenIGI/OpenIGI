using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Expressions;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// A door or gate that swings or slides between shut and open, I.G.I. 2's <c>Door</c>, exporting <c>isLocked</c>, <c>isOpen</c>,
/// <c>isClosed</c>, <c>isLastClosed</c>, <c>isLastOpen</c>, <c>isPicked</c> and <c>nDoorOpenTicks</c>.
/// </summary>
/// <remarks>
/// <para>
/// Its state is one number, how far open it is (<c>vRealPos</c>, 0 to 1), which moves toward where it is told to be by one
/// thirtieth of its <c>Open time</c> a tick (<c>Door_RunHandler</c>, <c>igi2 0x5377B0</c>). Shut, its open script may tell it to
/// open; open, its close script may tell it to close; and its locked script says, each tick, whether it is locked. Where it
/// stands follows from that number (<c>Door_SetPosition</c>, <c>0x5362B0</c>): slid along its stop offsets from its start, and
/// turned about its Z by that fraction of its <c>Max angle</c>.
/// </para>
/// <para>
/// A human's view finds it (<c>Door_EvaluateActivateHandler</c>, <c>0x537600</c>) from either side while it is unlocked or can be
/// picked; activated, it opens or closes (<c>Door_ToggleOpenDoorEx</c>, <c>0x5384D0</c>). A door that stops on collision holds
/// where it is for fifteen ticks when a human's lines strike it from the side it is moving toward
/// (<c>Door_TraceLinesHandler</c>, <c>0x536FE0</c>).
/// </para>
/// <para>It is still placed by the world scene, which reads its parameters by name (<see cref="IIgi2PlacedTask"/>).</para>
/// </remarks>
public sealed partial class Igi2DoorTask : Igi2Task, IIgi2Door, IIgi2PlacedTask, IIgi2Mover, IIgi2Attached, IAiDoor
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Door";

    /// <summary>The ticks a door that stops on collision holds where it is once struck.</summary>
    public const int HoldTicks = 15;

    /// <summary>The ticks in a second, which its open time is counted in.</summary>
    public const float TicksPerSecond = 30f;

    /// <summary>How far open a door that is open has to be to count as open to a picker and to the portals, <c>0x3F19999A</c>.</summary>
    public const float OpenEnough = 0.6f;

    // Door_EvaluateActivateHandler's box in the door's own frame: X from one side to the other, Z up and down.
    private const double FindLeft = -4096.0;
    private const double FindRight = 1843.2;
    private const double FindDown = -4096.0;
    private const double FindUp = 4096.0;

    private float[] _base = Igi2Pose.Rotation(0f, 0f, 0f);
    private float[] _orientation = Igi2Pose.Rotation(0f, 0f, 0f);
    private float[] _lastOrientation = Igi2Pose.Rotation(0f, 0f, 0f);
    private Vector3d _start;
    private Vector3d _position;
    private Vector3d _lastPosition;
    private float _authoredMaxAngle;
    private float _idealPos;
    private float _distance;
    private int _openTicks;
    private int _holdTimer;
    private byte _isLocked;
    private byte _isOpen;
    private byte _isClosed = 1;
    private byte _isLastOpen;
    private byte _isLastClosed;
    private byte _isPicked;
    private byte _isOpenDoorChanged;
    private byte _isOpenDoor;
    private LevelExpression? _open;
    private LevelExpression? _close;
    private LevelExpression? _locked;
    private Func<bool>? _opens;
    private Func<bool>? _closes;
    private Func<double>? _lockedValue;
    private Igi2Sound? _moveSound;
    private IReadOnlyList<Igi2RigidObject> _solids = [];
    private bool _started;
    private Igi2Pose? _built;
    private IAttachParent? _attachParent;
    private readonly float[] _relativeOrientation = new float[9];
    private Vector3f _relativePosition;

    /// <summary>Gets or sets where it stands shut, <c>Position start</c>.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position start")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets how far it slides along its own X when fully open, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Position stop X")]
    public float StopX { get; set; }

    /// <summary>Gets or sets how far it slides along its own Y when fully open, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Position stop Y")]
    public float StopY { get; set; }

    /// <summary>Gets or sets how far open it is, 0 shut to 1 open, <c>vRealPos</c>: the level's starting value, then its state.</summary>
    [ScriptParam(ScriptParamType.Real32, "Position slider")]
    public float Slider { get; set; }

    /// <summary>Gets or sets which way it faces shut, as level data's three angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the turn it makes fully open, in degrees: the level's value, then signed by the side a door that opens
    /// both ways was last opened from (<c>Door_s + 0x1B4</c>).
    /// </summary>
    [ScriptParam(ScriptParamType.Real32, "Max angle")]
    public float MaxAngle { get; set; } = 90f;

    /// <summary>Gets or sets the seconds it takes to open or close fully.</summary>
    [ScriptParam(ScriptParamType.Real32, "Open time")]
    public float OpenTime { get; set; } = 0.5f;

    /// <summary>Gets or sets a value indicating whether its lock can be picked.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Pickable")]
    public bool Pickable { get; set; }

    /// <summary>Gets or sets a value indicating whether it swings away from whoever opens it, either way.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Open Both Ways")]
    public bool OpenBothWays { get; set; }

    /// <summary>Gets or sets the seconds picking its lock takes, <c>vActivateTime</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Pick lock time (s)")]
    public float PickLockTime { get; set; }

    /// <summary>Gets or sets the script that says whether it is locked.</summary>
    [ScriptParam(ScriptParamType.VarString, "Locked expression")]
    public string LockedScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that opens it while it is shut.</summary>
    [ScriptParam(ScriptParamType.VarString, "Open door expression")]
    public string OpenScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the script that closes it while it is open.</summary>
    [ScriptParam(ScriptParamType.VarString, "Close door expression")]
    public string CloseScript { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether a human in its way holds it.</summary>
    [ScriptParam(ScriptParamType.Bool8, "StopOnCollision")]
    public bool StopOnCollision { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether it opens and shuts the sector portal it stands in.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Link up to a portal")]
    public bool LinkUpToPortal { get; set; } = true;

    /// <summary>Gets or sets how far toward the portal's centre it is drawn while shut and seen from the portal's side.</summary>
    [ScriptParam(ScriptParamType.Real32, "Delta scale factor")]
    public float DeltaScaleFactor { get; set; } = 1f;

    /// <summary>Gets or sets the sound it makes the tick it is fully open.</summary>
    [ScriptParam(ScriptParamType.String16, "Open sound")]
    public string OpenSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound it makes the tick it is fully shut.</summary>
    [ScriptParam(ScriptParamType.String16, "Close sound")]
    public string CloseSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound it makes while it moves.</summary>
    [ScriptParam(ScriptParamType.String16, "Move sound")]
    public string MoveSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound it makes when told to open.</summary>
    [ScriptParam(ScriptParamType.String16, "Begin open sound")]
    public string BeginOpenSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound it makes when told to close.</summary>
    [ScriptParam(ScriptParamType.String16, "Begin close sound")]
    public string BeginCloseSound { get; set; } = string.Empty;

    /// <summary>Gets or sets the sound picking its lock makes.</summary>
    [ScriptParam(ScriptParamType.String32, "Activate Sound")]
    public string ActivateSound { get; set; } = "tba_picklock";

    /// <summary>Gets a value indicating whether it is locked, <c>isLocked</c>.</summary>
    public bool IsLocked => _isLocked != 0;

    /// <summary>Gets a value indicating whether it stands fully open, <c>isOpen</c>.</summary>
    public bool IsOpen => _isOpen != 0;

    /// <summary>Gets a value indicating whether it stands fully shut, <c>isClosed</c>.</summary>
    public bool IsClosed => _isClosed != 0;

    /// <summary>Gets a value indicating whether it is told to open, <c>isOpenDoor</c>.</summary>
    public bool IsOpenDoor => _isOpenDoor != 0;

    /// <summary>Gets or sets a value indicating whether its lock has been picked, <c>isPicked</c>, which unlocks it for good.</summary>
    public bool IsPicked
    {
        get => _isPicked != 0;
        set => _isPicked = value ? (byte)1 : (byte)0;
    }

    /// <summary>Gets the ticks it has stood open, <c>nOpenTicks</c>, exported as <c>nDoorOpenTicks</c>.</summary>
    public int OpenTicks => _openTicks;

    /// <summary>Gets the ticks it still holds where it is, <c>nHoldDoorTimer</c>.</summary>
    public int HoldTimer => _holdTimer;

    /// <summary>Gets how far its stop offsets reach, <c>vDistance</c>.</summary>
    public float Distance => _distance;

    /// <summary>Gets where it stands now: the position its object was last moved to.</summary>
    public Vector3d CurrentPosition => _position;

    /// <summary>Gets which way it faces now, row by row.</summary>
    public IReadOnlyList<float> CurrentOrientation => _orientation;

    /// <inheritdoc/>
    /// <remarks><c>GameFunctions_GetMagicVertex</c> (<c>igi2 0x48CCD0</c>) at vertex 0, from its pose as it stands.</remarks>
    public Vector3d MagicVertex => Igi2MagicVertex.Position(Models?.Invoke(Model), 0, _orientation, _position);

    /// <summary>Gets where it stood at the start of the tick, <c>tLastPosition</c>.</summary>
    public Vector3d LastPosition => _lastPosition;

    /// <inheritdoc/>
    /// <remarks>Its start and its level's three angles, as the world scene places a door.</remarks>
    public Igi2Pose Built => _built ??= Igi2Pose.FromAngles(Position, Orientation);

    /// <summary>Gets which way it faced at the start of the tick, <c>tLastOrientation</c>.</summary>
    public IReadOnlyList<float> LastOrientation => _lastOrientation;

    /// <summary>
    /// Gets its state as <c>Door_GetDoorStatus</c> (<c>igi2 0x538460</c>) reports it: 0 open (past <see cref="OpenEnough"/>), 2
    /// told to open, 1 shut, 3 closing.
    /// </summary>
    public int Status => _isOpen != 0 && Slider > OpenEnough ? 0 : _isOpenDoor != 0 ? 2 : _isClosed != 0 ? 1 : 3;

    /// <inheritdoc/>
    public bool IsPickable => !(_isOpen != 0 && Slider > OpenEnough) && _isOpenDoor == 0 && _isClosed != 0 && Pickable;

    /// <summary>Gets or sets the level's models, which its magic vertex, its radius and its attachments are read from.</summary>
    [LevelService]
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets the magic object types its attachments were built through, which it moves them through.</summary>
    [LevelService]
    public World.Igi2MagicObjTypes? Magic { get; set; }

    /// <summary>Gets or sets the level's sounds it sounds through, or <see langword="null"/> for none.</summary>
    [LevelService]
    public Igi2Sounds? Sounds { get; set; }

    /// <inheritdoc/>
    public Vector3d Centre => _position;

    /// <inheritdoc/>
    /// <remarks>Its model's radius, as <c>Door_CreateHandler</c> (<c>igi2 0x5371C0</c>) registers it with its largest scale of one.</remarks>
    public float Radius => Models?.Invoke(Model)?.NativeMarkerBound ?? 1f;

    /// <inheritdoc/>
    /// <remarks><c>Door_GetActivateHandler</c> (<c>igi2 0x537790</c>).</remarks>
    public Igi2ActivateKind Kind => Igi2ActivateKind.Door;

    /// <summary>Takes the solids the collision world built of it, which it moves from now on; its own solid answers its lines.</summary>
    /// <param name="solids">Its solids, as <see cref="Igi2CollisionWorld.SolidsOf"/> gives them.</param>
    public void Bind(IReadOnlyList<Igi2RigidObject> solids)
    {
        ArgumentNullException.ThrowIfNull(solids);
        _solids = solids;
        if (solids.Count > 0 && solids[0].Owner is Igi2CollisionOwner { Depth: 0 })
        {
            solids[0].Traced = trace => TraceLines(solids[0], trace);
        }

        Repose();
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        Start(level);
        level.RunChildren(this);

        bool wasClosed = _isClosed != 0;
        _isLastOpen = _isOpen;
        _lastPosition = _position;
        _lastOrientation = [.. _orientation];
        _isLastClosed = _isClosed;

        bool openDoor = _isOpenDoor != 0;
        if (DoorExpressions.Command(wasClosed, _isOpen != 0, ref openDoor, ref _openTicks, _opens, _closes))
        {
            _isOpenDoorChanged = 1;
        }

        _isOpenDoor = openDoor ? (byte)1 : (byte)0;
        DoorExpressions.Lock(ref _isLocked, _isPicked != 0, _lockedValue);

        if (_holdTimer != 0)
        {
            _idealPos = Slider;
            _holdTimer--;
        }
        else
        {
            _idealPos = _isOpenDoor != 0 ? 1f : 0f;
        }

        bool moved = Move();
        if (_idealPos == Slider)
        {
            bool closed = _idealPos == 0f;
            _isClosed = closed ? (byte)1 : (byte)0;
            _isOpen = closed ? (byte)0 : (byte)1;
        }
        else
        {
            _isClosed = 0;
            _isOpen = 0;
        }

        // A door with no model to move goes quiet while it moves.
        if (moved && Models?.Invoke(Model) is null)
        {
            return;
        }

        Sound();
        RunPortal();
    }

    /// <summary>
    /// A soldier opens or shuts the door, <c>HumanSoldier_HandleActivation</c> (<c>igi2 0x4E3340</c>): the door's open flag flipped
    /// and marked changed, nothing more.
    /// </summary>
    public void ToggleOpenBySoldier()
    {
        _isOpenDoor ^= 1;
        _isOpenDoorChanged = 1;
    }

    /// <inheritdoc/>
    /// <remarks>Where it is now, which <c>Door_AIFindActivateHandler</c> answers with.</remarks>
    Vector3d IAiDoor.DoorPosition => CurrentPosition;

    /// <inheritdoc/>
    int IAiDoor.UseState => Status;

    /// <inheritdoc/>
    void IAiDoor.OpenForSoldier() => ToggleOpenBySoldier();

    /// <summary>Holds the door where it is for some ticks, as a soldier on its way does (<c>Door+0x3A0</c>).</summary>
    /// <param name="ticks">The ticks.</param>
    public void HoldOpen(int ticks) => _holdTimer = ticks;

    /// <inheritdoc/>
    public void ToggleOpen(Vector3d from)
    {
        _isOpenDoor ^= 1;
        _isOpenDoorChanged = 1;
        if (_isOpenDoor == 0)
        {
            return;
        }

        if (!OpenBothWays)
        {
            MaxAngle = _authoredMaxAngle;
            return;
        }

        // Which side of its starting plane the opener stands on: the door swings away from it, whichever way it was authored.
        float[] m = _base;
        Fpu dx = (Fpu)from.X - _start.X;
        Fpu dy = (Fpu)from.Y - _start.Y;
        Fpu dz = (Fpu)from.Z - _start.Z;
        Fpu side = (((Fpu)m[7] * dz) + ((Fpu)m[4] * dy)) + ((Fpu)m[1] * dx);
        float magnitude = _authoredMaxAngle < 0f ? -_authoredMaxAngle : _authoredMaxAngle;
        MaxAngle = side.Value < 0.0 ? magnitude : -magnitude;
    }

    /// <inheritdoc/>
    public void EvaluateActivate(Igi2EvaluateActivate context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_isLocked != 0 && !IsPickable)
        {
            return;
        }

        float[] m = _orientation;
        Vector3d vertex = MagicVertex;
        Fpu dx = (Fpu)context.ViewPosition.X - vertex.X;
        Fpu dy = (Fpu)context.ViewPosition.Y - vertex.Y;
        Fpu dz = (Fpu)context.ViewPosition.Z - vertex.Z;
        Fpu across = (((Fpu)m[0] * dx) + ((Fpu)m[6] * dz)) + ((Fpu)m[3] * dy);
        Fpu up = (((Fpu)m[8] * dz) + ((Fpu)m[2] * dx)) + ((Fpu)m[5] * dy);
        if (!(across.Value > FindLeft) || !(across.Value < FindRight) || !(up.Value > FindDown) || !(up.Value < FindUp))
        {
            return;
        }

        context.Resolve(vertex, this);

        // And the view's own spot on the door's face, in line with it: the door is as near as its nearest point.
        Fpu x = (((Fpu)m[2] * up) + ((Fpu)m[0] * across)) + ((Fpu)m[1] * 0.0);
        Fpu y = (((Fpu)m[5] * up) + ((Fpu)m[3] * across)) + ((Fpu)m[4] * 0.0);
        Fpu z = (((Fpu)m[8] * up) + ((Fpu)m[6] * across)) + ((Fpu)m[7] * 0.0);
        context.Resolve(new Vector3d((x + vertex.X).Value, (y + vertex.Y).Value, (z + vertex.Z).Value), this);
    }

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Position start" => Position,
            "Position stop X" => StopX,
            "Position stop Y" => StopY,
            "Position slider" => Slider,
            "Orientation" => Orientation,
            "Model" => Model,
            "Max angle" => MaxAngle,
            "Open time" => OpenTime,
            "Pickable" => Pickable,
            "Open Both Ways" => OpenBothWays,
            "Pick lock time (s)" => PickLockTime,
            "Locked expression" => LockedScript,
            "Open door expression" => OpenScript,
            "Close door expression" => CloseScript,
            "StopOnCollision" => StopOnCollision,
            "Link up to a portal" => LinkUpToPortal,
            "Delta scale factor" => DeltaScaleFactor,
            "Open sound" => OpenSound,
            "Close sound" => CloseSound,
            "Move sound" => MoveSound,
            "Begin open sound" => BeginOpenSound,
            "Begin close sound" => BeginCloseSound,
            "Activate Sound" => ActivateSound,
            _ => null,
        };
        return value is not null;
    }

    /// <inheritdoc/>
    /// <remarks><c>Door_RegisterScriptSymbolsHandler</c> (<c>igi2 0x537FF0</c>): six bytes and an integer, all writable.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isLocked", () => _isLocked, value => _isLocked = (byte)value, sizeof(byte));
        variables.BindInt(this, "isOpen", () => _isOpen, value => _isOpen = (byte)value, sizeof(byte));
        variables.BindInt(this, "isClosed", () => _isClosed, value => _isClosed = (byte)value, sizeof(byte));
        variables.BindInt(this, "isLastClosed", () => _isLastClosed, value => _isLastClosed = (byte)value, sizeof(byte));
        variables.BindInt(this, "isLastOpen", () => _isLastOpen, value => _isLastOpen = (byte)value, sizeof(byte));
        variables.BindInt(this, "isPicked", () => _isPicked, value => _isPicked = (byte)value, sizeof(byte));
        variables.BindInt(this, "nDoorOpenTicks", () => _openTicks, value => _openTicks = value);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Door_UpdateInternalDataHandler</c> (<c>igi2 0x537E10</c>): it starts shut and told to stay so, at its start, with its
    /// level's angle kept to sign from later. Its object is moved where its slider puts it.
    /// </remarks>
    protected override void OnParsed()
    {
        _start = Position;
        _base = Igi2Pose.Rotation(Orientation.X, Orientation.Y, Orientation.Z);
        _isClosed = 1;
        _isLastClosed = 1;
        _isOpen = 0;
        _isOpenDoor = 0;
        _isLastOpen = 0;
        Fpu x = StopX, y = StopY;
        Fpu reach = Fpu.Sqrt((x * x) + (y * y));
        _distance = (reach.Value < 0.0 ? -reach : reach).F;
        _built = Igi2Pose.FromAngles(Position, Orientation);
        AttachToParent();
        SetPosition();

        // The pose the tick starts from, which only drawing reads between ticks: retail copies its object's pose before
        // this first move, and every tick copies it again before anything moves.
        _lastPosition = _position;
        _lastOrientation = [.. _orientation];
        _authoredMaxAngle = MaxAngle;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Door_UpdateAttachmentHandler</c> (<c>igi2 0x5382C0</c>) over <c>ModelObj_UpdateAttachment</c> (<c>0x56CB40</c>, its
    /// unboned path): its start and its frame are its parent's turned by what it kept at attaching, and it is set again where
    /// its slider puts it from there.
    /// </remarks>
    public void UpdateAttachment()
    {
        if (_attachParent is not { } parent)
        {
            return;
        }

        IReadOnlyList<float> p = parent.AttachOrientation;
        float[] a = _relativeOrientation;
        Vector3f r = _relativePosition;
        float tx = ((((Fpu)p[1] * r.Y) + ((Fpu)r.X * p[0])) + ((Fpu)p[2] * r.Z)).F;
        float ty = ((((Fpu)r.X * p[3]) + ((Fpu)r.Z * p[5])) + ((Fpu)p[4] * r.Y)).F;
        float tz = ((((Fpu)p[7] * r.Y) + ((Fpu)p[6] * r.X)) + ((Fpu)p[8] * r.Z)).F;
        _base =
        [
            ((((Fpu)p[2] * a[6]) + ((Fpu)p[1] * a[3])) + ((Fpu)a[0] * p[0])).F,
            ((((Fpu)p[0] * a[1]) + ((Fpu)p[2] * a[7])) + ((Fpu)p[1] * a[4])).F,
            ((((Fpu)p[2] * a[8]) + ((Fpu)p[1] * a[5])) + ((Fpu)p[0] * a[2])).F,
            ((((Fpu)a[6] * p[5]) + ((Fpu)a[3] * p[4])) + ((Fpu)a[0] * p[3])).F,
            ((((Fpu)a[7] * p[5]) + ((Fpu)a[4] * p[4])) + ((Fpu)a[1] * p[3])).F,
            ((((Fpu)p[3] * a[2]) + ((Fpu)p[5] * a[8])) + ((Fpu)p[4] * a[5])).F,
            ((((Fpu)p[8] * a[6]) + ((Fpu)p[7] * a[3])) + ((Fpu)p[6] * a[0])).F,
            ((((Fpu)p[6] * a[1]) + ((Fpu)p[8] * a[7])) + ((Fpu)p[7] * a[4])).F,
            ((((Fpu)p[8] * a[8]) + ((Fpu)p[7] * a[5])) + ((Fpu)p[6] * a[2])).F,
        ];
        Vector3d at = parent.AttachPosition;
        _start = new Vector3d(((Fpu)tx + at.X).Value, ((Fpu)ty + at.Y).Value, ((Fpu)tz + at.Z).Value);
        Position = _start;
        SetPosition();
    }

    // Door_AttachToParent (igi2 0x536520): a door inside a parent that holds its children keeps its frame and its start against
    // the parent's, the frame as the parent's transpose times its own, the start as the offset turned back into the parent.
    private void AttachToParent()
    {
        _attachParent = Parent as IAttachParent;
        if (_attachParent is not { } parent)
        {
            return;
        }

        IReadOnlyList<float> p = parent.AttachOrientation;
        float[] d = _base;
        for (int i = 0; i < 3; i++)
        {
            for (int c = 0; c < 3; c++)
            {
                _relativeOrientation[(3 * i) + c] = ((((Fpu)p[i] * d[c]) + ((Fpu)p[6 + i] * d[6 + c])) + ((Fpu)p[3 + i] * d[3 + c])).F;
            }
        }

        Vector3d at = parent.AttachPosition;
        float r0 = ((Fpu)_start.X - at.X).F, r1 = ((Fpu)_start.Y - at.Y).F, r2 = ((Fpu)_start.Z - at.Z).F;
        _relativePosition = new Vector3f(
            ((((Fpu)p[0] * r0) + ((Fpu)p[3] * r1)) + ((Fpu)p[6] * r2)).F,
            ((((Fpu)p[1] * r0) + ((Fpu)p[4] * r1)) + ((Fpu)p[7] * r2)).F,
            ((((Fpu)p[2] * r0) + ((Fpu)p[5] * r1)) + ((Fpu)p[8] * r2)).F);
    }

    // Toward where it is told to be, by a thirtieth of its open time, not past it.
    private bool Move()
    {
        float real = Slider;
        if (real == _idealPos)
        {
            Slider = _idealPos;
            return false;
        }

        Fpu step = (Fpu)1f / ((Fpu)OpenTime * TicksPerSecond);
        if (real < _idealPos)
        {
            Fpu next = step + real;
            Slider = next.F;
            if (!(next.Value <= _idealPos))
            {
                Slider = _idealPos;
            }
        }
        else
        {
            Fpu next = (Fpu)real - step;
            Slider = next.F;
            if (next.Value < _idealPos)
            {
                Slider = _idealPos;
            }
        }

        SetPosition();
        return true;
    }

    // Slid along its stop offsets by the slider, turned by the slider's share of its angle, and its solids carried with it.
    private void SetPosition()
    {
        float[] m = _base;
        Fpu a = ((Fpu)StopX * Slider) * 4096f;
        Fpu b = ((Fpu)StopY * Slider) * 4096f;
        Fpu x = (((Fpu)m[1] * b) + ((Fpu)m[0] * a)) + ((Fpu)m[2] * 0.0);
        Fpu y = (((Fpu)m[4] * b) + ((Fpu)m[3] * a)) + ((Fpu)m[5] * 0.0);
        Fpu z = (((Fpu)m[7] * b) + ((Fpu)m[6] * a)) + ((Fpu)m[8] * 0.0);
        _position = new Vector3d((x + _start.X).Value, (y + _start.Y).Value, (z + _start.Z).Value);
        _orientation = MaxAngle > 0.001f || MaxAngle < -0.001f
            ? Igi2Pose.RotateGamma(m, (((Fpu)MaxAngle * Slider) * 0.017453292f).F)
            : [.. m];
        Repose();
    }

    private void Repose()
    {
        if (_solids.Count > 0 && Models is { } models)
        {
            Igi2CollisionWorld.Repose(_solids, models, Model, Igi2Pose.FromMatrix(_position, Frame(_orientation)), Magic);
        }
    }

    // The sounds of a door that is told to move, that arrives, and that is moving.
    private void Sound()
    {
        if (_isOpenDoorChanged != 0)
        {
            Sounds?.New(_isOpenDoor != 0 ? BeginOpenSound : BeginCloseSound, _position);
            _isOpenDoorChanged = 0;
        }

        if (_isClosed != 0 && _isLastClosed == 0)
        {
            Sounds?.New(CloseSound, _position);
        }

        if (_isOpen != 0 && _isLastOpen == 0)
        {
            Sounds?.New(OpenSound, _position);
        }

        if (_isClosed != 0 || _isOpen != 0)
        {
            _moveSound?.Delete();
            _moveSound = null;
        }
        else if (_moveSound is not { IsAlive: true } && Sounds?.New(MoveSound, _position) is { } moving)
        {
            _moveSound?.Delete();
            _moveSound = moving;
        }
    }

    // Door_TraceLinesHandler (igi2 0x536FE0) after the solid's own trace: a human's lines that strike the door from the side
    // it is moving toward hold it. A sliding door is held only closing.
    private void TraceLines(Igi2RigidObject solid, WorldLineTrace trace)
    {
        if (!StopOnCollision || (MaxAngle == 0f && _isOpenDoor != 0) || trace.Evaluator is not Igi2Human)
        {
            return;
        }

        float[] m = _orientation;
        for (int i = 0; i < trace.Count; i++)
        {
            WorldIntersection hit = trace.Intersections[i];
            if (!hit.IsHit || !ReferenceEquals(hit.HitObject, solid))
            {
                continue;
            }

            Vector3d start = trace.Starts[i];
            Fpu dx = (Fpu)start.X - _position.X;
            Fpu dy = (Fpu)start.Y - _position.Y;
            Fpu dz = (Fpu)start.Z - _position.Z;
            double side = ((((Fpu)m[7] * dz) + ((Fpu)m[4] * dy)) + ((Fpu)m[1] * dx)).Value;

            // Struck from behind the way it moves, the lines are in no way of it, and the rest are not looked at.
            bool behind = _isOpenDoor != 0
                ? (MaxAngle > 0f && side < 0.0) || (MaxAngle < 0f && side > 0.0)
                : (MaxAngle > 0f && side > 0.0) || (MaxAngle < 0f && side < 0.0);
            if (behind)
            {
                return;
            }

            _holdTimer = HoldTicks;
        }
    }

    private void Start(Igi2LevelRun level)
    {
        if (_started)
        {
            return;
        }

        _open = Igi2LevelExpressions.Create(OpenScript, Name, level.Natives);
        _close = Igi2LevelExpressions.Create(CloseScript, Name, level.Natives);
        _locked = Igi2LevelExpressions.Create(LockedScript, Name, level.Natives);
        LevelExpression open = _open, close = _close, locked = _locked;
        _opens = open.IsEmpty ? null : () => level.Evaluate(open) != 0.0;
        _closes = close.IsEmpty ? null : () => level.Evaluate(close) != 0.0;
        _lockedValue = locked.IsEmpty ? null : () => level.Evaluate(locked);
        _started = true;
    }

    private static Matrix3f Frame(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
}
