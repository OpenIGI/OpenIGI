using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;
using static OpenIGI.Game.World.CutscenePoses;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.World;

/// <summary>The cutscene vehicles' eyes, guns and death zones: what each sees, how its guns turn and fire, whom it runs over.</summary>
/// <remarks>
/// A vehicle's vision takes its sweep phase from the level's one round-robin counter (<see cref="NextVisionPhase"/>),
/// which the load starts and every later re-initialisation carries on from. Each gun asks the vision what it sees,
/// turns to it (<c>0x46F390</c>) and lets <c>Gun_Fire</c> (<c>0x478BA0</c>) take the trigger.
/// </remarks>
internal sealed class CutsceneVehicleArmament(
    CutsceneContext context,
    List<CarAiState> carAis,
    List<CutsceneAnimationState> animations)
{
    private const string GunMountTaskTypeName = "TASKTYPE_AISTATIONARYGUN";

    private readonly CutsceneContext _context = context;
    private readonly List<CarAiState> _carAis = carAis;
    private readonly List<CutsceneAnimationState> _animations = animations;
    private readonly Dictionary<GameTask, VehicleArmament> _armaments = [];
    private readonly Dictionary<GameTask, VehicleVision> _visions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GameTask, VehicleDeathZoneState> _deathZones = [];
    private readonly Dictionary<WorldPlacement, (GameTask Vehicle, int Index)> _gunPlacements = [];
    private readonly Random _gunRandom = new(0x46F390);
    private int _visionCounter;

    /// <summary>Gets or sets what supplies the living player eye used by firing vehicles.</summary>
    public Func<Vector3d?>? VehicleCombatTarget { get; set; }

    /// <summary>Gets or sets where a vehicle's eye tests the same target; falls back to <see cref="VehicleCombatTarget"/>.</summary>
    public Func<Vector3d?>? VehicleSightTarget { get; set; }

    /// <summary>Gets or sets the test of a firing vehicle's muzzle-to-player sight segment.</summary>
    public Func<GameTask, Vector3d, Vector3d, bool>? VehicleCombatLineOfSight { get; set; }

    /// <summary>Gets or sets what receives a vehicle missile once sight, retention and gun cadence allow it.</summary>
    public Action<VehicleCombatShot>? VehicleCombatShotFired { get; set; }

    /// <summary>Starts the round-robin vision counter where the load's phase assignment left it.</summary>
    public void StartVisionCounter(int counter) => _visionCounter = counter;

    /// <summary>Gives a Car or Heli its vision, with the phase the load assigned it or the counter's next.</summary>
    public void RegisterVision(GameTask task, IReadOnlyDictionary<GameTask, int> visionPhases) =>
        _visions[task] = new VehicleVision(visionPhases.TryGetValue(task, out int phase) ? phase : NextVisionPhase());

    /// <summary>Finds a vehicle's vision, if it has one.</summary>
    public bool TryGetVision(GameTask vehicle, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out VehicleVision? vision) =>
        _visions.TryGetValue(vehicle, out vision);

    /// <summary>Binds one authored firing vehicle to a single retail weapon row, fired from its captured muzzle.</summary>
    /// <remarks>
    /// The Level 6 APC capture's model: the vehicle fires the row's cadence at whatever it retains,
    /// from a fixed muzzle, without turning a gun. Vehicles whose model guns are known use
    /// <see cref="ConfigureVehicleGuns"/> instead.
    /// </remarks>
    public void ConfigureVehicleCombat(GameTask vehicle, WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(weapon);
        _armaments[vehicle] = new VehicleArmament(weapon, []);
    }

    /// <summary>Arms one authored vehicle with the guns its model carries.</summary>
    /// <param name="vehicle">The Car or Heli task.</param>
    /// <param name="guns">Every gun its model's attachments define, each turning and firing on its own.</param>
    /// <remarks>
    /// The vehicle sees; each gun asks it what it sees and turns to it (<c>0x46F390</c>). A tank's
    /// cannon and coaxial gun therefore fire independently at their own rows' cadences.
    /// </remarks>
    public void ConfigureVehicleGuns(GameTask vehicle, IReadOnlyList<VehicleGun> guns)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(guns);
        if (guns.Count == 0)
        {
            _armaments.Remove(vehicle);
            return;
        }

        _armaments[vehicle] = new VehicleArmament(
            null,
            [.. guns.Select(static gun => new VehicleGunState(gun))]);
    }

    /// <summary>Every armed live vehicle's guns, as this tick's updates left them.</summary>
    /// <returns>One entry per gun of an active, undestroyed vehicle.</returns>
    public IEnumerable<VehicleGunSound> VehicleGunSounds()
    {
        foreach ((GameTask vehicle, VehicleArmament armament) in _armaments)
        {
            if (!ConditionalContainerGate.IsTaskActive(vehicle)
                || _context.Destructibles?.IsDestroyed(vehicle) == true
                || !_context.TaskTransforms.TryGetValue(vehicle, out CutsceneTransformState? transform))
            {
                continue;
            }

            for (int index = 0; index < armament.Guns.Count; index++)
            {
                VehicleGunState gun = armament.Guns[index];

                // Gun_Fire plays its reports at the weapon object (+32), where the seat put it.
                yield return new VehicleGunSound(
                    vehicle,
                    index,
                    armament.Seated
                        ? gun.WeaponPosition
                        : transform.Position + transform.Orientation.Transposed().Transform(gun.Gun.Offset),
                    gun.Hold,
                    gun.RoundsFiredThisTick,
                    gun.Turret.Turning,
                    gun.Gun.Weapon,
                    gun.Gun.Mount.TurnSound);
            }
        }
    }

    /// <summary>Gives one authored vehicle the death zones its model carries.</summary>
    /// <param name="vehicle">The Car or Heli task.</param>
    /// <param name="zones">The zones' volumes in the vehicle's frame.</param>
    public void ConfigureDeathZones(GameTask vehicle, IReadOnlyList<VehicleDeathZone> zones)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(zones);
        if (zones.Count == 0)
        {
            _deathZones.Remove(vehicle);
            return;
        }

        _deathZones[vehicle] = new VehicleDeathZoneState(zones);
    }

    /// <summary>Finds a moving vehicle whose death zone a human stands in (<c>0x45FA80</c>).</summary>
    /// <param name="humanOrigin">The human's model origin in world units -- about a metre above the feet.</param>
    /// <param name="vehicle">Receives the vehicle that runs the human over.</param>
    /// <returns>
    /// <see langword="true"/> when the human is inside a zone of a live vehicle that moved at least
    /// <see cref="VehicleDeathZone.LethalSpeedUnitsPerTick"/> on the last tick.
    /// </returns>
    public bool TryRunOver(Vector3d humanOrigin, out GameTask? vehicle)
    {
        foreach ((GameTask candidate, VehicleDeathZoneState state) in _deathZones)
        {
            if (state.SpeedUnitsPerTick < VehicleDeathZone.LethalSpeedUnitsPerTick
                || !ConditionalContainerGate.IsTaskActive(candidate)
                || _context.Destructibles?.IsDestroyed(candidate) == true
                || !_context.TaskTransforms.TryGetValue(candidate, out CutsceneTransformState? transform))
            {
                continue;
            }

            Vector3d local = transform.Orientation.Transform(humanOrigin - transform.Position);
            foreach (VehicleDeathZone zone in state.Zones)
            {
                if (zone.Contains(local))
                {
                    vehicle = candidate;
                    return true;
                }
            }
        }

        vehicle = null;
        return false;
    }

    /// <summary>Gets the turret of one gun a vehicle was armed with, for presentation and tests.</summary>
    public VehicleGunTurret? GunTurret(GameTask vehicle, int index) =>
        _armaments.TryGetValue(vehicle, out VehicleArmament? armament) && index >= 0 && index < armament.Guns.Count
            ? armament.Guns[index].Turret
            : null;

    /// <summary>Runs one vehicle's guns for its update: each turns to what the vehicle sees and meters its fire.</summary>
    public void TickVehicleCombat(GameTask vehicle)
    {
        Vector3d? target = VehicleCombatTarget?.Invoke();
        if (!_armaments.TryGetValue(vehicle, out VehicleArmament? armament))
        {
            return;
        }

        {
            if (!_context.TaskTransforms.TryGetValue(vehicle, out CutsceneTransformState? transform)
                || vehicle is not DeclaredTask declared)
            {
                return;
            }

            // A car's "Can fire" is an expression: 0x42E870 evaluates it every tick and registers the
            // car's vision while it holds, removing it again when it stops.
            bool canFire = _context.Expressions.EvaluateCondition(TextOrEmpty(declared, "Can fire"), emptyValue: false, vehicle);
            CarAiState? controller = _carAis.Find(state => ReferenceEquals(state.Car, vehicle));
            bool active = ConditionalContainerGate.IsTaskActive(vehicle)
                && (controller is null || ConditionalContainerGate.IsTaskActive(controller.Task))
                && _context.Destructibles?.IsDestroyed(vehicle) != true;

            if (armament.LegacyWeapon is { } weapon)
            {
                TickLegacyVehicleGun(declared, transform, armament, weapon, target, active, canFire);
                return;
            }

            // Each gun walks its vehicle's record for the first slot marked visible and aims at that human's
            // position (0x46F390); its own 90-run hold covers a target it loses.
            Vector3d? aim = active
                && _visions.TryGetValue(vehicle, out VehicleVision? vision)
                && vision.Sees
                && target is { } candidate
                    ? candidate
                    : null;

            // A car's update (0x42E320) runs its guns once, then re-seats them and runs every
            // AIStationaryGun a second time unless +11980 says its physics tick already did, so a car's
            // gun turns and meters its fire twice a tick. Probed on Mission 7 for both kinds of car: the
            // parked T80 354 (T80 rounds 75 ticks apart, the coaxial M2HB one a tick) and the CarAI-driven
            // APC 352 (APC rounds 75 ticks apart), where the rows' own cadences are 150, 2 and 150.
            //
            // A helicopter's update (0x4316D0) never runs its model's attachments; its gun turns only while
            // its AnimTask flies it, once a tick. Probed on Mission 7's heli 353: nothing while parked, and
            // on its flight 55 MIL rounds over 162 ticks -- the row's own three-tick cadence.
            bool helicopter = string.Equals(vehicle.Type.Name, "Heli", StringComparison.Ordinal);
            int runs = !helicopter
                ? 2
                : _animations.Exists(animation => animation.TargetId == vehicle.Id && animation.RanThisTick) ? 1 : 0;

            // A gun aims from its mount's position (+32) and fires from its weapon object, both as the last re-seat
            // left them: nothing moves them while the guns run. The spawn seats them first (0x46EC90).
            if (!armament.Seated)
            {
                SeatVehicleGuns(armament, transform);
            }

            foreach (VehicleGunState gun in armament.Guns)
            {
                gun.RoundsFiredThisTick = 0;
            }

            for (int run = 0; run < runs; run++)
            {
                // A car re-seats its guns between its two runs (0x46FBA0 through op 46), from where it stands now and
                // with the traverse the first run left; a helicopter's AnimTask moves it before its one run.
                if (run == 1 || helicopter)
                {
                    SeatVehicleGuns(armament, transform);
                }

                // The turret runs before the gun it carries (0x46F390 walks its children last).
                foreach (VehicleGunState gun in armament.Guns)
                {
                    Vector3d? local = aim is { } point ? transform.Orientation.Transform(point - gun.MountPosition) : null;
                    if (FireVehicleGun(vehicle, gun, local, aim) is { } shot)
                    {
                        gun.RoundsFiredThisTick++;
                        gun.ShotSequence++;
                        VehicleCombatShotFired?.Invoke(shot);
                    }

                    // The Gun's own update (0x478640) counts its cooldown down after Gun_Fire has run.
                    if (gun.Cooldown > 0)
                    {
                        gun.Cooldown--;
                    }
                }
            }
        }
    }

    /// <summary>Measures how far each zone-carrying vehicle moved this tick, the speed 0x45FA80 tests.</summary>
    public void TickDeathZones(GameTask vehicle)
    {
        if (!_deathZones.TryGetValue(vehicle, out VehicleDeathZoneState? state))
        {
            return;
        }

        if (!_context.TaskTransforms.TryGetValue(vehicle, out CutsceneTransformState? transform))
        {
            state.SpeedUnitsPerTick = 0d;
            return;
        }

        state.SpeedUnitsPerTick = state.LastPosition is { } last ? (transform.Position - last).Length : 0d;
        state.LastPosition = transform.Position;
    }

    /// <summary>One run of a vehicle gun: turn (0x46F390), then let Gun_Fire (0x478BA0) take the trigger.</summary>
    private VehicleCombatShot? FireVehicleGun(
        GameTask vehicle,
        VehicleGunState gun,
        Vector3d? local,
        Vector3d? aim)
    {
        bool trigger = gun.Turret.Tick(local);
        int burst = gun.Gun.Weapon.MaximumRoundsPerBurst;

        // The hold of Gun_Fire decides its sounds: the trigger, the pull's burst unspent and the magazine not empty.
        // The cooldown between rounds is not part of it (0x478BA0).
        gun.Hold = trigger && !(burst > 0 && gun.RoundsThisPull >= burst) && gun.Ammunition != 0;
        if (!trigger)
        {
            // Gun_Fire resets the pull's round count (+320) when the trigger drops.
            gun.RoundsThisPull = 0;
            return null;
        }

        // A row that fires a fixed burst per pull (+1612) stops at it until the trigger is released.
        if ((burst > 0 && gun.RoundsThisPull >= burst)
            || gun.Cooldown > 0
            || gun.Ammunition == 0
            || aim is not { } shotTarget)
        {
            return null;
        }

        // Gun_Fire restarts the cooldown at (60 / rpm) * 30 ticks, truncated.
        gun.RoundsThisPull++;
        gun.Cooldown = Math.Max(1, GunTiming.TicksPerRound(gun.Gun.Weapon.RoundsPerMinute));
        if (gun.Ammunition > 0)
        {
            gun.Ammunition--;
        }

        // With no origin in its message, Gun_Fire starts the round at the weapon's magic vertex 2 and sends it down the
        // weapon's middle column (0x478BA0) -- the weapon object as the last re-seat posed it, which swings a gun on a
        // gun round with the gun it rides on.
        Vector3f muzzle = gun.Gun.MuzzleVertex;
        Vector3d origin = gun.WeaponPosition
            + gun.WeaponOrientation.Transposed().Transform(new Vector3d(muzzle.X, muzzle.Y, muzzle.Z));
        Vector3f barrel = gun.WeaponOrientation.Transposed().Transform(Vector3f.UnitY).Normalized();
        WeaponType row = gun.Gun.Weapon;

        // Gun_Fire sends the round down the barrel, not at the target, turned inside the row's cone
        // (0x478BA0: +1636 for a pull's first three rounds, +1640 after; radius three draws deep).
        float cone = GunFiring.ConeAngle(row, gun.RoundsThisPull);
        if (cone > 0f)
        {
            Span<float> draws = stackalloc float[GunFiring.ConeRadiusSamples + 1];
            for (int draw = 0; draw < draws.Length; draw++)
            {
                draws[draw] = _gunRandom.NextSingle();
            }

            (float radius, float bearing) = GunFiring.SampleCone(cone, draws);
            Vector3f side = Vector3f.Cross(barrel, Vector3f.UnitZ);
            side = side.Length > 1e-6f ? side.Normalized() : Vector3f.UnitX;
            Vector3f up = Vector3f.Cross(side, barrel).Normalized();
            barrel = GunFiring.RetailConeDirection(barrel, radius, bearing);
        }

        double range = (shotTarget - origin).Length;
        Vector3d along = origin + new Vector3d(barrel.X * range, barrel.Y * range, barrel.Z * range);
        return new VehicleCombatShot(
            origin,
            along,
            barrel,
            MissileFlight.TypeForLauncher(row),
            MissileFlight.LaunchSpeedMetresPerSecond(row),
            string.IsNullOrWhiteSpace(row.Projectile.Text) ? "110_02_1" : row.Projectile.Text)
        {
            Owner = vehicle,
            WeaponScriptId = row.ScriptId,
            IsHitscan = gun.Gun.IsHitscan,
        };
    }

    /// <summary>The Level 6 capture's single fixed gun: fire the row's cadence at whatever is retained.</summary>
    private void TickLegacyVehicleGun(
        DeclaredTask vehicle,
        CutsceneTransformState transform,
        VehicleArmament armament,
        WeaponType weapon,
        Vector3d? target,
        bool active,
        bool canFire)
    {
        if (target is not { } seen)
        {
            return;
        }

        Vector3f forward = transform.Orientation.Column2;
        Vector3d sightOrigin = transform.Position + new Vector3d(
            forward.X * AiVehicleCombat.CapturedMuzzleForwardMetres * WorldScene.UnitsPerMetre,
            forward.Y * AiVehicleCombat.CapturedMuzzleForwardMetres * WorldScene.UnitsPerMetre,
            (forward.Z * AiVehicleCombat.CapturedMuzzleForwardMetres
                + AiVehicleCombat.CapturedMuzzleHeightMetres) * WorldScene.UnitsPerMetre);
        bool visible = VehicleCombatLineOfSight?.Invoke(vehicle, sightOrigin, seen) ?? true;
        VehicleCombatShot? shot = armament.Sight.Tick(new AiVehicleCombatInput(
            transform.Position,
            transform.Orientation,
            seen,
            active,
            canFire,
            DeclaredTaskValues.RealConverted(vehicle, "ViewCone Alpha (degrees)", 180f),
            DeclaredTaskValues.RealConverted(vehicle, "ViewCone Gamma (degrees)", 360f),
            DeclaredTaskValues.RealConverted(vehicle, "ViewCone Length (meter)", 0f),
            visible,
            weapon.MuzzleVelocity,
            weapon.RoundsPerMinute));
        if (shot is { } fired)
        {
            VehicleCombatShotFired?.Invoke(fired with
            {
                Owner = vehicle,
                WeaponScriptId = weapon.ScriptId,
                MissileType = MissileFlight.TypeForLauncher(weapon),
                MuzzleVelocityMetresPerSecond = MissileFlight.LaunchSpeedMetresPerSecond(weapon),
                ModelName = string.IsNullOrWhiteSpace(weapon.Projectile.Text)
                    ? fired.ModelName
                    : weapon.Projectile.Text,
            });
        }
    }

    /// <summary>
    /// The vehicle's eye: a car's <c>0x42E870</c> (eye 8192 units over its origin, its own orientation), a helicopter's
    /// <c>+1960</c> block of <c>0x4316D0</c> (eye at its origin, looking down: its orientation times RotX(-90°)).
    /// </summary>
    public void TickVehicleVision(GameTask vehicle)
    {
        if (!_visions.TryGetValue(vehicle, out VehicleVision? vision)
            || vehicle is not DeclaredTask declared
            || !_context.TaskTransforms.TryGetValue(vehicle, out CutsceneTransformState? transform)
            || _context.Destructibles?.IsDestroyed(vehicle) == true)
        {
            return;
        }

        bool helicopter = string.Equals(vehicle.Type.Name, "Heli", StringComparison.Ordinal);
        bool canFire = _context.Expressions.EvaluateCondition(TextOrEmpty(declared, "Can fire"), emptyValue: false, vehicle);
        Vector3d eyePosition = helicopter
            ? transform.Position
            : transform.Position + new Vector3d(0d, 0d, VehicleVision.CarEyeHeightUnits);
        Matrix3f orientation = helicopter ? transform.Orientation * HeliEyeTilt : transform.Orientation;
        AiEyeSnapshot eye = new(
            eyePosition,
            orientation,
            new AiSnapshotCone(
                DeclaredTaskValues.RealConverted(declared, "ViewCone Alpha (degrees)", 180f) * 0.017453292,
                DeclaredTaskValues.RealConverted(declared, "ViewCone Gamma (degrees)", 360f) * 0.017453292,
                DeclaredTaskValues.RealConverted(declared, "ViewCone Length (meter)", 0f) * 4096.0),
            default,
            secondaryEnabled: false);
        Vector3d? target = VehicleSightTarget?.Invoke() ?? VehicleCombatTarget?.Invoke();
        // The sweep compares its phase against the flow tick count (0x4028B0), which the walk increments after it runs:
        // the level's first walk is flow tick 0. Probed on mission 7: the T80's sweeps fall on flow ticks 3, 7, 11, ...
        vision.Tick(
            _context.SimulationTick - 1,
            canFire,
            eye,
            target,
            point => VehicleCombatLineOfSight?.Invoke(vehicle, eyePosition, point) ?? true,
            NextVisionPhase);
    }

    /// <summary>RotX(-1.5707964) as <c>0x4B3B60</c> builds it, which <c>0x4316D0</c> turns a helicopter's eye by.</summary>
    private static readonly Matrix3f HeliEyeTilt = HeliTilt();

    private static Matrix3f HeliTilt()
    {
        float angle = -1.5707964f;
        float sine = (float)Math.Sin(angle), cosine = (float)Math.Cos(angle);
        return new Matrix3f(1f, 0f, 0f, 0f, cosine, -sine, 0f, sine, cosine);
    }

    private int NextVisionPhase()
    {
        int phase = _visionCounter & 3;
        _visionCounter = (_visionCounter + 1) & 3;
        return phase;
    }

    /// <summary>Binds each armed vehicle's drawn gun mounts to the guns that turn them.</summary>
    /// <param name="scene">The built world whose placements carry the vehicles' attachments.</param>
    /// <remarks>
    /// A vehicle's <c>TASKTYPE_AISTATIONARYGUN</c> attachments arrive in the same depth-first order its guns were
    /// collected in (<see cref="VehicleGun.FromModel"/>); each placement takes the next gun whose mount model it is.
    /// Call it after <see cref="ConfigureVehicleGuns"/>.
    /// </remarks>
    public void BindVehicleGunPlacements(WorldScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _gunPlacements.Clear();
        Dictionary<GameTask, int> next = [];
        foreach (WorldPlacement placement in scene.Placements)
        {
            if (!string.Equals(placement.MagicTaskTypeName, GunMountTaskTypeName, StringComparison.Ordinal)
                || !_armaments.TryGetValue(placement.Owner, out VehicleArmament? armament))
            {
                continue;
            }

            for (int index = next.GetValueOrDefault(placement.Owner); index < armament.Guns.Count; index++)
            {
                if (string.Equals(armament.Guns[index].Gun.Model, placement.Instance.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _gunPlacements[placement] = (placement.Owner, index);
                    next[placement.Owner] = index + 1;
                    break;
                }
            }
        }
    }

    /// <summary>Every armed live vehicle's weapon models, as this tick's updates posed them.</summary>
    /// <returns>One entry per gun of an active, undestroyed vehicle.</returns>
    public IEnumerable<VehicleGunVisual> VehicleGunVisuals()
    {
        foreach ((GameTask vehicle, VehicleArmament armament) in _armaments)
        {
            if (!ConditionalContainerGate.IsTaskActive(vehicle) || _context.Destructibles?.IsDestroyed(vehicle) == true)
            {
                continue;
            }

            for (int index = 0; index < armament.Guns.Count; index++)
            {
                if (!TryGetVehicleGunWeapon(vehicle, armament, index, out Vector3d position, out Matrix3f orientation))
                {
                    continue;
                }

                VehicleGunState gun = armament.Guns[index];
                yield return new VehicleGunVisual(
                    vehicle,
                    index,
                    gun.Gun.Mount.WeaponModel,
                    position,
                    orientation,
                    gun.RoundsFiredThisTick > 0,
                    gun.ShotSequence);
            }
        }
    }

    /// <summary>Where a vehicle gun's mount stands and faces, re-seated on its parent (<c>0x46FBA0</c>).</summary>
    /// <remarks>
    /// The mount sits at its attachment offset in its parent's frame and turns by its traverse about its own up
    /// axis: parent · attachment · Z(traverse), rows dotted into columns. A gun on a gun (a T80's coaxial M2HB on
    /// its turret) is re-seated on the turned mount, so it swings round with the turret.
    /// </remarks>
    private bool TryGetVehicleGunMount(
        GameTask vehicle,
        VehicleArmament armament,
        int index,
        out Vector3d position,
        out Matrix3f orientation)
    {
        VehicleGun gun = armament.Guns[index].Gun;
        Vector3d parentPosition;
        Matrix3f parentOrientation;
        if (gun.Parent >= 0 && gun.Parent < index)
        {
            if (!TryGetVehicleGunMount(vehicle, armament, gun.Parent, out parentPosition, out parentOrientation))
            {
                position = default;
                orientation = Matrix3f.Identity;
                return false;
            }
        }
        else if (_context.TaskTransforms.TryGetValue(vehicle, out CutsceneTransformState? state))
        {
            UpdateDerivedPose(state);
            parentPosition = state.Position;
            parentOrientation = state.Orientation;
        }
        else
        {
            position = default;
            orientation = Matrix3f.Identity;
            return false;
        }

        (position, orientation) = PoseVehicleGunMount(armament.Guns[index], parentPosition, parentOrientation);
        return true;
    }

    /// <summary>Re-seats every gun of a vehicle where it stands now (<c>0x46FBA0</c>, then <c>0x470050</c>).</summary>
    /// <remarks>A gun's parent comes before it in the list, so each gun is seated on its parent's new pose.</remarks>
    private static void SeatVehicleGuns(VehicleArmament armament, CutsceneTransformState transform)
    {
        for (int index = 0; index < armament.Guns.Count; index++)
        {
            VehicleGunState gun = armament.Guns[index];
            int parent = gun.Gun.Parent;
            (Vector3d mountPosition, Matrix3f mountOrientation) = parent >= 0 && parent < index
                ? PoseVehicleGunMount(gun, armament.Guns[parent].MountPosition, armament.Guns[parent].MountOrientation)
                : PoseVehicleGunMount(gun, transform.Position, transform.Orientation);
            gun.MountPosition = mountPosition;
            gun.MountOrientation = mountOrientation;
            (gun.WeaponPosition, gun.WeaponOrientation) =
                PoseVehicleGunWeapon(gun, mountPosition, mountOrientation, transform.Orientation);
        }

        armament.Seated = true;
    }

    /// <summary>A mount's pose on its parent: parent · attachment · Z(traverse) (<c>0x46FBA0</c>).</summary>
    private static (Vector3d Position, Matrix3f Orientation) PoseVehicleGunMount(
        VehicleGunState gun,
        Vector3d parentPosition,
        Matrix3f parentOrientation)
    {
        Vector3f local = gun.Gun.AttachmentPosition;
        return (
            parentPosition + parentOrientation.Transposed().Transform(new Vector3d(local.X, local.Y, local.Z)),
            parentOrientation * gun.Gun.AttachmentOrientation * Matrix3f.RotationZ(gun.Turret.Yaw));
    }

    /// <summary>A weapon object's pose on its mount (<c>0x470050</c>).</summary>
    private static (Vector3d Position, Matrix3f Orientation) PoseVehicleGunWeapon(
        VehicleGunState gun,
        Vector3d mountPosition,
        Matrix3f mountOrientation,
        Matrix3f vehicleOrientation)
    {
        Vector3f vertex = gun.Gun.WeaponVertex;
        Matrix3f orientation = vehicleOrientation * Matrix3f.FromEngineEulerAngles(gun.Turret.Pitch, 0f, gun.Turret.Yaw);
        if (gun.Turret.Spin != 0f)
        {
            orientation *= Matrix3f.RotationY(gun.Turret.Spin);
        }

        return (mountPosition + mountOrientation.Transposed().Transform(new Vector3d(vertex.X, vertex.Y, vertex.Z)), orientation);
    }

    /// <summary>Where a vehicle gun's weapon object sits and faces (<c>0x470050</c>).</summary>
    /// <remarks>
    /// On the mount's weapon vertex, turned with the mount; faced by the vehicle's own orientation times
    /// <c>0x4B38E0(elevation, 0, traverse)</c>, then by Y(spin) once the barrels have spun. The traverse and
    /// elevation are measured in the vehicle's frame, so the mount's own attachment turn plays no part.
    /// </remarks>
    private bool TryGetVehicleGunWeapon(
        GameTask vehicle,
        VehicleArmament armament,
        int index,
        out Vector3d position,
        out Matrix3f orientation)
    {
        if (!TryGetVehicleGunMount(vehicle, armament, index, out Vector3d mountPosition, out Matrix3f mountOrientation)
            || !_context.TaskTransforms.TryGetValue(vehicle, out CutsceneTransformState? state))
        {
            position = default;
            orientation = Matrix3f.Identity;
            return false;
        }

        (position, orientation) = PoseVehicleGunWeapon(armament.Guns[index], mountPosition, mountOrientation, state.Orientation);
        return true;
    }

    /// <summary>Poses a drawn gun-mount placement on the gun that turns it, when it is bound to one.</summary>
    public bool TryGetGunMountDraw(WorldPlacement placement, out Vector3d position, out Matrix3f orientation)
    {
        if (_gunPlacements.TryGetValue(placement, out (GameTask Vehicle, int Index) mount)
            && _armaments.TryGetValue(mount.Vehicle, out VehicleArmament? armament)
            && TryGetVehicleGunMount(mount.Vehicle, armament, mount.Index, out Vector3d mountUnits, out orientation))
        {
            position = mountUnits / WorldScene.UnitsPerMetre;
            return true;
        }

        position = default;
        orientation = Matrix3f.Identity;
        return false;
    }
}
