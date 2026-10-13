using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// An automatic gun, I.G.I. 2's <c>AIStationaryGunHolder</c> (aistationarygunholder.c) and the <c>AIStationaryGun</c> it carries on its
/// model's attach point (aistationarygun.c): a holder that watches through a cone from the gun's muzzle while its on expression holds,
/// and a gun whose head turns and whose barrel tilts towards the first enemy it sees, firing once it is on target.
/// </summary>
/// <remarks>
/// <para>
/// <c>AIStationaryGunHolder_RunHandler</c>: its team and on expressions, where it has them, set its side and whether it watches. On, its
/// detection runs from the barrel's third magic vertex, the muzzle, along the barrel, through its cone alone; turned off, its detection
/// forgets everything. Then the gun runs.
/// </para>
/// <para>
/// <c>AIStationaryGun_RunHandler</c>: the first enemy listed with any point in plain sight is the target, its shoulders for a human,
/// taken into the gun's frame: its bearing and its elevation are where to aim. A target lost is aimed at where it was for ninety ticks
/// more; with none, the gun comes back to nought. Wholly turning guns go the short way round. The tilt and turn each move at most their
/// speed a tick towards the aim, the turn kept within a half turn either way, then held within the gun's limits. With a target, within
/// five degrees on both is on target: an empty weapon reloads, and on target it fires and its barrel spins. A trigger held as it comes
/// off target, or a burst done, is let go. While the aim, held within the limits, is more than two degrees away, its rotation sound
/// plays.
/// </para>
/// </remarks>
public sealed partial class Igi2AiStationaryGunHolderTask : Igi2Task, IIgi2PlacedTask, IIgi2PartMover, IIgi2WeaponOwner
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "AIStationaryGunHolder";

    /// <summary>The magic task type the gun's attach point is.</summary>
    public const string GunType = "AIStationaryGun";

    /// <summary>The ticks a lost target is still aimed at.</summary>
    public const int RememberTicks = 90;

    /// <summary>How near the aim counts as on target, five degrees.</summary>
    public const double OnTarget = 0.0872664675116539;

    /// <summary>How far the aim must be for the gun to sound turning, two degrees.</summary>
    public const double Turning = 0.03490658476948738;

    /// <summary>A degree in radians, as the original's single-precision constant.</summary>
    public const double DegreesToRadians = 0.01745329238474369;

    /// <summary>A tick in seconds, as the original's single-precision constant.</summary>
    public const double TickSeconds = 0.03333333507180214;

    private Igi2LevelExpression? _onExpression;
    private Igi2LevelExpression? _teamExpression;
    private byte _isOn;
    private int _team = 1;
    private Igi2AiDetection? _detection;
    private Igi2DetectionWorld? _detectionWorld;
    private Igi2WeaponFiring? _firing;
    private Igi2Pose? _base;
    private string? _headModel;
    private float[] _headFrame = Igi2Pose.Rotation(0f, 0f, 0f);
    private float[] _barrelFrame = Igi2Pose.Rotation(0f, 0f, 0f);
    private Vector3d _barrelPosition;
    private float _tilt;
    private float _turn;
    private float _rememberedTilt;
    private float _rememberedTurn;
    private int _rememberTimer;
    private bool _wasFound;
    private bool _wasOnTarget;
    private Igi2Sound? _rotationSound;
    private int _ticks;
    private float _minAlpha;
    private float _maxAlpha;
    private float _maxGamma;
    private float _alphaSpeed;
    private float _gammaSpeed;
    private float _betaSpeed;

    /// <summary>Gets or sets where it stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its turn, as three Euler angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets its holder's model.</summary>
    [ScriptParam(ScriptParamType.String32, "Holder model")]
    public string HolderModel { get; set; } = string.Empty;

    /// <summary>Gets or sets its cone's half width up and down, in degrees.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Viewcone Alpha")]
    public float ViewconeAlpha { get; set; } = 30f;

    /// <summary>Gets or sets its cone's half width across, in degrees.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Viewcone Gamma")]
    public float ViewconeGamma { get; set; } = 30f;

    /// <summary>Gets or sets how far its cone reaches, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Viewcone Length")]
    public float ViewconeLength { get; set; } = 10f;

    /// <summary>Gets or sets the expression that says whether it watches.</summary>
    [ScriptParam(ScriptParamType.VarString, "On expression")]
    public string OnExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that says its side.</summary>
    [ScriptParam(ScriptParamType.VarString, "Team expression")]
    public string TeamExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets its barrel's model.</summary>
    [ScriptParam(ScriptParamType.String32, "Barrel model")]
    public string BarrelModel { get; set; } = "700_02_1";

    /// <summary>Gets or sets the sound it turns with.</summary>
    [ScriptParam(ScriptParamType.String32, "Rotationsound")]
    public string RotationSound { get; set; } = string.Empty;

    /// <summary>Gets or sets its lowest tilt, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "Min Alpha (deg)")]
    public float MinAlpha { get; set; } = -90f;

    /// <summary>Gets or sets its highest tilt, in degrees.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max Alpha (deg)")]
    public float MaxAlpha { get; set; } = -90f;

    /// <summary>Gets or sets the most it turns either way, in degrees; past a full turn it turns freely.</summary>
    [ScriptParam(ScriptParamType.Real32, "Max Gamma (deg)")]
    public float MaxGamma { get; set; } = 300f;

    /// <summary>Gets or sets its ammunition, which its weapon never runs out of.</summary>
    [ScriptParam(ScriptParamType.Int32, "Ammo")]
    public int Ammo { get; set; } = 10000;

    /// <summary>Gets or sets how fast it tilts, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Alpha speed (deg/sec)")]
    public float AlphaSpeed { get; set; } = 300f;

    /// <summary>Gets or sets how fast it turns, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Gamma speed (deg/sec)")]
    public float GammaSpeed { get; set; } = 300f;

    /// <summary>Gets or sets how fast its barrel spins while it fires, in degrees a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Beta speed (deg/sec)")]
    public float BetaSpeed { get; set; } = 1000f;

    /// <summary>Gets or sets how likely its rounds are to strike, nought to one.</summary>
    [ScriptParam(ScriptParamType.Real32, "Accuracy (0..1)")]
    public float Accuracy { get; set; } = 0.3f;

    /// <summary>Gets or sets its weapon, a <c>WEAPON_ID_*</c> constant's name.</summary>
    [ScriptParam(ScriptParamType.String32, "WeaponID")]
    public string WeaponId { get; set; } = "WEAPON_ID_M2HB";

    /// <summary>Gets a value indicating whether it watches, <c>isOn</c>.</summary>
    public bool IsOn => _isOn != 0;

    /// <summary>Gets its side, as its team expression last said.</summary>
    public int Team => _team;

    /// <summary>Gets its detection, once prepared.</summary>
    public Igi2AiDetection? Detection => _detection;

    /// <summary>Gets the view its detection looks through.</summary>
    public Igi2DetectionView View { get; } = new();

    /// <summary>Gets its weapon, once prepared.</summary>
    public Igi2WeaponFiring? Firing => _firing;

    /// <summary>Gets the barrel's tilt, in radians.</summary>
    public float Tilt => _tilt;

    /// <summary>Gets the head's turn, in radians.</summary>
    public float Turn => _turn;

    /// <summary>Gets how far its barrel has spun, in radians.</summary>
    public float Spin { get; private set; }

    /// <summary>Gets a value indicating whether it saw a target at its last run.</summary>
    public bool HasTarget => _wasFound;

    /// <summary>Gets a value indicating whether it was on target at its last run.</summary>
    public bool IsOnTarget => _wasOnTarget;

    /// <summary>Gets the head's model, the gun's attach point's, or <see langword="null"/> when its holder carries none.</summary>
    public string? HeadModel => _headModel;

    /// <summary>Gets or sets the level's models.</summary>
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets the level's sounds.</summary>
    public Igi2Sounds? Sounds { get; set; }

    /// <inheritdoc/>
    public int Ticks => _ticks;

    /// <inheritdoc/>
    public Igi2HumanView? ZoomView => null;

    /// <inheritdoc/>
    public IgiRandom Random { get; set; } = new();

    /// <inheritdoc/>
    public bool IsPlayer => false;

    /// <inheritdoc/>
    public int GrenadeHeldTime => 0;

    /// <inheritdoc/>
    public Vector3f AccumulatedSpeed => default;

    /// <inheritdoc/>
    int IIgi2WeaponOwner.Team => _detection?.Team ?? _team;

    /// <inheritdoc/>
    public bool IsZoomMode => false;

    /// <inheritdoc/>
    public bool HasFireFrame => true;

    /// <summary>Gets the gun's head frame, row by row: its attach point's, turned.</summary>
    public IReadOnlyList<float> HeadFrame => _headFrame;

    /// <summary>Gets the barrel's frame, row by row.</summary>
    public IReadOnlyList<float> BarrelFrame => _barrelFrame;

    /// <summary>Gets where the barrel is mounted: the head's first magic vertex.</summary>
    public Vector3d BarrelPosition => _barrelPosition;

    /// <summary>Gets the muzzle: the barrel's third magic vertex, <c>AIStationaryGun_GetMuzzlePosition</c>.</summary>
    public Vector3d Muzzle => Models?.Invoke(BarrelModel) is { } barrel
        ? Igi2MagicVertex.Position(barrel, 2, _barrelFrame, _barrelPosition)
        : _barrelPosition;

    /// <summary>
    /// Arms it and readies its detection, as the level loads: <c>AIStationaryGunHolder_UpdateInternalDataHandler</c>, and the gun's
    /// <c>AIStationaryGun_CreateHandler</c> and <c>AIStationaryGun_UpdateInternalDataHandler</c>.
    /// </summary>
    /// <param name="weapons">The session's weapons.</param>
    /// <param name="models">The level's models.</param>
    /// <param name="magic">The magic object types its attach point is looked up in.</param>
    /// <param name="detection">The detectables it watches, or <see langword="null"/> for none.</param>
    public void Prepare(Igi2HumanWeapons weapons, Func<string, MefModel?> models, Igi2MagicObjTypes? magic, Igi2DetectionWorld? detection)
    {
        ArgumentNullException.ThrowIfNull(weapons);
        ArgumentNullException.ThrowIfNull(models);
        Models = models;
        _detectionWorld = detection;
        _detection = detection is null ? null : NewDetection(detection);
        View.ConeAlpha = ViewconeAlpha * DegreesToRadians;
        View.ConeGamma = ViewconeGamma * DegreesToRadians;
        View.ConeLength = ViewconeLength * 4096.0;
        View.IsSecondaryViewUsed = false;

        Igi2Pose holder = Igi2Pose.FromAngles(Position, Orientation);
        _base = null;
        _headModel = null;
        if (magic is not null && models(HolderModel) is { } model)
        {
            foreach (MefAttachment attachment in model.Attachments)
            {
                if (magic.Find(attachment.Name) is { TaskType: GunType } gun)
                {
                    _base = holder.Attach(attachment);
                    _headModel = gun.Model;
                    break;
                }
            }
        }

        _minAlpha = ((Fpu)MinAlpha * DegreesToRadians).F;
        _maxAlpha = ((Fpu)MaxAlpha * DegreesToRadians).F;
        _maxGamma = ((Fpu)MaxGamma * DegreesToRadians).F;
        _alphaSpeed = ((Fpu)AlphaSpeed * DegreesToRadians * TickSeconds).F;
        _gammaSpeed = ((Fpu)GammaSpeed * DegreesToRadians * TickSeconds).F;
        _betaSpeed = ((Fpu)BetaSpeed * DegreesToRadians * TickSeconds).F;
        (_tilt, _turn, _rememberedTilt, _rememberedTurn) = (0f, 0f, 0f, 0f);
        (_rememberTimer, _wasFound, _wasOnTarget, Spin) = (0, false, false, 0f);

        Igi2Inventory inventory = weapons.CreateInventory([]);
        _firing = new Igi2WeaponFiring(inventory, inventory.Tables, weapons.FireTaskTypes, Igi2FireHandlers.Create(inventory.Tables))
        {
            Owner = this,
            IsUnlimitedAmmo = true,
            HitProbability = Math.Clamp(Accuracy, 0f, 1f),
        };
        inventory.EnableWeaponType(WeaponId, addAmmo: true);
        PlaceGun();
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _ticks = level.LevelFlow?.Tick ?? _ticks + 1;
        bool wasOn = _isOn != 0;
        _teamExpression ??= new Igi2LevelExpression(TeamExpression, Name, level.Natives);
        if (!_teamExpression.IsEmpty)
        {
            _team = (int)Igi2LevelExpression.Truncate(level.Evaluate(_teamExpression));
        }

        _onExpression ??= new Igi2LevelExpression(OnExpression, Name, level.Natives);
        if (!_onExpression.IsEmpty)
        {
            _isOn = (byte)Igi2LevelExpression.Truncate(level.Evaluate(_onExpression));
        }

        if (_isOn != 0)
        {
            if (_detection is { } detection)
            {
                View.Position = Muzzle;
                View.Orientation = [.. _barrelFrame];
                detection.Run(View, null, _team);
            }
        }
        else if (wasOn && _detectionWorld is { } world)
        {
            _detection?.Clear();
            _detection = NewDetection(world);
        }

        level.RunChildren(this);
        RunGun();
    }

    /// <inheritdoc/>
    /// <remarks><c>AIStationaryGun_GetParentParametersHandler</c>: from the muzzle, along the barrel.</remarks>
    public Igi2FireFrame GetFireFrame(bool addInaccuracy) => new(Muzzle, [.. _barrelFrame]);

    /// <inheritdoc/>
    public int WeaponObjectCount() => Models?.Invoke(BarrelModel) is null ? 0 : 1;

    /// <inheritdoc/>
    public void Flash(Vector3f flash)
    {
    }

    /// <inheritdoc/>
    public void IgnoreLightOnVisibility(int ticks)
    {
    }

    /// <inheritdoc/>
    public void CountShots(int shots)
    {
    }

    /// <inheritdoc/>
    /// <remarks>The gun's head, its holder's attach point, turned with the gun; its barrel, tilted and turned on the head.</remarks>
    public bool TryMovePart(WorldPlacement placement, out Vector3d positionMetres, out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        orientation = placement.Orientation;
        positionMetres = default;
        if (_base is null)
        {
            return false;
        }

        if (_headModel is not null && string.Equals(placement.Instance.Name, _headModel, StringComparison.Ordinal))
        {
            positionMetres = placement.Position;
            orientation = Rows(_headFrame);
            return true;
        }

        if (string.Equals(placement.Instance.Name, BarrelModel, StringComparison.Ordinal))
        {
            positionMetres = WorldScene.ToMetres(_barrelPosition);
            orientation = Rows(_barrelFrame);
            return true;
        }

        return false;
    }

    // AIDetection_InitDetection(.., 1, 4, Human_bAIDetectionQTaskMask, 4, 8): its own side one, enemies alive, humans, a cone every
    // fourth tick and a look every eighth.
    private static Igi2AiDetection NewDetection(Igi2DetectionWorld world) =>
        new(world, 1, Igi2AiDetection.LiveEnemies, Igi2DetectableKind.Human, 4, 8);

    // AIStationaryGun_RunHandler.
    private void RunGun()
    {
        if (_detection is not { } detection || _firing is not { } firing || _base is null)
        {
            return;
        }

        firing.RunGunShot();
        float aimTilt = 0f, aimTurn = 0f;
        bool found = false;
        Vector3d from = _base.Position;
        float[] b = _base.ToArray();
        foreach (Igi2DetectionItem item in detection.Enemies)
        {
            if (!item.FullVisible[0] && !item.FullVisible[1] && !item.FullVisible[2])
            {
                continue;
            }

            Vector3d at = item.Target is Igi2Human human
                ? human.BonePosition(Animation.Igi2BoneBranches.ShouldersBone)
                : item.Target.DetectablePosition;
            double dx = at.X - from.X, dy = at.Y - from.Y, dz = at.Z - from.Z;
            double lx = (b[6] * dz) + (b[3] * dy) + (b[0] * dx);
            double ly = (b[7] * dz) + (b[4] * dy) + (b[1] * dx);
            double lz = (b[8] * dz) + (b[5] * dy) + (b[2] * dx);
            aimTurn = Igi2Pose.CalcGamma(lx, ly);
            double length = Math.Sqrt((ly * ly) + (lz * lz) + (lx * lx));
            aimTilt = length <= 0.10000000149011612 ? 0f : (float)Math.Asin(Math.Clamp((float)(lz / length), -1.0, 1.0));
            found = true;
            break;
        }

        if (_wasFound && !found)
        {
            _rememberTimer = RememberTicks;
        }
        else if (found)
        {
            _rememberTimer = 0;
        }

        if (_rememberTimer != 0)
        {
            aimTilt = _rememberedTilt;
            aimTurn = _rememberedTurn;
            _rememberTimer--;
        }
        else
        {
            _rememberedTilt = aimTilt;
            _rememberedTurn = aimTurn;
        }

        if (_maxGamma > 6.2831854820251465 && Math.Abs(aimTurn - _turn) > 3.1415927410125732)
        {
            aimTurn = ((Fpu)aimTurn + 6.2831854820251465).F;
        }

        _tilt = ((Fpu)Math.Clamp(((Fpu)aimTilt - _tilt).F, -_alphaSpeed, _alphaSpeed) + _tilt).F;
        float turn = ((Fpu)Math.Clamp(((Fpu)aimTurn - _turn).F, -_gammaSpeed, _gammaSpeed) + _turn).F;
        if (turn > 3.1415927410125732)
        {
            turn = (((Fpu)turn - 3.1415927410125732) - 3.1415927410125732).F;
        }
        else if (turn < -3.1415927410125732)
        {
            turn = (((Fpu)turn + 3.1415927410125732) + 3.1415927410125732).F;
        }

        _turn = turn;
        LimitRotation();
        double tiltOff = Math.Abs(_tilt - aimTilt), turnOff = Math.Abs(_turn - aimTurn);
        PlaceGun();

        bool onTarget = false;
        if (found)
        {
            onTarget = tiltOff < OnTarget && turnOff < OnTarget;
            if (firing.Inventory.Status == Igi2WeaponStatus.Empty)
            {
                firing.Reload();
            }
            else if (onTarget)
            {
                firing.Fire(0);
                Spin = ((Fpu)_betaSpeed + Spin).F;
            }
        }

        if ((_wasOnTarget && !onTarget) || firing.Inventory.Status == Igi2WeaponStatus.Locked)
        {
            firing.FireTypeState(0).IsReleaseFire = true;
            firing.Inventory.Status = Igi2WeaponStatus.Ready;
        }

        float heldTilt = aimTilt < _minAlpha ? _minAlpha : aimTilt > _maxAlpha ? _maxAlpha : aimTilt;
        float heldTurn = aimTurn < -_maxGamma ? -_maxGamma : aimTurn > _maxGamma ? _maxGamma : aimTurn;
        if (Math.Abs(_tilt - heldTilt) > Turning || Math.Abs(_turn - heldTurn) > Turning)
        {
            if (_rotationSound is null && RotationSound.Length > 0)
            {
                _rotationSound = Sounds?.New(RotationSound, from, () => _base?.Position ?? from);
            }
        }
        else
        {
            _rotationSound?.Delete();
            _rotationSound = null;
        }

        firing.UpdateStatus();
        _wasFound = found;
        _wasOnTarget = onTarget;
        firing.RunDelayed();
    }

    // AIStationaryGun_LimitRotation: the tilt held between its lowest and highest, the turn within its limit unless it turns freely.
    private void LimitRotation()
    {
        if (_minAlpha > _tilt)
        {
            _tilt = _minAlpha;
        }
        else if (_maxAlpha < _tilt)
        {
            _tilt = _maxAlpha;
        }

        if (_maxGamma < 6.251769410604253)
        {
            if (-_maxGamma > _turn)
            {
                _turn = -_maxGamma;
            }
            else if (_maxGamma < _turn)
            {
                _turn = _maxGamma;
            }
        }
    }

    // AIStationaryGun_SetGunPosAndOrientation: the head turned on its attach point by the turn, and the barrel by the tilt and the turn,
    // mounted on the head's first magic vertex.
    private void PlaceGun()
    {
        if (_base is null)
        {
            return;
        }

        float[] b = _base.ToArray();
        _headFrame = Multiply(b, Igi2Pose.Rotation(0f, 0f, _turn));
        _barrelFrame = Multiply(b, Igi2Pose.Rotation(_tilt, 0f, _turn));
        _barrelPosition = Igi2MagicVertex.Position(_headModel is null ? null : Models?.Invoke(_headModel), 0, _headFrame, _base.Position);
    }

    // The base times a turn, row by column, each element summed in the order the original sums it: third, first, second.
    private static float[] Multiply(float[] b, float[] r)
    {
        float[] m = new float[9];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                m[(3 * row) + column] =
                    (((Fpu)r[6 + column] * b[(3 * row) + 2]) + ((Fpu)r[column] * b[3 * row]) + ((Fpu)r[3 + column] * b[(3 * row) + 1])).F;
            }
        }

        return m;
    }

    private static Matrix3f Rows(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
}
