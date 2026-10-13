using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Physics;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Vehicles;

/// <summary>
/// A car, I.G.I. 2's <c>Car</c> (car.c, a <c>Vehicle</c>): a physics object on sprung wheels that its <c>CarAI</c> drives, which may carry
/// an automatic gun, and which blows up when a rocket reaches it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Car_RunHandler</c>, once a tick: its engine sound, made of its type's sound while its play sound expression holds (or always,
/// with none) and deleted when it does not; its open door expression, given to its door; its detection, while its can fire expression
/// holds, from its gun's muzzle along the gun; its children (its driver, who drives it); its gun mounted where the car now is, and
/// run; then its explosion, if one is coming.
/// </para>
/// <para>
/// It moves only when its driver drives it (<c>Car_UpdateAnimDataHandler</c>): a step at tick nought sets it back where the level put
/// it, at its original thrust; any other step takes the thrust (unless it is -1), the steering and the brake (a fourth channel other
/// than -1), and moves it a tick (<see cref="Move"/>).
/// </para>
/// <para>
/// <c>Car_Explode</c>: a car with no explosion vertices goes off once at its centre, a quarter of its radius at full damage; one with
/// them goes off at each, each a random number of ticks below thirty later. Ten or thirty ticks on, it is taken out of the world, its
/// gun and its sound with it, and its destroyed model is left where it stood.
/// </para>
/// </remarks>
public sealed partial class Igi2CarTask : Igi2Task, IIgi2PlacedTask, IIgi2Mover, IIgi2PartMover, IIgi2ExplosionTarget, IIgi2Destructible, IIgi2AnimTarget
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Car";

    /// <summary>The magic task type a car's wheels are.</summary>
    public const string WheelType = "Wheel";

    /// <summary>The groups a car's lines are traced against, <c>0x98000000</c>: the ground, spline segments and rigid objects.</summary>
    public const uint CarCollisionGroups = 0x98000000;

    /// <summary>A step's channel value that leaves a channel as it is.</summary>
    public const float HoldChannel = -1f;

    /// <summary>The model an explosion's fragments are.</summary>
    public const string FragmentModel = "1009_01_1";

    /// <summary>An explosion's sound.</summary>
    public const string ExplosionSound = "explo_03_l";

    private readonly Igi2CarWheel[] _wheels = new Igi2CarWheel[Igi2CarType.MaxWheels];
    private Igi2CarType? _type;
    private Igi2PhysicsObjectType? _physicsType;
    private Igi2PhysicsObject? _body;
    private Igi2PhysicsWorld? _physicsWorld;
    private MefModel? _model;
    private Igi2Pose? _built;
    private IReadOnlyList<Igi2RigidObject> _solids = [];
    private MefAttachment? _gunAttachment;
    private Igi2LevelExpression? _playSound;
    private Igi2LevelExpression? _openDoor;
    private Igi2LevelExpression? _canFire;
    private Igi2Sound? _engineSound;
    private Igi2AiDetection? _detection;
    private Igi2DetectionWorld? _detectionWorld;
    private Vector3d _lastPosition;
    private float[] _lastOrientation = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
    private int _movedTick = int.MinValue;
    private int _runTick = -1;
    private int _engineWheels;
    private int _attachedWheels;
    private bool _isExplode;
    private bool _isFire;
    private int _ticksToDie = -1;
    private byte _isExploded;
    private bool _isWeaponSystemsRun;

    /// <summary>Initializes a new instance of the <see cref="Igi2CarTask"/> class.</summary>
    public Igi2CarTask()
    {
        for (int i = 0; i < _wheels.Length; i++)
        {
            _wheels[i] = new Igi2CarWheel();
        }
    }

    /// <summary>Gets or sets where it stands as the level begins.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets which way it faces as the level begins, as three Euler angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets the thrust it starts with, <c>vOriginalThrust</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Original Thrust")]
    public float OriginalThrust { get; set; }

    /// <summary>Gets or sets the speed it starts with, which only its keyframes read.</summary>
    [ScriptParam(ScriptParamType.Real32x3, "Speed")]
    public Vector3f Speed { get; set; }

    /// <summary>Gets or sets its model, which names its physics type.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether its contact points meet the world, <c>isCollDetectionEnabled</c>.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Collision detection")]
    public bool CollisionDetection { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether it is held level, <c>isForceZ</c>.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Force Z")]
    public bool ForceZ { get; set; }

    /// <summary>Gets or sets the expression that opens its door.</summary>
    [ScriptParam(ScriptParamType.VarString, "Open door")]
    public string OpenDoorExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether it watches and fires.</summary>
    [ScriptParam(ScriptParamType.VarString, "Can fire")]
    public string CanFireExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says whether its engine sounds.</summary>
    [ScriptParam(ScriptParamType.VarString, "Play sound")]
    public string PlaySoundExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets its cone's half height, in degrees.</summary>
    [ScriptParam(ScriptParamType.Degrees, "ViewCone Alpha (degrees)")]
    public float ViewconeAlpha { get; set; } = 90f;

    /// <summary>Gets or sets its cone's half width, in degrees.</summary>
    [ScriptParam(ScriptParamType.Degrees, "ViewCone Gamma (degrees)")]
    public float ViewconeGamma { get; set; } = 90f;

    /// <summary>Gets or sets how far its cone reaches, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "ViewCone Length (meter)")]
    public float ViewconeLength { get; set; } = 50f;

    /// <summary>Gets or sets its gun's barrel model.</summary>
    [ScriptParam(ScriptParamType.String16, "Barrel model")]
    public string BarrelModel { get; set; } = "661_04_1";

    /// <summary>Gets or sets the sound its gun turns with.</summary>
    [ScriptParam(ScriptParamType.String16, "Rotationsound")]
    public string RotationSound { get; set; } = string.Empty;

    /// <summary>Gets or sets its gun's lowest tilt, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "Min Alpha (deg)")]
    public float MinAlpha { get; set; }

    /// <summary>Gets or sets its gun's highest tilt, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max Alpha (deg)")]
    public float MaxAlpha { get; set; } = 90f;

    /// <summary>Gets or sets the most its gun turns either way, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max Gamma (deg)")]
    public float MaxGamma { get; set; } = 360f;

    /// <summary>Gets or sets its gun's ammunition, which never runs out.</summary>
    [ScriptParam(ScriptParamType.Int32, "Ammo")]
    public int Ammo { get; set; } = 10000;

    /// <summary>Gets or sets how fast its gun tilts, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Alpha speed (deg/sec)")]
    public float AlphaSpeed { get; set; } = 90f;

    /// <summary>Gets or sets how fast its gun turns, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Gamma speed (deg/sec)")]
    public float GammaSpeed { get; set; } = 360f;

    /// <summary>Gets or sets how fast its gun's barrel spins while it fires, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Beta speed (deg/sec)")]
    public float BetaSpeed { get; set; } = 1000f;

    /// <summary>Gets or sets how likely its gun's rounds are to strike, nought to one.</summary>
    [ScriptParam(ScriptParamType.Real32, "Accuracy")]
    public float Accuracy { get; set; } = 0.3f;

    /// <summary>Gets or sets its gun's weapon, a <c>WEAPON_ID_*</c> constant's name.</summary>
    [ScriptParam(ScriptParamType.String32, "WeaponID")]
    public string WeaponId { get; set; } = "WEAPON_ID_M2HB";

    /// <summary>Gets its physics type's car part, once prepared.</summary>
    public Igi2CarType? CarType => _type;

    /// <summary>Gets its physics object, once prepared.</summary>
    public Igi2PhysicsObject? Body => _body;

    /// <summary>Gets its wheels' states, <c>atWheelState</c>, one for each of its type's wheels.</summary>
    public IReadOnlyList<Igi2CarWheel> Wheels => _type is null ? [] : _wheels.AsSpan(0, _type.Wheels.Count).ToArray();

    /// <summary>Gets how many of its wheels are attach objects that are there, <c>nWheel</c>.</summary>
    public int AttachedWheels => _attachedWheels;

    /// <summary>Gets the gun it carries, once prepared; <see langword="null"/> when its model has no attach point for one.</summary>
    public Igi2AiStationaryGun? Gun { get; private set; }

    /// <summary>Gets its detection, once prepared.</summary>
    public Igi2AiDetection? Detection => _detection;

    /// <summary>Gets the view its detection looks through, <c>tAIDetectionView</c>.</summary>
    public Igi2DetectionView View { get; } = new();

    /// <summary>Gets a value indicating whether its can fire expression held at its last run, <c>isFire</c>.</summary>
    public bool IsFire => _isFire;

    /// <summary>Gets a value indicating whether its door is told to be open.</summary>
    public bool IsDoorOpen { get; private set; }

    /// <summary>Gets a value indicating whether an explosion is coming, <c>isExplode</c>.</summary>
    public bool IsExploding => _isExplode;

    /// <summary>Gets a value indicating whether it has blown up and been taken out of the world, <c>isExploded</c>.</summary>
    public bool IsExploded => _isExploded != 0;

    /// <inheritdoc/>
    public bool IsDestroyed => _isExploded != 0;

    /// <summary>Gets its thrust as the engine gives it, <c>vRealThrust</c>.</summary>
    public float RealThrust { get; private set; }

    /// <summary>Gets its steering as it has come round, <c>vRealDeltaGamma</c>.</summary>
    public float RealDeltaGamma { get; private set; }

    /// <summary>Gets the thrust it is told, <c>vIdealThrust</c>.</summary>
    public float IdealThrust { get; private set; }

    /// <summary>Gets the steering it is told, <c>vIdealDeltaGamma</c>.</summary>
    public float IdealDeltaGamma { get; private set; }

    /// <summary>Gets a value indicating whether it is braking, <c>isBreak</c>.</summary>
    public bool IsBreak { get; private set; }

    /// <summary>Gets its engine sound's channels: its thrust, whether it drives, and its speed in thousands of units a tick.</summary>
    public IReadOnlyList<float> SoundChannels { get; } = new float[3];

    /// <summary>Gets or sets the level's models.</summary>
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets the magic object types its attach points are looked up in.</summary>
    public Igi2MagicObjTypes? Magic { get; set; }

    /// <summary>Gets or sets the level's sounds.</summary>
    public Igi2Sounds? Sounds { get; set; }

    /// <summary>Gets or sets the level's explosions, which it goes off as and is reached by.</summary>
    public Igi2ExplosionWorld? Explosions { get; set; }

    /// <inheritdoc/>
    public Igi2Pose Built => _built ?? Igi2Pose.FromAngles(Position, Orientation);

    /// <inheritdoc/>
    public Vector3d CurrentPosition => _body?.Position ?? Position;

    /// <inheritdoc/>
    public IReadOnlyList<float> CurrentOrientation => _body?.Orientation ?? Built.ToArray();

    /// <inheritdoc/>
    public Vector3d LastPosition => _body is null ? Position : _lastPosition;

    /// <inheritdoc/>
    public IReadOnlyList<float> LastOrientation => _body is null ? Built.ToArray() : _lastOrientation;

    /// <summary>Gets its radius: its model's, by its largest scale.</summary>
    public float Radius => _model?.NativeMarkerBound ?? 1f;

    /// <summary>
    /// Readies it as the level loads, <c>Car_UpdateInternalDataHandler</c>: its detection, its physics object of the type its model
    /// names, its wheels and its gun on its model's attach points, and its expressions.
    /// </summary>
    /// <param name="types">The physics object types.</param>
    /// <param name="models">The level's models.</param>
    /// <param name="world">What it moves through; the car's own solids are passed by.</param>
    /// <param name="weapons">The session's weapons, which arm its gun; <see langword="null"/> for none.</param>
    /// <param name="detection">The detectables it watches, or <see langword="null"/> for none.</param>
    /// <param name="random">The stream its gun's rounds draw from, or <see langword="null"/> for a fresh one.</param>
    public void Prepare(
        Igi2PhysicsObjectTypes types,
        Func<string, MefModel?> models,
        Igi2PhysicsWorld world,
        Igi2HumanWeapons? weapons,
        Igi2DetectionWorld? detection,
        IgiRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(world);
        Models = models;
        _detectionWorld = detection;
        _detection = detection is null ? null : NewDetection(detection);
        View.ConeAlpha = ViewconeAlpha * Igi2AiStationaryGun.DegreesToRadians;
        View.ConeGamma = ViewconeGamma * Igi2AiStationaryGun.DegreesToRadians;
        View.ConeLength = ViewconeLength * 4096.0;
        View.IsSecondaryViewUsed = false;
        _isExplode = false;
        _isExploded = 0;
        _isFire = false;
        _ticksToDie = -1;
        _model = models(Model);
        _built = Igi2Pose.FromAngles(Position, Orientation);
        int id = Id;
        _physicsWorld = world with
        {
            Includes = owner => (world.Includes?.Invoke(owner) ?? true)
                && !(owner is Igi2CollisionOwner { TypeName: TypeName } solid && solid.LevelId == id),
        };

        _type = null;
        _physicsType = types.TryFind(Model, out Igi2PhysicsObjectType type) ? type : null;
        if (_physicsType is not null)
        {
            _type = Igi2CarType.Parse(_physicsType.Parameters);
            _engineWheels = _type.Wheels.Count(static w => w.IsEngine);
        }

        AttachWheelsAndGun(weapons, models, random);
        Reset();
    }

    /// <summary>Gives it the solids it was built as, which then move with it.</summary>
    /// <param name="solids">Its solids, as <see cref="Igi2CollisionWorld.SolidsOf"/> gives them.</param>
    public void Bind(IReadOnlyList<Igi2RigidObject> solids) => _solids = solids ?? throw new ArgumentNullException(nameof(solids));

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        int tick = level.LevelFlow?.Tick ?? _runTick + 1;
        if (tick == _runTick)
        {
            return;
        }

        _runTick = tick;
        if (_isExploded != 0)
        {
            return;
        }

        RunEngineSound(level);
        _openDoor ??= new Igi2LevelExpression(OpenDoorExpression, Name, level.Natives);
        if (!_openDoor.IsEmpty)
        {
            IsDoorOpen = Igi2LevelExpression.Truncate(level.Evaluate(_openDoor)) != 0;
        }

        RunDetection(level);
        level.RunChildren(this);
        if (!_isWeaponSystemsRun)
        {
            MountGun();
        }

        Gun?.Run(_detection, tick);
        _isWeaponSystemsRun = false;
        Explode();
    }

    /// <inheritdoc/>
    public void UpdateAnimData(Igi2AnimUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update.Program);
        ReadOnlySpan<float> program = update.Program.Channels;
        float[] channels = [.. program[..Math.Min(4, program.Length)]];
        Drive(update.Time, channels, update.FlowTick);
        if (update.Time == 0)
        {
            update.Program.WriteChannel(1, HoldChannel);
            update.Program.WriteChannel(2, -2f);
        }
    }

    /// <summary>
    /// Takes a step of driving, <c>Car_UpdateAnimDataHandler</c>: at tick nought it is set back where the level put it; at any other it
    /// takes the thrust, the steering and the brake, and moves a tick.
    /// </summary>
    /// <param name="time">The step's tick.</param>
    /// <param name="channels">The steering, the thrust (-1 leaves it), an unused channel, and the brake (-1 for none).</param>
    /// <param name="flowTick">The level's tick, which keeps where it stood as the tick began.</param>
    public void Drive(int time, IReadOnlyList<float> channels, int flowTick)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (channels.Count > 1 && channels[1] != HoldChannel)
        {
            IdealThrust = channels[1];
        }

        IdealDeltaGamma = channels.Count > 0 ? channels[0] : 0f;
        IsBreak = channels.Count > 3 && channels[3] != HoldChannel;
        if (time != 0)
        {
            if (flowTick != _movedTick && _body is { } body)
            {
                _movedTick = flowTick;
                _lastPosition = body.Position;
                _lastOrientation = [.. body.Orientation];
            }

            MountGun();
            _isWeaponSystemsRun = true;
            Move();
            return;
        }

        Reset();
        MountGun();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Car_ExplosionHitHandler</c>: only a rocket, a weapon of the first type whose id names an RPG-7 or an RPG-18, and only one
    /// whose blast reaches it, sets it to explode.
    /// </remarks>
    public void ExplosionHit(in Igi2ExplosionHit hit)
    {
        if (hit.WeaponType is not { WeaponType: 0 } weapon
            || !(weapon.WeaponId.Contains("WEAPON_ID_RPG7", StringComparison.Ordinal)
                || weapon.WeaponId.Contains("WEAPON_ID_RPG18", StringComparison.Ordinal)))
        {
            return;
        }

        if (hit.Resolve(CurrentPosition, Radius).Value != 0.0)
        {
            _isExplode = true;
        }
    }

    /// <summary>Sets it to explode, as one of its hit zones being destroyed does, <c>Car_HitZoneDestroyedHandler</c>.</summary>
    public void HitZoneDestroyed() => _isExplode = true;

    /// <inheritdoc/>
    /// <remarks>Its gun's head and barrel, turned on it; everything else of it moves with it.</remarks>
    public bool TryMovePart(WorldPlacement placement, out Vector3d positionMetres, out Matrix3f orientation)
    {
        if (Gun is { } gun)
        {
            return gun.TryMovePart(placement, out positionMetres, out orientation);
        }

        ArgumentNullException.ThrowIfNull(placement);
        (positionMetres, orientation) = (default, placement.Orientation);
        return false;
    }

    /// <summary>Gets the model it left when it blew up, where it stood, or <see langword="null"/> when it has not or leaves none.</summary>
    /// <returns>The wreck.</returns>
    public Game.Player.TitleWorldModel? Wreck() =>
        _isExploded != 0 && _type is { DestroyedModel.Length: > 0 } type && Models?.Invoke(type.DestroyedModel) is not null
            ? new Game.Player.TitleWorldModel(type.DestroyedModel, CurrentPosition, Rows(CurrentOrientation))
            : null;

    /// <inheritdoc/>
    /// <remarks><c>Car_RegisterScriptSymbolsHandler</c>: one byte, <c>isExploded</c>.</remarks>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isExploded", () => _isExploded, value => _isExploded = (byte)value, sizeof(byte));
    }

    // AIDetection_InitDetection(.., 1, 4, Human_bAIDetectionQTaskMask, 4, 8).
    private static Igi2AiDetection NewDetection(Igi2DetectionWorld world) =>
        new(world, 1, Igi2AiDetection.LiveEnemies, Igi2DetectableKind.Human, 4, 8);

    private static Matrix3f Rows(IReadOnlyList<float> m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);

    // Wheel_CreateHandler, for each wheel attach point in order, and the gun's attach point (Vehicle_Create's attach objects).
    private void AttachWheelsAndGun(Igi2HumanWeapons? weapons, Func<string, MefModel?> models, IgiRandom? random)
    {
        _attachedWheels = 0;
        _gunAttachment = null;
        Gun = null;
        foreach (Igi2CarWheel wheel in _wheels)
        {
            wheel.IsAttached = false;
            wheel.Attachment = null;
        }

        // Car_SetWheelCount: a car whose wheels are not attach objects of their own has every wheel of its type.
        if (_type is { IsWheelsAttachObject: false })
        {
            _attachedWheels = _type.Wheels.Count;
            for (int i = 0; i < _type.Wheels.Count; i++)
            {
                _wheels[i].IsAttached = true;
            }
        }

        if (_model is null || Magic is not { } magic)
        {
            return;
        }

        foreach (MefAttachment attachment in _model.Attachments)
        {
            switch (magic.Find(attachment.Name)?.TaskType)
            {
                case WheelType when _type is { IsWheelsAttachObject: true } && _attachedWheels < _type.Wheels.Count:
                    _wheels[_attachedWheels].IsAttached = true;
                    _wheels[_attachedWheels].Attachment = attachment;
                    _attachedWheels++;
                    break;
                case Igi2AiStationaryGun.TypeName when Gun is null && weapons is not null:
                    _gunAttachment = attachment;
                    Igi2AiGunParameters parameters = new(
                        BarrelModel, RotationSound, MinAlpha, MaxAlpha, MaxGamma, AlphaSpeed, GammaSpeed, BetaSpeed, Accuracy, WeaponId);
                    Gun = new Igi2AiStationaryGun(parameters, magic.Find(attachment.Name)!.Model, weapons, models)
                    {
                        Sounds = Sounds,
                        Random = random ?? new(),
                    };
                    break;
            }
        }
    }

    // UpdateMagicObj on the gun: mounted on its attach point where the car now stands.
    private void MountGun()
    {
        if (Gun is { } gun && _gunAttachment is { } attachment)
        {
            gun.Mount(Igi2Pose.FromMatrix(CurrentPosition, Rows(CurrentOrientation)).Attach(attachment));
        }
    }

    // Car_RunHandler's engine sound: with a play sound expression, it sounds while that holds; with none, always.
    private void RunEngineSound(Igi2LevelRun level)
    {
        _playSound ??= new Igi2LevelExpression(PlaySoundExpression, Name, level.Natives);
        if (!_playSound.IsEmpty && Igi2LevelExpression.Truncate(level.Evaluate(_playSound)) == 0)
        {
            _engineSound?.Delete();
            _engineSound = null;
            return;
        }

        if (_engineSound is null && _type is { EngineSound.Length: > 0 } type)
        {
            _engineSound = Sounds?.New(type.EngineSound, CurrentPosition, () => CurrentPosition);
        }
    }

    // Car_RunDetection: while its can fire expression holds, it watches from its gun's muzzle along the gun, or from itself without
    // one; when it stops holding, the detection forgets everything.
    private void RunDetection(Igi2LevelRun level)
    {
        _canFire ??= new Igi2LevelExpression(CanFireExpression, Name, level.Natives);
        if (_canFire.IsEmpty)
        {
            return;
        }

        bool wasFire = _isFire;
        _isFire = Igi2LevelExpression.Truncate(level.Evaluate(_canFire)) != 0;
        if (_isFire)
        {
            if (Gun is { IsMounted: true } gun)
            {
                View.Position = gun.Muzzle;
                View.Orientation = [.. gun.BarrelFrame];
            }
            else
            {
                View.Position = CurrentPosition;
                View.Orientation = [.. CurrentOrientation];
            }

            View.IsSecondaryViewUsed = false;
            _detection?.Run(View, null, 1);
        }
        else if (wasFire && _detectionWorld is { } world)
        {
            _detection?.Clear();
            _detection = NewDetection(world);
        }
    }

    // Car_Explode.
    private void Explode()
    {
        if (!_isExplode || _type is not { } type)
        {
            return;
        }

        float radius = Radius;
        if (_ticksToDie == -1)
        {
            if (type.ExplodeVertices.Count == 0)
            {
                Explosions?.New(
                    CurrentPosition,
                    1,
                    0,
                    1f,
                    ((Fpu)radius * 0.25).F,
                    ((Fpu)radius * 0.05000000074505806).F,
                    ((Fpu)radius * 0.004999999888241291).F,
                    3,
                    1,
                    null,
                    FragmentModel,
                    ExplosionSound,
                    20,
                    0x402,
                    307200f,
                    null,
                    null,
                    Igi2ExplosionFalloff.Linear);
                _ticksToDie = 10;
                return;
            }

            float[] m = [.. CurrentOrientation];
            foreach (int vertex in type.ExplodeVertices)
            {
                int period = (int)(Explosions?.Random.NextBounded(30) ?? 0);
                Vector3d at = Igi2MagicVertex.Position(_model, vertex, m, CurrentPosition);
                Explosions?.New(
                    at, 1, period, 3f, 24576f, 6144f, 6144f, 3, 2, null, FragmentModel, ExplosionSound, 20, 0x402, 307200f, null, null,
                    Igi2ExplosionFalloff.Linear);
            }

            _ticksToDie = 30;
            return;
        }

        _ticksToDie--;
        if (_ticksToDie == 0)
        {
            Remove();
            _isExploded = 1;
        }
    }

    // Car_RemoveHandler on blowing up: its engine sound and its gun gone, and it out of the world.
    private void Remove()
    {
        _engineSound?.Delete();
        _engineSound = null;
        Gun?.StopSound();
        Gun = null;
        foreach (Igi2RigidObject solid in _solids)
        {
            solid.ChangeModel([], 0f);
        }
    }
}

/// <summary>A car's wheel as it is, <c>CarWheelState_s</c>, and how it turns.</summary>
public sealed class Igi2CarWheel
{
    /// <summary>Gets or sets a value indicating whether the wheel is there, <c>isAttached</c>: its suspension works only then.</summary>
    public bool IsAttached { get; set; }

    /// <summary>Gets or sets a value indicating whether it touched the ground this tick, <c>isContact</c>.</summary>
    public bool IsContact { get; set; }

    /// <summary>Gets or sets the unit normal of the ground it touched, <c>tContactNormal</c>.</summary>
    public Vector3f ContactNormal { get; set; }

    /// <summary>Gets or sets where it touches, in the car's frame, <c>tContactPoint</c>.</summary>
    public Vector3f ContactPoint { get; set; }

    /// <summary>Gets or sets its attach point on the car's model, when it is an attach object of its own.</summary>
    public MefAttachment? Attachment { get; set; }
}
