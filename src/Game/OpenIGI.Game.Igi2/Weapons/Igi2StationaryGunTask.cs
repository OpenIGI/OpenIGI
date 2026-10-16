using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>Who works a mounted gun: the channels it aims and fires it by, and what its weapon asks of it.</summary>
public interface IIgi2GunOperator
{
    /// <summary>Gets the turn across this tick, channel nought (<c>avChannel[0]</c>).</summary>
    float LookAcross { get; }

    /// <summary>Gets the turn up this tick, channel one.</summary>
    float LookUp { get; }

    /// <summary>Gets the fire channel this tick, channel six.</summary>
    float FireChannel { get; }

    /// <summary>Gets the fire channel last tick (<c>avLastChannel[6]</c>).</summary>
    float LastFireChannel { get; }

    /// <summary>Gets its team, <c>eTeam</c>, which the gun's weapon fires for.</summary>
    int Team { get; }

    /// <summary>Gets a value indicating whether it is the player, whose view the gun seats behind it.</summary>
    bool IsPlayer { get; }

    /// <summary>Gets its view, which the gun overrides with its seat while the player works it.</summary>
    Igi2HumanView View { get; }

    /// <summary>Gets the game's run stream, which the player's aim at the gun is shaken by.</summary>
    IgiRandom Random { get; }

    /// <summary>
    /// Gets which way its AI aims, row by row (<c>Human+0x7A0</c>'s <c>+160</c>), or <see langword="null"/> for an operator with no AI,
    /// whose shots the gun's kick throws off.
    /// </summary>
    IReadOnlyList<float>? AimFrame { get; }

    /// <summary>Counts a tick of firing towards its accuracy, <c>tMissionScoreData.nShots</c>.</summary>
    void CountShot();
}

