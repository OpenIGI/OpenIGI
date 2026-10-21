using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// What an automatic gun's carrier tells it of itself, <c>AIStationaryGun_eGetGunParametersEvent</c>: an <c>AIStationaryGunHolder</c>'s
/// parameters, or a <c>Car</c>'s.
/// </summary>
/// <param name="BarrelModel">Its barrel's model.</param>
/// <param name="RotationSound">The sound it turns with.</param>
/// <param name="MinAlpha">Its lowest tilt, in degrees.</param>
/// <param name="MaxAlpha">Its highest tilt, in degrees.</param>
/// <param name="MaxGamma">The most it turns either way, in degrees; past a full turn it turns freely.</param>
/// <param name="AlphaSpeed">How fast it tilts, in degrees a second.</param>
/// <param name="GammaSpeed">How fast it turns, in degrees a second.</param>
/// <param name="BetaSpeed">How fast its barrel spins while it fires, in degrees a second.</param>
/// <param name="Accuracy">How likely its rounds are to strike, nought to one.</param>
/// <param name="WeaponId">Its weapon, a <c>WEAPON_ID_*</c> constant's name.</param>
public sealed record Igi2AiGunParameters(
    string BarrelModel,
    string RotationSound,
    float MinAlpha,
    float MaxAlpha,
    float MaxGamma,
    float AlphaSpeed,
    float GammaSpeed,
    float BetaSpeed,
    float Accuracy,
    string WeaponId);

/// <summary>
/// An automatic gun, I.G.I. 2's <c>AIStationaryGun</c> (aistationarygun.c): a magic object on its carrier's attach point, whose head turns
/// and whose barrel tilts towards the first enemy its carrier's detection sees, firing once it is on target.
/// </summary>
/// <remarks>
/// <para>
/// <c>AIStationaryGun_RunHandler</c>: the first enemy listed with any point in plain sight is the target, its shoulders for a human,
/// taken into the gun's frame: its bearing and its elevation are where to aim. A target lost is aimed at where it was for ninety ticks
/// more; with none, the gun comes back to nought. Wholly turning guns go the short way round. The tilt and turn each move at most their
/// speed a tick towards the aim, the turn kept within a half turn either way, then held within the gun's limits. With a target, within
/// five degrees on both is on target: an empty weapon reloads, and on target it fires and its barrel spins. A trigger held as it comes
/// off target, or a burst done, is let go. While the aim, held within the limits, is more than two degrees away, its rotation sound
/// plays.
/// </para>
/// <para>
/// Its carrier places it, <c>AIStationaryGun_UpdateAttachmentHandler</c>: a holder once, a car each time it moves.
/// </para>
/// </remarks>
public sealed class Igi2AiStationaryGun : IIgi2WeaponOwner
{
    /// <summary>The magic task type the gun's attach point is.</summary>
    public const string TypeName = "AIStationaryGun";

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

