using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// A security camera, I.G.I. 2's <c>SCamera</c> with its <c>SCameraLens</c>: a lens on a holder that sweeps, looks for the other
/// side and for its own side's dead, follows what it sees and, after a moment, detects it; exporting <c>isDetection</c>,
/// <c>isLastDetection</c>, <c>nDetectionTime</c> and <c>isExploded</c>.
/// </summary>
/// <remarks>
/// <para>
/// Its tick (<c>SCamera_RunHandler</c>, <c>igi2 0x48F9E0</c>): its lens first, then, destroyed, it is off and tells the AI every
/// thirty ticks; otherwise its script, where it has one, says whether it is on.
/// </para>
/// <para>
/// The lens's tick (<c>SCamera_SCameraLensRunHandler</c>, <c>0x48EF20</c>): on, it tilts back to its authored tilt, looks
/// (<see cref="Igi2AiDetection"/>, on side one, for live enemies and dead friends among humans and cameras), turns
/// (<c>SCamera_RotateLens</c>, <c>0x48E7A0</c>) and beeps; off, it tilts down and detects nothing. Hit hard enough, it explodes
/// and the camera is destroyed.
/// </para>
/// <para>
/// Turning, it takes the first dead friend in plain sight, then the first enemy in plain sight over it; a new target restarts the
/// detection clock (two, one or three quarters of three seconds by difficulty), and detection comes when it runs out. It follows
/// a target outside five degrees of its aim at three quarters of a degree a tick, within its limits; with none, after two seconds
/// it sweeps between its limits at its speed, holding at each end for its delay.
/// </para>
/// <para>The visible lens follows the collision frame; its indicator uses the lens model's first magic vertex.</para>
/// </remarks>
public sealed partial class Igi2SecurityCameraTask
    : Igi2Task, IIgi2PlacedTask, IIgi2Detectable, IIgi2WeaponHitTarget, IIgi2ExplosionTarget, IIgi2PartMover
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "SCamera";

    /// <summary>The side every camera is on in a single-player game, <c>1</c>.</summary>
    public const int CameraTeam = 1;

    /// <summary>The lens's tilt step a tick towards its aim, <c>0x3D32B8C3</c> (2.5°).</summary>
    public const float TiltStep = 0.043633234f;

    /// <summary>The tilt an off camera droops to, <c>-1.3089969</c> (−75°).</summary>
    public const float OffTilt = -1.3089969f;

    /// <summary>How far off its aim a target must be before the lens turns to it, <c>0x3DB2B8C3</c> (5°).</summary>
    public const float FollowDeadband = 0.08726647f;

    /// <summary>The most the lens turns to follow in a tick, <c>0x3C567750</c> (0.75°).</summary>
    public const float FollowStep = 0.013089969f;

    /// <summary>Degrees to radians, <c>0x3C8EFA35</c>.</summary>
    public const float DegreesToRadians = 0.017453292f;

    /// <summary>A tick of a second, <c>0x3D088889</c>.</summary>
    public const float TickSeconds = 0.033333335f;

    /// <summary>The ticks the lens waits after losing a target before it sweeps again in a single-player game, <c>60</c>.</summary>
    public const int TrackTimeout = 60;

    /// <summary>The ticks between a destroyed camera's calls to the AI, <c>30</c>.</summary>
    public const int DestroyedEventTicks = 30;

    /// <summary>The detection clock's share of three seconds at each difficulty: easy, normal, hard.</summary>
    public static readonly IReadOnlyList<float> DetectionShare = [2f, 1f, 0.75f];

    private readonly Lens _lens = new();
    private Igi2LevelExpression? _on;
    private float[] _holderFrame = Igi2Pose.Rotation(0f, 0f, 0f);
    private Vector3d _position;
    private float _radius = 1f;
    private byte _isOn;
    private byte _isDetection;
    private byte _isLastDetection;
    private byte _isDestroyed;
    private int _detectionTime;
    private int _destroyedEventTimer;
    private bool _isDeadFriend;
    private IReadOnlyList<Igi2RigidObject> _solids = [];

    /// <summary>Gets or sets where the holder stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Holder Position")]
    public Vector3d HolderPosition { get; set; }

    /// <summary>Gets or sets the holder's turn.</summary>
    [ScriptParam(ScriptParamType.Angle, "Holder Gamma")]
    public float HolderGamma { get; set; }

    /// <summary>Gets or sets the holder's model.</summary>
    [ScriptParam(ScriptParamType.String16, "Holder Model")]
    public string HolderModel { get; set; } = "335_01_1";

    /// <summary>Gets or sets a value indicating whether the computer's map shows it.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Camera Visible on Computer")]
    public bool IsVisibleOnComputer { get; set; } = true;

    /// <summary>Gets or sets the lens's tilt at rest.</summary>
    [ScriptParam(ScriptParamType.Angle, "Camera Alpha")]
    public float CameraAlpha { get; set; }

    /// <summary>Gets or sets the lens's turn at rest, on the holder's.</summary>
    [ScriptParam(ScriptParamType.Angle, "Camera Gamma")]
    public float CameraGamma { get; set; }

    /// <summary>Gets or sets the lens's model.</summary>
    [ScriptParam(ScriptParamType.String16, "Camera Model")]
    public string CameraModel { get; set; } = "335_02_1";

    /// <summary>Gets or sets the lens's model once destroyed.</summary>
    [ScriptParam(ScriptParamType.String16, "Camera Destroyed Model")]
    public string CameraDestroyedModel { get; set; } = "335_03_1";

    /// <summary>Gets or sets how far left of rest the lens turns, in degrees.</summary>
    [ScriptParam(ScriptParamType.Int16, "Rotate Gamma Left(d)")]
    public short RotateLeft { get; set; } = 45;

    /// <summary>Gets or sets how far right of rest the lens turns, in degrees.</summary>
    [ScriptParam(ScriptParamType.Int16, "Rotate Gamma Right(d)")]
    public short RotateRight { get; set; } = 45;

    /// <summary>Gets or sets how fast it sweeps, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Int16, "Rotate Gamma Speed (d/s)")]
    public short RotateSpeed { get; set; } = 45;

    /// <summary>Gets or sets how long it holds at each end of its sweep, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Gamma Delay (s)")]
    public float GammaDelay { get; set; } = 1f;

    /// <summary>Gets or sets its view cone's full height, in degrees.</summary>
    [ScriptParam(ScriptParamType.Int16, "Viewcone Alpha (d)")]
    public short ViewconeAlpha { get; set; } = 90;

    /// <summary>Gets or sets its view cone's full width, in degrees.</summary>
    [ScriptParam(ScriptParamType.Int16, "Viewcone Gamma (d)")]
    public short ViewconeGamma { get; set; } = 90;

    /// <summary>Gets or sets its view cone's length, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Viewcone length (m)")]
    public float ViewconeLength { get; set; } = 10f;

    /// <summary>Gets or sets the script that says whether it is on.</summary>
    [ScriptParam(ScriptParamType.VarString, "On Expression")]
    public string OnScript { get; set; } = string.Empty;

    /// <summary>Gets or sets the damage that destroys it.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max damage factor")]
    public float MaxDamageFactor { get; set; } = 0.5f;

    /// <summary>Gets a value indicating whether it is on, <c>isOn</c>.</summary>
    public bool IsOn => _isOn != 0;

    /// <summary>Gets a value indicating whether it has detected something, <c>isDetection</c>.</summary>
    public bool IsDetection => _isDetection != 0;

    /// <summary>Gets a value indicating whether it is destroyed, <c>isDestroyed</c>, exported as <c>isExploded</c>.</summary>
    public bool IsDestroyed => _isDestroyed != 0;

    /// <summary>Gets the ticks left before what it watches is detected, <c>nDetectionTime</c>.</summary>
    public int DetectionTime => _detectionTime;

    /// <summary>Gets a value indicating whether what it watches is a dead friend, <c>isDeadFriend</c>.</summary>
    public bool IsDeadFriend => _isDeadFriend;

    /// <summary>Gets what the lens is following, <c>ptTargetRef</c>, or <see langword="null"/>.</summary>
    public IIgi2Detectable? Target => _lens.Target;

    /// <summary>Gets or sets where its AI events go, <c>HumanAI_SendEvent</c>, or <see langword="null"/> when no one hears them.</summary>
    public Action<Ai.Igi2AiEvent>? AiEvents { get; set; }

    /// <summary>
    /// Gets or sets whether the level's alarm sounds: the last alarm control made's <c>isAlarm</c> (<c>dword_834DED8</c>), or
    /// <see langword="null"/> when the level has none.
    /// </summary>
    public Func<bool>? IsAlarmSounding { get; set; }

    /// <summary>Gets the lens's tilt, <c>vAlpha</c>.</summary>
    public float LensAlpha => _lens.Alpha;

    /// <summary>Gets the lens's turn off rest, <c>vGamma</c>.</summary>
    public float LensGamma => _lens.Gamma;

    /// <summary>Gets the lens pivot in world units.</summary>
    public Vector3d LensPosition => _lens.Position;

    /// <summary>Gets the lens frame, shared by its collision and visible model.</summary>
    public Matrix3f LensOrientation => new(_lens.Frame[0], _lens.Frame[1], _lens.Frame[2],
        _lens.Frame[3], _lens.Frame[4], _lens.Frame[5], _lens.Frame[6], _lens.Frame[7], _lens.Frame[8]);

    /// <summary>Gets whether the indicator is in its bright phase (scamera.c SCamera_SCameraLensRunHandler).</summary>
    public bool LightBright { get; private set; }

    private int _lightTicks;

    /// <summary>Builds the powered lens's blinking indicator (scamera.c SCamera_SCameraLensEnterHandler).</summary>
    /// <returns>The light, or null when powered off or destroyed.</returns>
    public Game.Player.TitleWorldSprite? Indicator()
    {
        if (!IsOn || IsDestroyed) { return null; }
        Vector3d position = Igi2MagicVertex.Position(Models?.Invoke(_lens.Model ?? CameraModel), 0, _lens.Frame, _lens.Position);
        bool alert = IsDetection || Target is not null;
        bool yellow = HolderModel.StartsWith("313", StringComparison.Ordinal);
        byte red = alert ? (byte)128 : yellow ? (byte)100 : (byte)0;
        byte green = alert ? (byte)0 : yellow ? (byte)100 : (byte)128;
        if (!LightBright && !alert) { red /= 2; green /= 2; }
        return new("alarmlight.spr", position, LightBright ? 1024f : 512f,
            Core.Graphics.Bgra32.FromRgba(red, green, 0, 128));
    }

    /// <inheritdoc/>
    public bool TryMovePart(Game.World.WorldPlacement placement, out Vector3d positionMetres, out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        positionMetres = placement.Position;
        orientation = placement.Orientation;
        if (placement.LevelId != Id || !string.Equals(placement.Instance.Name, CameraModel, StringComparison.OrdinalIgnoreCase)) { return false; }
        // scamera.c SCamera_SCameraLensRunHandler: all lens attachments follow the lens pivot.
        Matrix3f built = Igi2Pose.FromAngles(default, new Vector3f(CameraAlpha, 0f, ((Fpu)HolderGamma + CameraGamma).F)).ToMatrix();
        Matrix3f current = LensOrientation;
        Vector3d local = built.Transform(Game.World.WorldScene.ToUnits(placement.Position) - LensPosition);
        positionMetres = Game.World.WorldScene.ToMetres(current.Transposed().Transform(local) + LensPosition);
        orientation = current * (built.Transposed() * placement.Orientation);
        return true;
    }

    /// <summary>Gets the lens's damage, <c>vDamage</c>.</summary>
    public float Damage => _lens.Damage;

    /// <summary>Gets where the lens looks from.</summary>
    public Igi2DetectionView View => _lens.View;

    /// <summary>Gets what the lens watches, once the camera is bound to a world.</summary>
    public Igi2AiDetection? Detection => _lens.Detection;

    /// <summary>Gets or sets the level's models.</summary>
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets the level's sounds.</summary>
    public Igi2Sounds? Sounds { get; set; }

    /// <summary>Gets or sets the explosions the destroyed lens goes off in.</summary>
    public Igi2ExplosionWorld? Explosions { get; set; }

    /// <summary>Gets or sets the difficulty the detection clock is set by.</summary>
    public AiDifficulty Difficulty { get; set; } = AiDifficulty.Normal;

    /// <inheritdoc/>
    public Vector3d DetectablePosition => _position;

    /// <inheritdoc/>
    public float DetectableRadius => _radius;

    /// <inheritdoc/>
    public float? VisibilityFactor => null;

    /// <inheritdoc/>
    public bool IsGone => IsDeleted;

    /// <summary>Starts looking in a world, as <c>SCamera_SCameraLensCreateHandler</c> (<c>igi2 0x48EC90</c>) starts the lens's detection.</summary>
    /// <param name="world">The detectables.</param>
    /// <param name="solids">The camera's solids, the holder's first and the lens's after.</param>
    public void Bind(Igi2DetectionWorld world, IReadOnlyList<Igi2RigidObject> solids)
    {
        ArgumentNullException.ThrowIfNull(world);
        _solids = solids ?? throw new ArgumentNullException(nameof(solids));
        _lens.Detection = new Igi2AiDetection(
            world,
            CameraTeam,
            Igi2AiDetection.DeadFriends | Igi2AiDetection.LiveEnemies,
            Igi2DetectableKind.Human | Igi2DetectableKind.SecurityCamera,
            coneInterval: 4,
            visualInterval: 8)
        {
            Flags = Igi2AiDetection.NoVisibilityRange,
        };

        // The lens's solid answers the bullets the lens takes.
        foreach (Igi2RigidObject solid in solids)
        {
            if (solid.Owner is Igi2CollisionOwner { Depth: > 0 })
            {
                solid.Handler = this;
            }
        }
    }

    /// <summary>
    /// Readies the camera and its lens (<c>SCamera_UpdateInternalDataHandler</c>, <c>igi2 0x48FB80</c>, and
    /// <c>SCamera_SCameraLensUpdateInternalDataHandler</c>, <c>0x48F370</c>): the holder at its place turned by its gamma, the lens at
    /// the holder's first vertex tilted and turned to rest, and its view from the lens's second vertex.
    /// </summary>
    public void Prepare()
    {
        _position = HolderPosition;
        Fpu sin = Fpu.Sin(HolderGamma), cos = Fpu.Cos(HolderGamma);
        _holderFrame = [cos.F, (-sin).F, 0f, sin.F, cos.F, 0f, 0f, 0f, 1f];
        _radius = Models?.Invoke(HolderModel)?.NativeMarkerBound ?? 1f;
        _lens.MaxDamage = MaxDamageFactor;

        _lens.Position = Igi2MagicVertex.Position(Models?.Invoke(HolderModel), 0, _holderFrame, HolderPosition);
        CameraAlpha = LimitAngle(CameraAlpha);
        _lens.Alpha = CameraAlpha;
        _lens.Frame = Igi2Pose.Rotation(_lens.Alpha, 0f, ((Fpu)HolderGamma + CameraGamma).F);
        _lens.View.Position = Igi2MagicVertex.Position(Models?.Invoke(_lens.Model ?? CameraModel), 1, _lens.Frame, _lens.Position);
        _lens.View.Orientation = _lens.Frame;
        _lens.View.ConeAlpha = ((Fpu)(double)(ViewconeAlpha / 2) * 0.017453292).Value;
        _lens.View.ConeGamma = ((Fpu)(double)(ViewconeGamma / 2) * 0.017453292).Value;
        _lens.View.ConeLength = ((Fpu)ViewconeLength * 4096.0).Value;
        RotateLeft = Math.Max(RotateLeft, (short)0);
        RotateRight = Math.Max(RotateRight, (short)0);
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        RunLens();
        level.RunChildren(this);

        if (_isDestroyed != 0)
        {
            _isOn = 0;
            if (_destroyedEventTimer != 0)
            {
                _destroyedEventTimer--;
            }
            else
            {
                // HumanAI_SendEvent (igi2 0x460A90): AIEVENT_DESTROYED_CAMERA to every team, from the camera, eight metres out.
                AiEvents?.Invoke(new Ai.Igi2AiEvent(Ai.Igi2AiEvent.DestroyedCamera, -1, 131, 40960f, _position, default, null));
                _destroyedEventTimer = DestroyedEventTicks;
            }

            return;
        }

        _on ??= new Igi2LevelExpression(OnScript, Name, level.Natives);
        if (!_on.IsEmpty)
        {
            _isOn = (byte)Igi2LevelExpression.Truncate(level.Evaluate(_on));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>SCamera_AIDetectionGetInfoHandler</c> (<c>igi2 0x490030</c>): a camera on side one, alive until destroyed, its three points
    /// all its place.
    /// </remarks>
    public Igi2DetectionInfo DetectionInfo() =>
        new(Igi2DetectableKind.SecurityCamera, _position, _position, _position, CameraTeam, _isDestroyed == 0, IsActive: true);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>SCamera_SCameraLensWeaponHitHandler</c> (<c>igi2 0x48F5E0</c>): while whole, the lens adds the bullet's damage, and enough
    /// of it explodes the lens on its next tick. The shooter is told it hit something, which only counts towards the score.
    /// </remarks>
    public void WeaponHit(in Igi2WeaponHit hit)
    {
        BulletDamage(hit.Damage);
        if (hit.Shooter is Humans.Igi2Human scorer && hit.AmmoType is { UsedForAccuracyScore: true })
        {
            scorer.HitByYou(this);
        }
    }

    /// <summary>A bullet's damage to the lens, as <see cref="WeaponHit"/> takes it.</summary>
    /// <param name="damage">The damage the bullet still does.</param>
    public void BulletDamage(float damage)
    {
        if (_isDestroyed != 0)
        {
            return;
        }

        _lens.Damage = ((Fpu)damage + _lens.Damage).F;
        if (!(_lens.Damage < _lens.MaxDamage))
        {
            _lens.IsExplode = true;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>SCamera_SCameraLensExplosionHitHandler</c> (<c>igi2 0x48F670</c>): while whole, the lens adds what of the blast reaches it,
    /// sized by its model's radius and largest scale, and enough of it explodes the lens.
    /// </remarks>
    public void ExplosionHit(in Igi2ExplosionHit hit)
    {
        if (_isDestroyed != 0)
        {
            return;
        }

        float radius = _solids.FirstOrDefault(static s => s.Owner is Igi2CollisionOwner { Depth: > 0 })?.BoundingRadius
            ?? Models?.Invoke(CameraModel)?.NativeMarkerBound ?? 1f;
        _lens.Damage = (hit.Resolve(_lens.Position, radius) + _lens.Damage).F;
        if (!(_lens.Damage < _lens.MaxDamage))
        {
            _lens.IsExplode = true;
        }
    }

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Holder Position" => HolderPosition,
            "Holder Gamma" => HolderGamma,
            "Holder Model" => HolderModel,
            "Camera Alpha" => CameraAlpha,
            "Camera Gamma" => CameraGamma,
            "Camera Model" => CameraModel,
            "Camera Destroyed Model" => CameraDestroyedModel,
            _ => null,
        };
        return value is not null;
    }

    /// <summary>An angle brought within a half turn either way, <c>SCamera_LimitAngle</c> (<c>igi2 0x48EEC0</c>).</summary>
    /// <param name="angle">The angle, in radians.</param>
    /// <returns>The angle less its whole turns, from −π up to but not including π.</returns>
    public static float LimitAngle(float angle)
    {
        int turns = Fpu.ToInt32((Fpu)angle * 0.15915494f);
        Fpu r = (Fpu)angle - ((Fpu)(double)turns * 6.2831855f);
        if (r.Value < -3.1415927f)
        {
            return (r + 6.2831855f).F;
        }

        return r.Value < 3.1415927f ? r.F : (r - 6.2831855f).F;
    }

    // The event's position is the target task's tOrientation (ModelObj+124) read as three doubles: retail points ptPosition at
    // &ptTargetQTask[3].ptPrev, which is the frame, not tPos. Its first six floats, two to a double, are what the AI is told.
    private static Vector3d OrientationAsPosition(IIgi2Detectable target)
    {
        if (target is not Humans.Igi2Human human)
        {
            return default;
        }

        Matrix3f m = human.Orientation;
        return new Vector3d(Pair(m.M11, m.M12), Pair(m.M13, m.M21), Pair(m.M22, m.M23));

        static double Pair(float low, float high) =>
            BitConverter.Int64BitsToDouble(((long)BitConverter.SingleToInt32Bits(high) << 32) | (uint)BitConverter.SingleToInt32Bits(low));
    }

    /// <summary>Exports its four (<c>SCamera_RegisterScriptSymbolsHandler</c>, <c>igi2 0x48FE70</c>).</summary>
    /// <param name="variables">The level's variables.</param>
    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindInt(this, "isDetection", () => _isDetection, value => _isDetection = (byte)value, sizeof(byte));
        variables.BindInt(this, "isLastDetection", () => _isLastDetection, value => _isLastDetection = (byte)value, sizeof(byte));
        variables.BindInt(this, "nDetectionTime", () => _detectionTime, value => _detectionTime = value);
        variables.BindInt(this, "isExploded", () => _isDestroyed, value => _isDestroyed = (byte)value, sizeof(byte));
    }

    /// <inheritdoc/>
    protected override void OnParsed() => Prepare();

    // The lens's tick, SCamera_SCameraLensRunHandler (igi2 0x48EF20). The computer map's blink timers are the computer's.
    private void RunLens()
    {
        if (_lightTicks != 0) { _lightTicks--; }
        else { LightBright = !LightBright; _lightTicks = 7; }
        // A first detection while the level's alarm sounds tells the camera's side where its target is: AIEVENT_CAMERAALARMUPDATE.
        if (_isDetection != 0 && _isLastDetection == 0 && IsAlarmSounding?.Invoke() == true && _lens.Target is { } seen)
        {
            AiEvents?.Invoke(new Ai.Igi2AiEvent(Ai.Igi2AiEvent.CameraAlarmUpdate, CameraTeam, 0, 0f, OrientationAsPosition(seen), default, null));
        }

        _isLastDetection = _isDetection;
        Lens lens = _lens;
        lens.Alpha = LimitAngle(lens.Alpha);
        if (_isOn != 0)
        {
            lens.Alpha = Tilt(lens.Alpha, CameraAlpha);
            lens.View.Position = Igi2MagicVertex.Position(Models?.Invoke(lens.Model ?? CameraModel), 1, lens.Frame, lens.Position);
            lens.View.Orientation = lens.Frame;
            lens.View.IsSecondaryViewUsed = false;
            lens.Detection?.Run(lens.View, this, CameraTeam);
            Rotate();
            PlayDetectionSound();
        }
        else
        {
            lens.Alpha = Tilt(LimitAngle(lens.Alpha), OffTilt);
            _isDetection = 0;
            lens.MoveSound = Kill(lens.MoveSound);
            lens.SearchSound = Kill(lens.SearchSound);
            lens.DetectionSound = Kill(lens.DetectionSound);
        }

        if (lens.IsExplode)
        {
            lens.IsExplode = false;
            _isDestroyed = 1;
            lens.Model = CameraDestroyedModel;
            if (Models?.Invoke(CameraDestroyedModel) is { } destroyed && Igi2CollisionWorld.Meshes(destroyed) is { } meshes)
            {
                foreach (Igi2RigidObject solid in _solids)
                {
                    if (solid.Owner is Igi2CollisionOwner { Depth: > 0 })
                    {
                        solid.ChangeModel(meshes, destroyed.NativeMarkerBound);
                    }
                }
            }

            Explosions?.New(
                lens.Position,
                1,
                0,
                0.25f,
                2048f,
                0f,
                0f,
                7,
                3,
                null,
                Igi2Explosive.FragmentModel,
                "explo_camera",
                Igi2Explosive.AiEvent,
                Igi2Explosive.AiEventFlags,
                49152f,
                null,
                null,
                Igi2ExplosionFalloff.Linear);
        }

        lens.Frame = Igi2Pose.Rotation(lens.Alpha, 0f, (((Fpu)lens.Gamma + HolderGamma) + CameraGamma).F);

        // The lens's solid turns with it.
        foreach (Igi2RigidObject solid in _solids)
        {
            if (solid.Owner is Igi2CollisionOwner { Depth: > 0 })
            {
                solid.Position = lens.Position;
                Array.Copy(lens.Frame, solid.Orientation, solid.Orientation.Length);
            }
        }
    }

    // The tilt a step nearer an aim, never past it.
    private static float Tilt(float alpha, float aim)
    {
        if (alpha < aim)
        {
            float raised = ((Fpu)alpha + TiltStep).F;
            return raised <= aim ? raised : aim;
        }

        if (alpha > aim)
        {
            float lowered = ((Fpu)alpha - TiltStep).F;
            return lowered < aim ? aim : lowered;
        }

        return alpha;
    }

    // SCamera_RotateLens (igi2 0x48E7A0).
    private void Rotate()
    {
        Lens lens = _lens;
        float oldGamma = lens.Gamma;
        _isDetection = 0;
        _isDeadFriend = false;
        IIgi2Detectable? target = null;

        // A dead friend in plain sight. A soldier's body only the first time a camera sees it, or while this one is already
        // counting down to a detection; once seen it is marked (HumanSoldier +0xBDD) and passed over after.
        foreach (Igi2DetectionItem item in lens.Detection?.Friends ?? [])
        {
            if (!item.IsFullVisible)
            {
                continue;
            }

            if (item.Target is Soldiers.Igi2HumanSoldier body)
            {
                if (body.IsSeenByCamera && _detectionTime == 0)
                {
                    continue;
                }

                body.IsSeenByCamera = true;
            }

            target = item.Target;
            _isDeadFriend = true;
            break;
        }

        // The first enemy in plain sight over any friend.
        foreach (Igi2DetectionItem item in lens.Detection?.Enemies ?? [])
        {
            if (item.IsFullVisible)
            {
                target = item.Target;
                _isDeadFriend = false;
                break;
            }
        }

        if (target is not null)
        {
            if (!ReferenceEquals(lens.Target, target))
            {
                lens.Target = target;
                _detectionTime = Fpu.ToInt32((Fpu)DetectionShare[Math.Clamp((int)Difficulty, 0, 2)] * 90.0);
            }

            if (_detectionTime != 0)
            {
                _detectionTime--;
            }

            if (_detectionTime == 0)
            {
                _isDetection = 1;
            }

            Follow(TargetPoint(target));
        }
        else
        {
            _detectionTime = 0;
            if (lens.Target is not null)
            {
                lens.Target = null;
                lens.TrackTimeout = TrackTimeout;
            }

            if (lens.TrackTimeout != 0)
            {
                lens.TrackTimeout--;
            }

            if (lens.TrackTimeout == 0)
            {
                Sweep();
            }
        }

        if (oldGamma == lens.Gamma)
        {
            lens.MoveSound = Kill(lens.MoveSound);
        }
        else if (lens.MoveSound is null)
        {
            lens.MoveSound = Sounds?.New("camera_move", lens.Position);
        }
    }

    // Where a target is followed to: a human's eyes as its stance puts them, a camera's place.
    private static Vector3d TargetPoint(IIgi2Detectable target) => target.DetectionInfo().Head;

    // Turns to a point outside the dead band, a clamped step at a time, within the limits.
    private void Follow(Vector3d point)
    {
        Lens lens = _lens;
        float[] m = lens.Frame;
        Fpu dx = (Fpu)point.X - lens.Position.X, dy = (Fpu)point.Y - lens.Position.Y, dz = (Fpu)point.Z - lens.Position.Z;
        Fpu x = (((Fpu)m[6] * dz) + ((Fpu)m[3] * dy)) + ((Fpu)m[0] * dx);
        Fpu y = (((Fpu)m[7] * dz) + ((Fpu)m[4] * dy)) + ((Fpu)m[1] * dx);
        float gamma = Igi2Pose.CalcGamma(x.Value, y.Value);
        if (!(Math.Abs(gamma) > FollowDeadband))
        {
            return;
        }

        float step = gamma < -FollowStep ? -FollowStep : gamma > FollowStep ? FollowStep : gamma;
        float turned = ((Fpu)step + lens.Gamma).F;
        float left = ((Fpu)(double)(-RotateLeft) * DegreesToRadians).F;
        float right = ((Fpu)(double)RotateRight * DegreesToRadians).F;
        lens.Gamma = left > turned ? left : right < turned ? right : turned;
    }

    // Sweeps from end to end at its speed, holding at each for its delay.
    private void Sweep()
    {
        Lens lens = _lens;
        if (lens.RotateDirection == 0)
        {
            lens.RotateDirection = 1;
            lens.IdealGamma = ((Fpu)(double)RotateRight * DegreesToRadians).F;
        }

        if (lens.GammaDelay != 0)
        {
            lens.GammaDelay--;
            return;
        }

        Fpu step = ((Fpu)(double)RotateSpeed * DegreesToRadians) * TickSeconds;
        if (lens.Gamma < lens.IdealGamma)
        {
            float turned = (step + lens.Gamma).F;
            lens.Gamma = turned > lens.IdealGamma ? lens.IdealGamma : turned;
        }
        else if (lens.Gamma > lens.IdealGamma)
        {
            float turned = ((Fpu)lens.Gamma - step).F;
            lens.Gamma = turned < lens.IdealGamma ? lens.IdealGamma : turned;
        }

        if (lens.Gamma == lens.IdealGamma)
        {
            lens.GammaDelay = Fpu.ToInt32((Fpu)GammaDelay * 30.0);
            bool toRight = lens.RotateDirection == -1;
            lens.RotateDirection = toRight ? 1 : -1;
            lens.IdealGamma = ((Fpu)(double)(toRight ? RotateRight : -RotateLeft) * DegreesToRadians).F;
        }
    }

    // SCamera_SCameraLensPlayDetectionSound (igi2 0x48E6E0): while the clock runs, a search beep and a look every tick; once
    // detected, the detection beep, the alarm one for a body.
    private void PlayDetectionSound()
    {
        Lens lens = _lens;
        if (lens.SearchSound is { IsAlive: false })
        {
            lens.SearchSound = null;
        }

        if (_detectionTime != 0)
        {
            lens.Detection?.Flags |= Igi2AiDetection.Continuous;
            lens.SearchSound ??= Sounds?.New("camera_beep_s", lens.Position);
        }
        else
        {
            if (lens.Detection is { } detection)
            {
                detection.Flags &= ~Igi2AiDetection.Continuous;
            }

            lens.SearchSound = Kill(lens.SearchSound);
        }

        if (_isDetection != 0)
        {
            if (!lens.HasDetectionSound)
            {
                lens.DetectionSound = Sounds?.New(_isDeadFriend ? "camera_beep_al" : "camera_beep_l", lens.Position);
                lens.HasDetectionSound = true;
            }
        }
        else
        {
            lens.DetectionSound = Kill(lens.DetectionSound);
            lens.HasDetectionSound = false;
        }
    }

    private static Igi2Sound? Kill(Igi2Sound? sound)
    {
        sound?.Delete();
        return null;
    }

    // The lens, SCameraLens: the camera's child task, here its state.
    private sealed class Lens
    {
        public Vector3d Position { get; set; }

        public float[] Frame { get; set; } = Igi2Pose.Rotation(0f, 0f, 0f);

        public string? Model { get; set; }

        public float Alpha { get; set; }

        public float Gamma { get; set; }

        public float IdealGamma { get; set; }

        public int GammaDelay { get; set; }

        public int RotateDirection { get; set; }

        public float Damage { get; set; }

        public float MaxDamage { get; set; } = 0.5f;

        public bool IsExplode { get; set; }

        public int TrackTimeout { get; set; }

        public IIgi2Detectable? Target { get; set; }

        public Igi2DetectionView View { get; } = new();

        public Igi2AiDetection? Detection { get; set; }

        public Igi2Sound? MoveSound { get; set; }

        public Igi2Sound? SearchSound { get; set; }

        public Igi2Sound? DetectionSound { get; set; }

        public bool HasDetectionSound { get; set; }
    }
}