/// <summary>
/// A mounted gun, I.G.I. 2's <c>StationaryGun</c> (stationarygun.c): a holder that turns about its mount, a barrel of its weapon's fixed
/// model that tilts, and a <c>Weapon</c> of its own with a load of ammunition, worked by whoever activates it from behind.
/// </summary>
/// <remarks>
/// <para>
/// <c>StationaryGun_UpdateInternalDataHandler</c>: its weapon is its <c>Weapon ID</c>, with ammunition; an <c>Ammo</c> of minus one
/// never runs out, otherwise it carries that much and no more, and is loaded.
/// </para>
/// <para>
/// <c>StationaryGun_RunHandler</c>, once a tick: its precision eases two fifths of the way towards its weapon's standing precision;
/// its kick dies away by the weapon's damping. Worked, its tilt and turn follow the operator's look channels, a half turn a unit, held
/// within its limits; the fire channel fires its weapon, and its letting go lets the trigger go; the kick throws an operator without an
/// AI off; a tenth of the kick is shed. Not worked, the barrel droops towards nine tenths of its lowest tilt five degrees a tick. Then
/// the holder is turned and the barrel placed (<c>StationaryGun_PositionWeapon</c>), and the weapon's status settled.
/// </para>
/// <para>
/// <c>StationaryGun_Fire</c>: a shot fired this tick adds two fifths of the weapon's kick, in degrees, to the precision, and the kick,
/// in radians, to the gun's.
/// </para>
/// </remarks>
public sealed partial class Igi2StationaryGunTask : Igi2Task, IIgi2PlacedTask, IIgi2Activatable, IIgi2WeaponOwner, IIgi2AiMountedGun, IIgi2Mover, IIgi2AttachParent
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "StationaryGun";

    /// <summary>A degree in radians, as the original's single-precision constant.</summary>
    public const double DegreesToRadians = 0.01745329238474369;

    /// <summary>How far the barrel droops a tick while nobody works it, five degrees.</summary>
    public const double DroopStep = 0.0872664675116539;

    /// <summary>The bit of <see cref="LimitFlags"/> set when the tilt was held at a limit.</summary>
    public const int TiltLimited = 1;

    /// <summary>The bit of <see cref="LimitFlags"/> set when the turn was held at a limit.</summary>
    public const int TurnLimited = 2;

    private float[] _paramFrame = Igi2Pose.Rotation(0f, 0f, 0f);
    private float[] _modelFrame = Igi2Pose.Rotation(0f, 0f, 0f);
    private float[] _lastModelFrame = Igi2Pose.Rotation(0f, 0f, 0f);
    private float[] _gunFrame = Igi2Pose.Rotation(0f, 0f, 0f);
    private Vector3d _mount;
    private float _tilt;
    private float _turn;
    private float _finalTilt;
    private float _finalTurn;
    private float _tiltOffset;
    private float _turnOffset;
    private float _kickTilt;
    private float _kickTurn;
    private float _kickDamping = 1f;
    private float _precision;
    private int _runTick = -1;
    private int _movedTick = int.MinValue;
    private int _team = -1;
    private bool _isActivated;
    private Igi2WeaponFiring? _firing;
    private IReadOnlyList<Igi2RigidObject> _solids = [];

    /// <summary>Gets or sets where it stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its turn, as three Euler angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets its holder's model.</summary>
    [ScriptParam(ScriptParamType.String32, "Holder model")]
    public string HolderModel { get; set; } = string.Empty;

    /// <summary>Gets or sets its weapon, a <c>WEAPON_ID_*</c> constant's name.</summary>
    [ScriptParam(ScriptParamType.String32, "Weapon ID")]
    public string WeaponId { get; set; } = "WEAPON_ID_M2HB";

    /// <summary>Gets or sets the most its barrel tilts up, in degrees.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Max up angle")]
    public float MaxUpAngle { get; set; } = 25f;

    /// <summary>Gets or sets the most its barrel tilts down, in degrees, as a negative angle.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Max down angle")]
    public float MaxDownAngle { get; set; } = -25f;

    /// <summary>Gets or sets the most it turns to either side, in degrees.</summary>
    [ScriptParam(ScriptParamType.Degrees, "Max sideways angle")]
    public float MaxSidewaysAngle { get; set; } = 90f;

    /// <summary>Gets or sets the ammunition it carries, or minus one for no end of it.</summary>
    [ScriptParam(ScriptParamType.Int32, "Ammo")]
    public int Ammo { get; set; } = -1;

    /// <summary>Gets the barrel's tilt, as the operator steers it, in radians.</summary>
    public float Tilt => _tilt;

    /// <summary>Gets the holder's turn, as the operator steers it, in radians.</summary>
    public float Turn => _turn;

    /// <summary>Gets the barrel's tilt as drawn, the kick added.</summary>
    public float FinalTilt => _finalTilt;

    /// <summary>Gets the holder's turn as drawn, the kick added.</summary>
    public float FinalTurn => _finalTurn;

    /// <summary>Gets which limits held the aim this tick: <see cref="TiltLimited"/>, <see cref="TurnLimited"/>.</summary>
    public int LimitFlags { get; private set; }

    /// <summary>Gets its precision, in degrees, which the crosshair shows and a player's aim at it is shaken by.</summary>
    public float Precision => _precision;

    /// <summary>Gets where the barrel is mounted, its holder's magic vertex.</summary>
    public Vector3d Mount => _mount;

    /// <summary>Gets the barrel's frame, row by row.</summary>
    public IReadOnlyList<float> GunFrame => _gunFrame;

    /// <summary>Gets the holder's frame, row by row: its authored turn, turned about its mount.</summary>
    public IReadOnlyList<float> ModelFrame => _modelFrame;

    /// <summary>Gets the barrel's model, its weapon's fixed model, or <see langword="null"/> when there is none.</summary>
    public string? BarrelModel { get; private set; }

    /// <summary>Gets who works it, or <see langword="null"/>.</summary>
    public IIgi2GunOperator? Operator { get; private set; }

    /// <summary>Gets a value indicating whether it has been activated and not let go, <c>isActivated</c>.</summary>
    public bool IsActivated => _isActivated;

    /// <summary>Gets its weapon, once prepared.</summary>
    public Igi2WeaponFiring? Firing => _firing;

    /// <summary>Gets or sets the level's models.</summary>
    public Func<string, MefModel?>? Models { get; set; }

    /// <summary>Gets or sets the magic object types its holder's attach points are looked up in.</summary>
    public Igi2MagicObjTypes? Magic { get; set; }

    /// <inheritdoc cref="IIgi2WeaponOwner.Ticks"/>
    public int Ticks { get; private set; }

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

    /// <inheritdoc/>
    public Vector3d Centre => Position;

    /// <inheritdoc/>
    public float Radius => Models?.Invoke(HolderModel)?.NativeMarkerBound ?? 0f;

    /// <inheritdoc/>
    public Igi2ActivateKind Kind => Igi2ActivateKind.StationaryGun;

    /// <inheritdoc/>
    int IIgi2AiMountedGun.TaskId => Id;

    /// <inheritdoc/>
    Vector3d IIgi2AiMountedGun.Position => _mount;

    /// <inheritdoc/>
    public Vector3f Forward => new(_gunFrame[1], _gunFrame[4], _gunFrame[7]);

    /// <inheritdoc/>
    public bool IsManned => _isActivated;

    /// <inheritdoc/>
    public bool IsWrecked => false;

    /// <inheritdoc/>
    public Igi2SeatView Seat => new(SeatPosition(), [.. _gunFrame]);

    /// <inheritdoc/>
    public Igi2Pose Built => Igi2Pose.FromMatrix(Position, Rows(_paramFrame));

    /// <inheritdoc/>
    public Vector3d CurrentPosition => Position;

    /// <inheritdoc/>
    public IReadOnlyList<float> CurrentOrientation => _modelFrame;

    /// <inheritdoc/>
    /// <remarks>Its operator rides it, and swings round with the holder as the gun turns.</remarks>
    public Vector3d AttachPosition => Position;

    /// <inheritdoc/>
    public IReadOnlyList<float> AttachOrientation => _modelFrame;

    /// <inheritdoc/>
    public Vector3d LastPosition => Position;

    /// <inheritdoc/>
    public IReadOnlyList<float> LastOrientation => _lastModelFrame;

    /// <summary>
    /// Arms it and places its barrel, as the level loads: <c>StationaryGun_CreateHandler</c>'s weapon and
    /// <c>StationaryGun_UpdateInternalDataHandler</c>.
    /// </summary>
    /// <param name="weapons">The session's weapons.</param>
    /// <param name="models">The level's models.</param>
    public void Prepare(Igi2HumanWeapons weapons, Func<string, MefModel?> models)
    {
        ArgumentNullException.ThrowIfNull(weapons);
        ArgumentNullException.ThrowIfNull(models);
        Models = models;
        _paramFrame = Igi2Pose.Rotation(Orientation.X, Orientation.Y, Orientation.Z);
        _modelFrame = [.. _paramFrame];
        _lastModelFrame = [.. _paramFrame];
        (_tilt, _turn, _finalTilt, _finalTurn, _tiltOffset, _turnOffset) = (0f, 0f, 0f, 0f, 0f, 0f);
        _runTick = -1;
        _isActivated = false;

        Igi2Inventory inventory = weapons.CreateInventory([]);
        _firing = new Igi2WeaponFiring(inventory, inventory.Tables, weapons.FireTaskTypes, Igi2FireHandlers.Create(inventory.Tables))
        {
            Owner = this,
        };
        BarrelModel = null;
        if (inventory.FindWeapon(WeaponId) is { } item)
        {
            inventory.EnableWeaponType(item.WeaponType.WeaponId, addAmmo: true);
            _firing.IsUnlimitedAmmo = Ammo == -1;
            if (Ammo != -1 && item.WeaponType.AmmoInfo(0, inventory.Tables).AmmoType is { } ammoType)
            {
                inventory.ClearAll();
                inventory.EnableWeaponType(item.WeaponType.WeaponId, addAmmo: false);
                if (inventory.FindAmmo(ammoType.AmmoId) is { } ammo)
                {
                    ammo.SetLimit(Ammo);
                    ammo.Add(Ammo);
                }

                _firing.Reload();
            }

            BarrelModel = item.WeaponType.FixedModel.Length > 0 && models(item.WeaponType.FixedModel) is not null
                ? item.WeaponType.FixedModel
                : null;
        }

        PositionWeapon();
    }

    /// <summary>Gives it the solids its holder was built as, which turn with it.</summary>
    /// <param name="solids">Its solids, as <see cref="Igi2CollisionWorld.SolidsOf"/> gives them.</param>
    public void Bind(IReadOnlyList<Igi2RigidObject> solids) => _solids = solids ?? throw new ArgumentNullException(nameof(solids));

    /// <inheritdoc/>
    public void EvaluateActivate(Igi2EvaluateActivate context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_isActivated)
        {
            return;
        }

        // StationaryGun_EvaluateActivateHandler: offered from its first magic vertex to a view behind it.
        Vector3d at = Igi2MagicVertex.Position(Models?.Invoke(HolderModel), 0, _modelFrame, Position);
        Vector3d view = context.ViewPosition;
        double ahead = (_modelFrame[7] * (view.Z - at.Z)) + (_modelFrame[4] * (view.Y - at.Y)) + (_modelFrame[1] * (view.X - at.X));
        if (ahead <= 0.0)
        {
            context.Resolve(at, this);
        }
    }

    /// <summary>
    /// A tick of being activated, <c>StationaryGun_ActivateHandler</c>: on the first, whoever activated it works it, and its weapon fires
    /// for their team; it is activated.
    /// </summary>
    /// <param name="activator">Who activates it.</param>
    /// <param name="activateTime">The ticks the activation has run, before this one.</param>
    public void Man(IIgi2GunOperator activator, int activateTime)
    {
        ArgumentNullException.ThrowIfNull(activator);
        if (activateTime == 0)
        {
            Operator = activator;
            _team = activator.Team;
        }

        _isActivated = true;
    }

    /// <summary>
    /// Lets it go, <c>StationaryGun_Deactivate</c>: the operator's view is its own again, nobody works it, its weapon fires for nobody,
    /// and a trigger held is let go.
    /// </summary>
    public void Deactivate()
    {
        IIgi2GunOperator? left = Operator;
        left?.View.ClearOverrideView();
        Operator = null;
        _isActivated = false;
        _team = -1;
        if (left is not null && left.LastFireChannel != 0f)
        {
            LetGo(alsoAuto: true);
        }
    }

    /// <summary>Stops working it for a while without letting it go, <c>StationaryGun_Pause</c>.</summary>
    public void Pause()
    {
        IIgi2GunOperator? left = Operator;
        Operator = null;
        if (left is not null && left.LastFireChannel != 0f)
        {
            LetGo(alsoAuto: true);
        }
    }

    /// <summary>Works it again after a pause, <c>StationaryGun_Unpause</c>.</summary>
    /// <param name="activator">Who works it.</param>
    public void Unpause(IIgi2GunOperator activator) => Operator = activator ?? throw new ArgumentNullException(nameof(activator));

    /// <inheritdoc/>
    public void ReleaseTrigger() => LetGo(alsoAuto: false);

    /// <summary>Gets where the player stands to work it and which way, <c>StationaryGun_GetActivatePosAndOrientation</c>.</summary>
    /// <param name="frame">Receives the holder's frame, row by row.</param>
    /// <returns>Its holder's first magic vertex.</returns>
    public Vector3d ActivatePosition(out IReadOnlyList<float> frame)
    {
        frame = _modelFrame;
        return Igi2MagicVertex.Position(Models?.Invoke(HolderModel), 0, _modelFrame, Position);
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        int tick = level.LevelFlow?.Tick ?? _runTick + 1;
        if (_firing is not { } firing || tick == _runTick)
        {
            return;
        }

        _runTick = tick;
        Ticks = tick;
        firing.RunGunShot();
        float target = firing.Inventory.Current?.WeaponType.StandingPrecisionDegrees ?? 0f;
        _precision = ((((Fpu)target - _precision) * 0.4000000059604645) + _precision).F;
        _kickTilt = ((-(Fpu)_kickTilt * _kickDamping) + _kickTilt).F;
        _kickTurn = ((-(Fpu)_kickTurn * _kickDamping) + _kickTurn).F;
        _tiltOffset = _kickTilt;
        _turnOffset = _kickTurn;

        if (Operator is { } worker)
        {
            bool fire = false;
            _tilt = (((Fpu)worker.LookUp * 3.1415927410125732) + _tilt).F;
            _turn = (((Fpu)worker.LookAcross * -3.1415927410125732) + _turn).F;
            LimitRotation();
            if (worker.FireChannel == 0f)
            {
                if (worker.LastFireChannel != 0f)
                {
                    LetGo(alsoAuto: false);
                }
            }
            else
            {
                fire = true;
                worker.CountShot();
            }

            _finalTilt = _tilt;
            _finalTurn = _turn;
            if (worker.AimFrame is null)
            {
                _finalTilt = ((Fpu)_tilt + _tiltOffset).F;
                _finalTurn = ((Fpu)_turn + _turnOffset).F;
            }

            _tiltOffset = ((Fpu)_tiltOffset * 0.8999999761581421).F;
            _turnOffset = ((Fpu)_turnOffset * 0.8999999761581421).F;
            Fire(fire);
        }
        else
        {
            float rest = ((Fpu)MaxDownAngle * 0.8999999761581421 * DegreesToRadians).F;
            if (rest > _finalTilt)
            {
                _finalTilt = ((Fpu)_finalTilt + DroopStep).F;
                if (_finalTilt > rest)
                {
                    _finalTilt = rest;
                }
            }
            else if (rest < _finalTilt)
            {
                _finalTilt = ((Fpu)_finalTilt - DroopStep).F;
                if (_finalTilt < rest)
                {
                    _finalTilt = rest;
                }
            }

            _tilt = 0f;
            _turn = 0f;
        }

        PositionWeapon(tick);
        firing.UpdateStatus();
        level.RunChildren(this);
        firing.RunDelayed();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>StationaryGun_GetParentPositionHandler</c>: worked by the player, two metres ahead of its seat along the seat's frame, turned
    /// at random by up to its precision; worked by an AI, a metre ahead of the mount along the barrel, as the AI aims.
    /// </remarks>
    public Igi2FireFrame GetFireFrame(bool addInaccuracy)
    {
        if (Operator is { IsPlayer: true } player)
        {
            Matrix3f seat = player.View.OverrideOrientation;
            float[] o = [seat.M11, seat.M12, seat.M13, seat.M21, seat.M22, seat.M23, seat.M31, seat.M32, seat.M33];
            Vector3d eye = player.View.OverridePosition;
            Vector3d from = new(
                ((((Fpu)o[0] + o[2]) * 0.0) + ((Fpu)o[1] * 8192.0)).Value + eye.X,
                ((((Fpu)o[3] + o[5]) * 0.0) + ((Fpu)o[4] * 8192.0)).Value + eye.Y,
                ((((Fpu)o[6] + o[8]) * 0.0) + ((Fpu)o[7] * 8192.0)).Value + eye.Z);
            float far = (float)player.Random.NextDouble();
            float round = ((Fpu)(float)player.Random.NextDouble() * 6.2831854820251465).F;
            float spread = ((Fpu)_precision * DegreesToRadians * far).F;
            float[] n = Igi2Pose.Rotation(spread, round, 0f);
            float[] turned = new float[9];
            for (int c = 0; c < 3; c++)
            {
                turned[c] = ((((Fpu)n[c] * o[0]) + ((Fpu)n[3 + c] * o[1])) + ((Fpu)n[6 + c] * o[2])).F;
                turned[3 + c] = ((((Fpu)n[3 + c] * o[4]) + ((Fpu)n[6 + c] * o[5])) + ((Fpu)n[c] * o[3])).F;
                turned[6 + c] = ((((Fpu)n[3 + c] * o[7]) + ((Fpu)n[6 + c] * o[8])) + ((Fpu)n[c] * o[6])).F;
            }

            return new Igi2FireFrame(from, turned);
        }

        float[] g = _gunFrame;
        Vector3d ahead = new(
            ((((Fpu)g[2] + g[0]) * 0.0) + ((Fpu)g[1] * 4096.0)).Value + _mount.X,
            ((((Fpu)g[5] + g[3]) * 0.0) + ((Fpu)g[4] * 4096.0)).Value + _mount.Y,
            ((((Fpu)g[8] + g[6]) * 0.0) + ((Fpu)g[7] * 4096.0)).Value + _mount.Z);
        return Operator?.AimFrame is { } aim ? new Igi2FireFrame(ahead, [.. aim]) : new Igi2FireFrame(ahead, [.. g]);
    }

    /// <inheritdoc/>
    /// <remarks><c>StationaryGun_GetWeaponObjectHandler</c>: the barrel.</remarks>
    public int WeaponObjectCount() => BarrelModel is null ? 0 : 1;

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
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Holder model" => HolderModel,
            "Position" => Position,
            "Orientation" => Orientation,
            _ => null,
        };
        return value is not null;
    }

    /// <summary>The barrel as drawn: its model at its mount in its frame.</summary>
    /// <returns>The barrel, or <see langword="null"/> without one.</returns>
    public Game.Player.TitleWorldModel? Barrel() =>
        BarrelModel is { } model ? new Game.Player.TitleWorldModel(model, _mount, Rows(_gunFrame)) : null;

    // StationaryGun_Fire.
    private void Fire(bool isFire)
    {
        if (!isFire || _firing is not { } firing)
        {
            return;
        }

        firing.Fire(0);
        if (firing.Inventory.Status == Igi2WeaponStatus.Fire)
        {
            float alpha = firing.AlphaKickback, gamma = firing.GammaKickback;
            _precision = ((((Fpu)alpha + gamma) * 0.4000000059604645) + _precision).F;
            _kickTilt = (((Fpu)alpha * DegreesToRadians) + _kickTilt).F;
            _kickTurn = (((Fpu)gamma * DegreesToRadians) + _kickTurn).F;
            _kickDamping = firing.KickbackDamping;
        }
    }

    // The weapon's trigger let go: the primary's release flagged and the weapon ready, and, when the gun is let go, no more firing by
    // itself.
    private void LetGo(bool alsoAuto)
    {
        if (_firing is not { } firing)
        {
            return;
        }

        firing.FireTypeState(0).IsReleaseFire = true;
        firing.Inventory.Status = Igi2WeaponStatus.Ready;
        if (alsoAuto)
        {
            firing.IsAutoFire = false;
        }
    }

    // StationaryGun_LimitRotation: the tilt held between its lowest and highest, the turn brought within a turn either way and held
    // within its sideways limit, each flag set where it was held.
    private void LimitRotation()
    {
        int flags = 0;
        float down = ((Fpu)MaxDownAngle * DegreesToRadians).F;
        if (down > _tilt)
        {
            _tilt = down;
            flags |= TiltLimited;
        }
        else
        {
            float up = ((Fpu)MaxUpAngle * DegreesToRadians).F;
            if (up < _tilt)
            {
                _tilt = up;
                flags |= TiltLimited;
            }
        }

        if (_turn < -6.283185307179589)
        {
            _turn = ((Fpu)_turn + 6.2831854820251465).F;
        }
        else if (_turn > 6.283185307179589)
        {
            _turn = ((Fpu)_turn - 6.2831854820251465).F;
        }

        float side = ((Fpu)MaxSidewaysAngle * DegreesToRadians).F;
        if (-side > _turn)
        {
            _turn = -side;
            flags |= TurnLimited;
        }
        else if (_turn > side)
        {
            _turn = side;
            flags |= TurnLimited;
        }

        LimitFlags = flags;
    }

    // StationaryGun_PositionWeapon: the holder turned about its mount by the turn, the barrel mounted on its magic vertex and turned by
    // the tilt and the turn, and the operator's eyes seated half a metre behind and a fifth of a metre above the mount.
    private void PositionWeapon(int tick = int.MinValue)
    {
        if (tick != _movedTick)
        {
            _movedTick = tick;
            _lastModelFrame = [.. _modelFrame];
        }

        float[] p = _paramFrame;
        float[] r = Igi2Pose.Rotation(0f, 0f, _finalTurn);
        _modelFrame =
        [
            Sum(p[0], r[0], r[6], p[2], r[3], p[1]), Sum(p[1], r[4], p[0], r[1], r[7], p[2]), Sum(p[1], r[5], p[0], r[2], r[8], p[2]),
            Sum(p[5], r[6], p[3], r[0], r[3], p[4]), Sum(p[4], r[4], p[5], r[7], p[3], r[1]), Sum(p[4], r[5], p[5], r[8], p[3], r[2]),
            Sum(r[6], p[8], r[0], p[6], p[7], r[3]), Sum(r[7], p[8], r[1], p[6], p[7], r[4]), Sum(r[8], p[8], r[2], p[6], p[7], r[5]),
        ];

        MefModel? holder = Models?.Invoke(HolderModel);
        int vertex = holder is { } h && h.TryGetMagicVertex(1, out _) ? 1 : 0;
        _mount = Igi2MagicVertex.Position(holder, vertex, _modelFrame, Position);

        float[] g = Igi2Pose.Rotation(_finalTilt, 0f, _finalTurn);
        _gunFrame =
        [
            Sum(g[6], p[2], p[0], g[0], g[3], p[1]), Sum(p[0], g[1], p[1], g[4], g[7], p[2]), Sum(p[0], g[2], p[1], g[5], g[8], p[2]),
            Sum(g[6], p[5], p[3], g[0], g[3], p[4]), Sum(p[4], g[4], p[5], g[7], p[3], g[1]), Sum(p[4], g[5], p[5], g[8], p[3], g[2]),
            Sum(g[6], p[8], p[7], g[3], g[0], p[6]), Sum(p[7], g[4], g[7], p[8], g[1], p[6]), Sum(p[7], g[5], g[8], p[8], g[2], p[6]),
        ];

        if (Operator is { } worker && BarrelModel is not null)
        {
            worker.View.OverrideView(SeatPosition(), Rows(_gunFrame));
        }

        if (_solids.Count > 0 && Models is { } models)
        {
            Igi2CollisionWorld.Repose(_solids, models, HolderModel, Igi2Pose.FromMatrix(Position, Rows(_modelFrame)), Magic);
        }
    }

    // Half a metre behind and a fifth of a metre above the mount, along the barrel.
    private Vector3d SeatPosition()
    {
        float[] m = _gunFrame;
        return new Vector3d(
            (((Fpu)m[2] * 819.2000122070312) - ((Fpu)m[1] * 2048.0) + ((Fpu)m[0] * 0.0)).Value + _mount.X,
            (((Fpu)m[5] * 819.2000122070312) - ((Fpu)m[4] * 2048.0) + ((Fpu)m[3] * 0.0)).Value + _mount.Y,
            (((Fpu)m[8] * 819.2000122070312) - ((Fpu)m[7] * 2048.0) + ((Fpu)m[6] * 0.0)).Value + _mount.Z);
    }

    // Three products summed in the order the original sums them.
    private static float Sum(float a, float b, float c, float d, float e, float f) =>
        (((Fpu)a * b) + ((Fpu)c * d) + ((Fpu)e * f)).F;

    private static Matrix3f Rows(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
}