    private readonly Igi2AiGunParameters _parameters;
    private readonly Func<string, MefModel?> _models;
    private readonly string? _headModel;
    private readonly float _minAlpha;
    private readonly float _maxAlpha;
    private readonly float _maxGamma;
    private readonly float _alphaSpeed;
    private readonly float _gammaSpeed;
    private readonly float _betaSpeed;
    private readonly Igi2WeaponFiring _firing;
    private Igi2Pose? _base;
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
    private int _team = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="Igi2AiStationaryGun"/> class, armed: <c>AIStationaryGun_CreateHandler</c> and
    /// <c>AIStationaryGun_UpdateInternalDataHandler</c>, its weapon never running out.
    /// </summary>
    /// <param name="parameters">What its carrier tells it.</param>
    /// <param name="headModel">Its head's model, its attach point's, or <see langword="null"/> for none.</param>
    /// <param name="weapons">The session's weapons.</param>
    /// <param name="models">The level's models.</param>
    public Igi2AiStationaryGun(Igi2AiGunParameters parameters, string? headModel, Igi2HumanWeapons weapons, Func<string, MefModel?> models)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(weapons);
        ArgumentNullException.ThrowIfNull(models);
        _parameters = parameters;
        _headModel = headModel;
        _models = models;
        _minAlpha = ((Fpu)parameters.MinAlpha * DegreesToRadians).F;
        _maxAlpha = ((Fpu)parameters.MaxAlpha * DegreesToRadians).F;
        _maxGamma = ((Fpu)parameters.MaxGamma * DegreesToRadians).F;
        _alphaSpeed = ((Fpu)parameters.AlphaSpeed * DegreesToRadians * TickSeconds).F;
        _gammaSpeed = ((Fpu)parameters.GammaSpeed * DegreesToRadians * TickSeconds).F;
        _betaSpeed = ((Fpu)parameters.BetaSpeed * DegreesToRadians * TickSeconds).F;
        Igi2Inventory inventory = weapons.CreateInventory([]);
        _firing = new Igi2WeaponFiring(inventory, inventory.Tables, weapons.FireTaskTypes, Igi2FireHandlers.Create(inventory.Tables))
        {
            Owner = this,
            IsUnlimitedAmmo = true,
            HitProbability = Math.Clamp(parameters.Accuracy, 0f, 1f),
        };
        inventory.EnableWeaponType(parameters.WeaponId, addAmmo: true);
    }

    /// <summary>Gets what its carrier told it.</summary>
    public Igi2AiGunParameters Parameters => _parameters;

    /// <summary>Gets its weapon.</summary>
    public Igi2WeaponFiring Firing => _firing;

    /// <summary>
    /// Gets or sets what carries it, whose own body its bullets pass through: the shot's ignored task, which
    /// <c>AIStationaryGun_CreateHandler</c> sets to the carrier it is told of.
    /// </summary>
    public object? Holder
    {
        get => _firing.GunShot.Ignore;
        set => _firing.GunShot.Ignore = value;
    }

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

    /// <summary>Gets the head's model, the gun's attach point's, or <see langword="null"/> when its carrier carries none.</summary>
    public string? HeadModel => _headModel;

    /// <summary>Gets a value indicating whether it is mounted on anything.</summary>
    public bool IsMounted => _base is not null;

    /// <summary>Gets or sets the level's sounds.</summary>
    public Igi2Sounds? Sounds { get; set; }

    /// <summary>Gets the gun's head frame, row by row: its attach point's, turned.</summary>
    public IReadOnlyList<float> HeadFrame => _headFrame;

    /// <summary>Gets the barrel's frame, row by row.</summary>
    public IReadOnlyList<float> BarrelFrame => _barrelFrame;

    /// <summary>Gets where the head is mounted: its attach point.</summary>
    public Vector3d HeadPosition => _base?.Position ?? default;

    /// <summary>Gets where the barrel is mounted: the head's first magic vertex.</summary>
    public Vector3d BarrelPosition => _barrelPosition;

    /// <summary>Gets the muzzle: the barrel's third magic vertex, <c>AIStationaryGun_GetMuzzlePosition</c>.</summary>
    public Vector3d Muzzle => _models(_parameters.BarrelModel) is { } barrel
        ? Igi2MagicVertex.Position(barrel, 2, _barrelFrame, _barrelPosition)
        : _barrelPosition;

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
    public int Team => _team;

    /// <inheritdoc/>
    public bool IsZoomMode => false;

    /// <inheritdoc/>
    public bool HasFireFrame => true;

    /// <summary>
    /// Mounts it where its carrier's attach point now is, <c>AIStationaryGun_UpdateAttachmentHandler</c>, and places its head and
    /// barrel there as they are turned.
    /// </summary>
    /// <param name="attachPoint">The attach point's pose.</param>
    public void Mount(Igi2Pose attachPoint)
    {
        ArgumentNullException.ThrowIfNull(attachPoint);
        _base = attachPoint;
        PlaceGun();
    }

    /// <summary>Runs it a tick, <c>AIStationaryGun_RunHandler</c>, against its carrier's detection.</summary>
    /// <param name="detection">Its carrier's detection, <c>AIDetection_eGetAIDetectionEvent</c>; <see langword="null"/> for none.</param>
    /// <param name="ticks">The level's tick.</param>
    public void Run(Igi2AiDetection? detection, int ticks)
    {
        _ticks = ticks;
        if (detection is null || _base is null)
        {
            return;
        }

        _team = detection.Team;
        _firing.RunGunShot();
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
            if (_firing.Inventory.Status == Igi2WeaponStatus.Empty)
            {
                _firing.Reload();
            }
            else if (onTarget)
            {
                _firing.Fire(0);
                Spin = ((Fpu)_betaSpeed + Spin).F;
            }
        }

        if ((_wasOnTarget && !onTarget) || _firing.Inventory.Status == Igi2WeaponStatus.Locked)
        {
            _firing.FireTypeState(0).IsReleaseFire = true;
            _firing.Inventory.Status = Igi2WeaponStatus.Ready;
        }

        float heldTilt = aimTilt < _minAlpha ? _minAlpha : aimTilt > _maxAlpha ? _maxAlpha : aimTilt;
        float heldTurn = aimTurn < -_maxGamma ? -_maxGamma : aimTurn > _maxGamma ? _maxGamma : aimTurn;
        if (Math.Abs(_tilt - heldTilt) > Turning || Math.Abs(_turn - heldTurn) > Turning)
        {
            if (_rotationSound is null && _parameters.RotationSound.Length > 0)
            {
                _rotationSound = Sounds?.New(_parameters.RotationSound, from, () => _base?.Position ?? from);
            }
        }
        else
        {
            StopSound();
        }

        _firing.UpdateStatus();
        _wasFound = found;
        _wasOnTarget = onTarget;
        _firing.RunDelayed();
    }

    /// <summary>Stops its rotation sound, as it is deleted.</summary>
    public void StopSound()
    {
        _rotationSound?.Delete();
        _rotationSound = null;
    }

    /// <inheritdoc/>
    /// <remarks><c>AIStationaryGun_GetParentParametersHandler</c>: from the muzzle, along the barrel.</remarks>
    public Igi2FireFrame GetFireFrame(bool addInaccuracy) => new(Muzzle, [.. _barrelFrame]);

    /// <inheritdoc/>
    public int WeaponObjectCount() => _models(_parameters.BarrelModel) is null ? 0 : 1;

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

    /// <summary>Gets where its head or its barrel is drawn now.</summary>
    /// <param name="placement">The part as the walk placed it.</param>
    /// <param name="positionMetres">Receives where it is drawn, in metres.</param>
    /// <param name="orientation">Receives its orientation.</param>
    /// <returns>Whether the part is its head or its barrel.</returns>
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
            positionMetres = WorldScene.ToMetres(_base.Position);
            orientation = Rows(_headFrame);
            return true;
        }

        if (string.Equals(placement.Instance.Name, _parameters.BarrelModel, StringComparison.Ordinal))
        {
            positionMetres = WorldScene.ToMetres(_barrelPosition);
            orientation = Rows(_barrelFrame);
            return true;
        }

        return false;
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
        _barrelPosition = Igi2MagicVertex.Position(_headModel is null ? null : _models(_headModel), 0, _headFrame, _base.Position);
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
