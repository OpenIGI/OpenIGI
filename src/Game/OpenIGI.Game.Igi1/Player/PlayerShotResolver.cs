using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Input;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// Settles what the player's weapons fire: the hit-scan march, the knife's sweep, the thrown and
/// launched rounds handed to their simulations, and the marks the rounds leave.
/// </summary>
/// <remarks>
/// <para>
/// The player's <c>0x462E50</c> call and everything it drives. The camera ticks it at the same two
/// points <c>HumanPlayer_Update</c> does — the fire after the view is committed, then the rounds in
/// flight — so a round spawned this tick flies its first segment in the same tick.
/// </para>
/// <para>
/// It holds no input and no view of its own: the weapon machine and the rig are handed to
/// <see cref="Fire"/>, and the world it strikes is set on it as the level loads.
/// </para>
/// </remarks>
internal sealed class PlayerShotResolver
{
    private readonly PlayerBody _body;
    private readonly IgiRandom _random;
    private readonly List<GunImpact> _impacts = [];
    private readonly BallisticSimulation _ballistics = new();
    private readonly GrenadeSimulation _grenades = new();
    private readonly MissileSimulation _missiles = new();
    private readonly M203Simulation _m203 = new();
    private readonly List<PendingMark> _pendingMarks = [];

    /// <summary>Initializes the resolver for one player.</summary>
    /// <param name="body">The body whose walls and feet the rounds and grenades read.</param>
    /// <param name="random">The player's random stream, which the ballistics draw from.</param>
    internal PlayerShotResolver(PlayerBody body, IgiRandom random)
    {
        _body = body;
        _random = random;
    }

    /// <summary>Gets or sets the level's placed objects, which the rounds march through.</summary>
    internal ObjectCollisionIndex? Walls { get; set; }

    /// <summary>Gets or sets the game material table each struck surface's row comes from.</summary>
    internal GameMaterialRegistry? Materials { get; set; }

    /// <summary>Gets what the last tick's rounds hit.</summary>
    /// <remarks>Empty on every tick nothing was fired, which is most of them.</remarks>
    internal IReadOnlyList<GunImpact> Impacts => _impacts;

    /// <summary>Gets the marks rounds have left on the world.</summary>
    internal BulletMarks Marks { get; } = new();

    /// <summary>Gets the rounds in flight under <c>g_ballistics</c>, for the tracer renderer.</summary>
    /// <remarks>Empty whenever the option is off — nothing is ever spawned into it.</remarks>
    internal BallisticSimulation Ballistics => _ballistics;

    /// <summary>Gets the grenades in the air, both the player's and the AI's.</summary>
    /// <remarks>
    /// One simulation for both throwers so there is one place to tick, draw and detonate from — the
    /// soldiers spawn their lobs into it through the renderer's wiring.
    /// </remarks>
    internal GrenadeSimulation Grenades => _grenades;

    /// <summary>Gets the launcher missiles currently in flight.</summary>
    internal MissileSimulation Missiles => _missiles;

    /// <summary>Gets the M203 rounds currently in flight.</summary>
    internal M203Simulation M203 => _m203;

    /// <summary>Gets or sets what a grenade bounces off — the world <b>including the terrain</b>.</summary>
    /// <remarks>
    /// Deliberately not <see cref="PlayerBody.Walls"/>: that is the object index alone, because the
    /// body meets the terrain through its own ground query. A grenade has no ground query — without
    /// the terrain in its cast it falls straight through the floor of the world.
    /// </remarks>
    internal WorldRayCast? GrenadeWalls { get; set; }

    /// <summary>
    /// Gets or sets the rich world trace used by proximity mines to distinguish an object from terrain.
    /// </summary>
    internal WorldTraceCast? GrenadeTrace { get; set; }

    /// <summary>Gets or sets the world cast used by launcher missiles.</summary>
    internal WorldRayCast? MissileWalls { get; set; }

    /// <summary>Gets or sets the rich world trace used to retain a launcher's exact impact object.</summary>
    internal WorldTraceCast? MissileTrace { get; set; }

    /// <summary>Gets or sets what offers this player's rounds to the humans in the world.</summary>
    /// <remarks>
    /// Without it rounds stop at walls and hit nobody. A round takes whichever of the two is
    /// <b>nearer</b>, so a guard standing in front of a wall is hit and the wall is not marked.
    /// </remarks>
    internal HumanShot? Humans { get; set; }

    /// <summary>Gets or sets a non-mutating human intersection used by launcher missiles.</summary>
    internal HumanRayCast? HumanProbes { get; set; }

    /// <summary>Gets or sets the living-human query used by armed proximity mines.</summary>
    internal HumanProximityCast? HumanProximityTargets { get; set; }

    /// <summary>Gets or sets where actual projectile contacts are offered to the AI event broadcaster.</summary>
    /// <remarks>
    /// This is invoked only when the world surface is the nearest hit. A human in front of that surface
    /// receives damage instead and does not also manufacture a ground-impact event behind itself.
    /// </remarks>
    internal Action<Vector3d>? GroundImpact { get; set; }

    /// <summary>Raised where a round glanced off a surface, so the whizz can be sounded there.</summary>
    /// <remarks>
    /// Separate from the ordinary contact notice because the original sounds a deflection directly at
    /// <c>0x47BD7B</c>, outside the material's own quiet window.
    /// </remarks>
    internal Action<Vector3d>? Deflected { get; set; }

    /// <summary>Gets how many of this player's rounds have struck a human.</summary>
    internal int HumanHits { get; private set; }

    /// <summary>Gets the zone the last round to strike a human landed in, or <c>-1</c>.</summary>
    internal int LastHumanZone { get; private set; } = -1;

    /// <summary>Gets the cast a mark's corner test probes with, or null when no walls are loaded.</summary>
    /// <remarks>
    /// It has to be the same collision the rounds strike. <c>0x47D920</c> probes each corner a few
    /// millimetres either side of the struck face, so a cast over any other mesh -- the player's
    /// movement level in particular -- misses every corner and drops every mark.
    /// </remarks>
    internal WorldRayCast? MarkProbe => Walls?.Caster();

    /// <summary>Gets or sets a trace through the terrain, for the casts a ground query cannot answer.</summary>
    /// <remarks>
    /// The view model's wall pullback sweeps <em>forward</em>, so it wants a ray through the terrain
    /// rather than the height under a point -- which is why it arrives as a cast the host supplies and not
    /// as something read off the ground the body stands on. A title whose terrain nothing can trace
    /// leaves this null, and the pullback then meets placed objects only.
    /// </remarks>
    internal WorldTraceCast? TerrainTrace { get; set; }

    /// <summary>Gets or sets the ray the crosshair sits on — the drawn camera's own centre line.</summary>
    /// <value>
    /// The camera's position in engine units and its unit forward, or <see langword="null"/> — the
    /// default, and the only value first person ever holds — to fire straight along the eye's own
    /// forward, exactly as the original does.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>The one thing the third-person view changes about the game rather than about the picture.</b>
    /// A camera behind and to the side of the eye does not share its sight line, so a fixed crosshair
    /// and a shot along the body's forward cannot both be right. Rather than let the crosshair wander to
    /// wherever the round would land — truthful, and unusable — the shot converges on what the middle of
    /// the screen is over.
    /// </para>
    /// <para>
    /// <b>A ray rather than a point, and this is the whole of the fix.</b> Handing in a point meant the
    /// renderer decided what the crosshair was over, and it answered with a different test from the one
    /// the bullet uses — it saw walls and terrain and looked straight through people. So a shot aimed at
    /// a guard converged on the hillside behind him and landed half a metre wide, which is exactly the
    /// distance between the camera and the eye. Resolving the ray HERE settles it against
    /// <see cref="HumanProbes"/> and the same <c>WorldTraceMask.World</c> trace <see cref="ResolveShot"/>
    /// marches, so the crosshair's target and the round's target are found by one query and cannot
    /// disagree.
    /// </para>
    /// <para>
    /// The round still <b>leaves the eye</b>: only its direction is turned, which keeps the muzzle, the
    /// tracer and the wall probe where they were. And the turn is the identity whenever the camera is at
    /// the eye, so first person needs no flag to be unaffected.
    /// </para>
    /// </remarks>
    internal (Vector3d OriginUnits, Vector3f Direction)? AimConvergenceRay { get; set; }

    /// <summary>How far the convergence probe reaches, in engine units — 300 m.</summary>
    /// <remarks>
    /// Past this the angle between a shot from the eye and one from a camera 2.6 m behind it is a
    /// fraction of a degree. A ray that reaches the end unobstructed converges on the end, which is the
    /// same answer as not converging at all.
    /// </remarks>
    internal const double AimConvergenceReachInUnits = 300.0 * PlayerCamera.UnitsPerMetre;

    /// <summary>Finds what the crosshair is over, with the tests a round would meet on the way.</summary>
    /// <returns>The world point, in engine units, or <see langword="null"/> when nothing is aimed.</returns>
    /// <remarks>
    /// The picking rule itself is <see cref="AimConvergence"/>, which is where the reasoning lives and
    /// where it is tested; this only names the three sources. They are the SAME ones a round meets:
    /// <see cref="Walls"/> is what <see cref="ResolveShot"/> marches, <see cref="TerrainTrace"/> is the
    /// cast through the ground the height field cannot answer, and <see cref="HumanProbes"/> is
    /// the reporting half of the <see cref="Humans"/> cast it offers rounds to.
    /// </remarks>
    private Vector3d? ResolveAimConvergence()
    {
        if (AimConvergenceRay is not { } ray)
        {
            return null;
        }

        return AimConvergence.Resolve(
            ray.OriginUnits,
            ray.Direction,
            AimConvergenceReachInUnits,
            Walls?.Tracer(),
            TerrainTrace,
            HumanProbes);
    }

    /// <summary>Forgets the last tick's impacts, as the fire and the mounted gun do before they resolve.</summary>
    internal void ClearImpacts() => _impacts.Clear();

    /// <summary>The firing half of <c>0x462E50</c>: the rounds the weapon in hand let go this tick.</summary>
    /// <param name="weapons">The weapon machine.</param>
    /// <param name="view">The first-person rig, already committed for this tick.</param>
    /// <param name="scoped">Whether the Dragunov's scope is open.</param>
    internal void Fire(PlayerWeapons weapons, HumanView view, bool scoped)
    {
        // Binoculars are an optical device, not a gun. The weapon state machine ignores Fire while
        // they are held, and this guard keeps a stale trigger/request from reaching the generic gun
        // resolver if the input changes during the weapon-swap transition.
        if (weapons.InHand?.Weapon.Category == WeaponCategory.Binocular)
        {
            return;
        }

        // The barrel is the view's +Y axis and the cone opens across its +X and +Z -- 0x462E50 picks
        // exactly the middle column of the view's 3x3 out for the direction.
        Matrix3f m = view.EyeOrientation;
        Vector3f forward = new(m.M12, m.M22, m.M32);
        Vector3f right = new(m.M11, m.M21, m.M31);
        Vector3f up = new(m.M13, m.M23, m.M33);

        // ...unless a camera that is not at the eye has said what the crosshair is over. See
        // AimConvergencePointUnits: null in first person, and null is the retail path untouched.
        AimBasis.Converge(view.Eye, ResolveAimConvergence(), ref forward, ref right, ref up);

        // A scoped rifle is braced: 0x472E50 multiplies the round's sampled kick by 0.05 before the
        // accumulators see it, and this is the same factor at the same point.
        float recoilScale = scoped ? DragunovScope.ScopedRecoilFactor : 1f;
        if (!weapons.ResolveFire(view.Eye, forward, right, up, recoilScale))
        {
            return;
        }

        if (weapons.M203Shots.Count > 0)
        {
            bool rejected = false;
            foreach (M203Projectile shot in weapons.M203Shots)
            {
                rejected |= !_m203.Spawn(shot);
            }

            if (rejected)
            {
                weapons.RefundRejectedSimulationShot();
            }

            return;
        }

        float damageFactor = weapons.InHand?.Weapon.DamageFactor ?? 0f;

        // The category travels with the round because it decides how a body this round kills falls:
        // 0x45D3C0 records a shotgun kill as a throw and everything else as a crumple, and the death
        // state picks a different family of clips for each.
        WeaponCategory category = weapons.InHand?.Weapon.Category ?? WeaponCategory.Gun;

        // A thrown round is a GRENADE, and a grenade was never hit-scan in the original either --
        // it is a physics object with a four-second fuse. It flies the same on every profile.
        WeaponType? thrownWeapon = weapons.InHand?.Weapon;
        if (thrownWeapon is not null && thrownWeapon.Category == WeaponCategory.ProximityMine)
        {
            WorldTraceCast? mineTrace = GrenadeTrace;
            if (mineTrace is null && GrenadeWalls is { } mineWalls)
            {
                mineTrace = WorldTrace.FromRayCast(mineWalls);
            }

            foreach (GunProjectile shot in weapons.Shots)
            {
                // The mine task is placed at the first surface under the crosshair. It is not a
                // thrown grenade: starting it at the muzzle with zero velocity made it drop through
                // the level until the physics solver happened to find a floor, which is visibly
                // different from retail and makes wall/prop placement impossible.
                _grenades.PlaceMine(
                    shot.Origin,
                    _body.Position,
                    shot.Direction,
                    shot.RangeInUnits,
                    mineTrace);
            }

            return;
        }

        if (thrownWeapon is not null && thrownWeapon.Category == WeaponCategory.Grenade)
        {
            // The physics table keys the explosion type by MODEL: 135_01_1 is the frag, 137_01_1
            // the flashbang.
            GrenadeExplosionType explosion = string.Equals(thrownWeapon.GunModel, "137_01_1", StringComparison.OrdinalIgnoreCase)
                ? GrenadeExplosionType.Flashbang
                : GrenadeExplosionType.Normal;

            // The speed is the CHARGE'S: retail winds the throw up while fire is held and releases
            // on the retail power curve, up to the table's own fifteen metres a second.
            float throwSpeed = PlayerWeapons.ChargedThrowSpeed(
                thrownWeapon.MuzzleVelocity, weapons.ThrowChargeTicks);

            float radius = ExplosionDamage.DefaultGrenadeRadiusMetres;
            float falloff = ExplosionDamage.DefaultGrenadeFalloffMetres;
            float damage = ExplosionDamage.DefaultGrenadeDamageFactor;
            if (thrownWeapon.RetailConfig is { } config)
            {
                if (WeaponConfigRegistry.TryGetRealTag(config, "Grenade - Explosion radius (meters)", out float configured))
                {
                    radius = configured;
                }
                if (WeaponConfigRegistry.TryGetRealTag(config, "Grenade - Explosion falloff (meters)", out configured))
                {
                    falloff = configured;
                }
                if (WeaponConfigRegistry.TryGetRealTag(config, "Grenade - Explosion damage factor", out configured))
                {
                    damage = configured;
                }
            }

            foreach (GunProjectile shot in weapons.Shots)
            {
                _grenades.Spawn(
                    shot.Origin,
                    shot.Direction,
                    throwSpeed,
                    explosion,
                    radiusMetres: radius,
                    falloffMetres: falloff,
                    damageFactor: damage);
            }

            return;
        }

        // A missile task is a physics object, not a gun ray. The launcher binding selects this path
        // for every missile carrier; no weapon id or model name is special-cased here.
        WeaponType? missileWeapon = weapons.InHand?.Weapon;
        if (missileWeapon is not null
            && string.Equals(missileWeapon.Projectile.TypeName, "TASKTYPE_MISSILE", StringComparison.Ordinal))
        {
            bool rejected = false;
            foreach (GunProjectile shot in weapons.Shots)
            {
                MissileType missileType = MissileFlight.TypeForLauncher(missileWeapon);
                // Direct / keyboard rockets fly along the barrel. Using hit-scan EffectiveRange as
                // the aim point lofted anti-tank shots 25 m above a phantom far point.
                Vector3d target = missileType is MissileType.MissileDirect or MissileType.MissileKeyboard
                    ? shot.Origin + new Vector3d(
                        shot.Direction.X * MissileFlight.UnitsPerMetre,
                        shot.Direction.Y * MissileFlight.UnitsPerMetre,
                        shot.Direction.Z * MissileFlight.UnitsPerMetre)
                    : shot.Origin + new Vector3d(
                        shot.Direction.X * shot.RangeInUnits,
                        shot.Direction.Y * shot.RangeInUnits,
                        shot.Direction.Z * shot.RangeInUnits);

                rejected |= !_missiles.Spawn(
                    shot.Origin,
                    target,
                    missileType,
                    MissileFlight.LaunchSpeedMetresPerSecond(missileWeapon),
                    shot.Direction,
                    string.IsNullOrWhiteSpace(missileWeapon.Projectile.Text) ? "110_02_1" : missileWeapon.Projectile.Text,
                    sourceWeaponId: missileWeapon.WeaponId,
                    launcherMetresPerSecond: missileWeapon.MuzzleVelocity);
            }

            if (rejected)
            {
                weapons.RefundRejectedSimulationShot();
            }

            return;
        }

        // The opt-in fork. With g_ballistics on, a round that can fly is spawned into the simulation
        // instead of being resolved here -- it arrives ticks later, in TickBallistics. A weapon no
        // profile accepts (the knife, a rocket carrier) falls through to the retail ray either way,
        // so the option never silently disarms anything.
        WeaponType? held = weapons.InHand?.Weapon;
        AmmoType? ammo = weapons.InHand?.Gun.Ammo;
        bool simulate = GameplayCVars.Ballistics.Value
            && held is not null
            && BallisticProfile.TryCreate(held, ammo) is not null;

        // Where the round is DRAWN from: the gun sits right of and below the eye the simulation
        // fires from, and a tracer leaving dead screen centre is invisible -- it recedes into a
        // point behind the crosshair. The offset is presentation only; the flight is the retail
        // eye-origin geometry either way.
        Vector3d visualOffset = new(
            ((right.X * 0.22) - (up.X * 0.16)) * PlayerCamera.UnitsPerMetre,
            ((right.Y * 0.22) - (up.Y * 0.16)) * PlayerCamera.UnitsPerMetre,
            ((right.Z * 0.22) - (up.Z * 0.16)) * PlayerCamera.UnitsPerMetre);

        foreach (GunProjectile shot in weapons.Shots)
        {
            if (simulate)
            {
                _ballistics.Spawn(
                    shot,
                    held!,
                    ammo,
                    damageFactor,
                    category,
                    visualOffset,
                    GameplayCVars.BallisticsMinimumFlight.Value);
            }
            else if (category == WeaponCategory.Knife)
            {
                ResolveStab(shot, right, damageFactor, category);
            }
            else
            {
                ResolveShot(shot, damageFactor, category, held?.PenetrationPower ?? 0f);
            }
        }
    }

    // The rounds of a mounted gun, down the same fork as a carried one: flown with g_ballistics on and
    // a weapon a profile accepts, else the retail ray. The muzzle is the gun's own, so a streak needs no
    // offset to be seen leaving it.
    internal void ResolveMountedRounds(IReadOnlyList<GunProjectile> shots, WeaponType weapon)
    {
        bool simulate = GameplayCVars.Ballistics.Value && BallisticProfile.TryCreate(weapon, null) is not null;
        foreach (GunProjectile shot in shots)
        {
            if (simulate)
            {
                _ballistics.Spawn(
                    shot,
                    weapon,
                    null,
                    weapon.DamageFactor,
                    weapon.Category,
                    default,
                    GameplayCVars.BallisticsMinimumFlight.Value);
            }
            else
            {
                ResolveShot(shot, weapon.DamageFactor, weapon.Category, weapon.PenetrationPower);
            }
        }
    }

    /// <summary>Advances the ballistic rounds and lands whatever arrived this tick.</summary>
    /// <remarks>
    /// The impact side-effects are exactly <see cref="ResolveShot"/>'s, applied on the tick the round
    /// arrives rather than the tick it was fired: the hit counters, the AI's ground-impact notice and
    /// the decal. Damage needs nothing here — the <see cref="Humans"/> delegate applied it during the
    /// cast, the same way it does for a ray.
    /// </remarks>
    internal void TickBallistics()
    {
        // The grenades fly every tick regardless of the tracer option -- they are the retail
        // behaviour, not a remaster one. Detonations are drained by the game, which owns the
        // effects and the blast's victims.
        _grenades.Tick(GrenadeWalls ?? _body.Walls, HumanProximityTargets, GrenadeTrace);
        if (MissileTrace is { } missileTrace)
        {
            _missiles.TickWithMissileContext(
                (LiveMissile _, Vector3d from, Vector3d to, out WorldTraceHit hit) =>
                    missileTrace(new WorldTraceRequest(from, to, WorldTraceMask.World), out hit),
                HumanProbes);
        }
        else
        {
            _missiles.Tick(MissileWalls ?? GrenadeWalls ?? _body.Walls, HumanProbes);
        }
        _m203.Tick(GrenadeWalls ?? _body.Walls, HumanProbes);

        if (_ballistics.Rounds.Count == 0)
        {
            return;
        }

        WorldRayCast? walls = _body.Walls;
        _ballistics.Tick(
            walls,
            Humans,
            GameplayCVars.BallisticsGravity.Value,
            GameplayCVars.BallisticsDrag.Value,
            Walls?.Tracer(),
            BallisticEntryOf);

        foreach (BallisticImpact impact in _ballistics.Impacts)
        {
            if (impact.StruckHuman)
            {
                HumanHits++;
                LastHumanZone = impact.Human.Zone;
            }
            else if (impact.StruckWall)
            {
                GroundImpact?.Invoke(impact.Wall.Position);

                // The surface decides what the hit looks like, out of the material its own face names,
                // and the struck object decides whether it takes a mark at all.
                if (Materials is { } materials)
                {
                    Marks.TryAdd(
                        impact.Wall,
                        BulletMarks.SurfaceOf(impact.Owner, impact.Placement),
                        materials,
                        isEntry: !impact.Wall.IsExit,
                        exitCrossing: impact.Wall.IsExit,
                        probe: MarkProbe,
                        placement: impact.Placement);
                }
            }

            _impacts.Add(new GunImpact(
                impact.Position,
                impact.StruckHuman || impact.StruckWall,
                impact.RangeInUnits,
                impact.Owner,
                impact.Placement,
                StruckHuman: impact.StruckHuman,
                Wall: impact.StruckWall ? impact.Wall : default,
                Direction: impact.Direction));
        }
    }

    // A flying round meets a surface or a body under the hit-scan march's own rule: the material row against
    // the weapon's power, and only an object the trace named can be passed. A glance stops a flying round.
    private GunShotImpact BallisticEntryOf(
        BallisticRound round,
        in WorldRayHit surface,
        bool namedObject,
        Vector3d direction) =>
        Materials is { } rows && rows.TryGet(surface.Material, out GameMaterial row)
            ? GunShotBallistics.ResolveEntry(
                round.Weapon.PenetrationPower,
                row,
                namedObject,
                GunShotBallistics.IncidenceRadians(direction, surface.Normal),
                alreadyDeflected: false,
                _random.NextSingle)
            : GunShotImpact.Stopped;

    /// <summary>Resolves one round the way <c>0x47B750</c> does — as a march, not a single cast.</summary>
    /// <remarks>
    /// <para>
    /// The round is hit-scan and settled entirely in the tick it is fired, but it does not stop at the
    /// first thing it meets. The original loops while range is left, and each surface either stops the
    /// round, is penetrated, or deflects it — the rules are <see cref="GunShotBallistics"/>, which had
    /// been written from this same function and then never driven by anything.
    /// </para>
    /// <para>
    /// Each surface's own row supplies the two numbers: <see cref="GameMaterial.BulletResistance"/>
    /// against the weapon's penetration power (<c>0x47BD88</c>), and
    /// <see cref="GameMaterial.RicochetAngleDegrees"/> against the angle of arrival (<c>0x47BB96</c>).
    /// A deflection is allowed once per round and is tested BEFORE penetration, exactly as the original
    /// orders them, so a grazing round glances rather than punching through.
    /// </para>
    /// <para>
    /// A crossing is an <b>entry</b> or an <b>exit</b>, never both: <c>0x4CD300</c> reports the two as
    /// the halves of one condition (<c>*a7 = c0; *a8 = !c0</c>), so the pair the trace hands back always
    /// has exactly one side set. An entry sounds and marks the surface and is what penetration and
    /// deflection are tested on; an exit leaves only the far-side decal, silently, and arms the latch
    /// that stops the round on its next entry (<c>0x47BB0A</c>) — which is what keeps a round that
    /// punched through a wall from carrying on through the whole level.
    /// </para>
    /// </remarks>
    internal void ResolveShot(
        GunProjectile shot,
        float damageFactor,
        WeaponCategory category,
        float penetrationPower)
    {
        Vector3d direction = new(shot.Direction.X, shot.Direction.Y, shot.Direction.Z);
        Vector3d origin = shot.Origin;
        float remaining = shot.RangeInUnits;
        bool deflected = false;
        bool leftASurface = false;

        // The impacts the march records, marked only once it is over: 0x47C262 decides each one's entry
        // flag after the loop, from whether the round ever glanced and from which way the LAST surface
        // faced. A round that glances therefore marks even the surfaces it met BEFORE the glance as exits.
        _pendingMarks.Clear();
        March();
        CommitMarks(deflected);

        void March()
        {
            // The march is bounded by the range it spends, but a surface reporting a zero-length step would
            // spend none. Cap the passes at the number of impacts one round may record.
            for (int pass = 0; pass < GunShotBallistics.MaximumRecordedImpacts && remaining > 0f; pass++)
            {
                Vector3d end = origin + (direction * remaining);

                // The direction the effect dispatcher carries with the contact. It changes mid-march when
                // the round glances, so it is taken fresh each pass rather than from the shot.
                Vector3f travel = new((float)direction.X, (float)direction.Y, (float)direction.Z);

                WorldTraceHit richHit = default;
                bool struckWall = Walls is not null
                    && Walls.TryTrace(new WorldTraceRequest(origin, end, WorldTraceMask.World), out richHit);
                WorldRayHit hit = struckWall ? richHit.RayHit : default;

                // The wall first, then the guards short of it. A guard in front of a wall takes the round
                // and the wall stays clean; one behind it is never offered the damage, and the wall is
                // marked. The callback damages as it is called, so the stop has to go in with the round.
                HumanHit human = default;
                bool struckHuman = Humans is { } humans
                    && humans(
                        origin,
                        end,
                        damageFactor,
                        category,
                        struckWall ? hit.Distance : double.PositiveInfinity,
                        alreadyHeard: pass > 0,
                        out human)
                    && (!struckWall || human.Distance < hit.Distance);

                if (struckHuman && ResolveBody(human, travel))
                {
                    continue;
                }

                if (struckHuman)
                {
                    return;
                }

                if (!struckWall)
                {
                    _impacts.Add(new GunImpact(end, false, remaining, Direction: travel));
                    return;
                }

                remaining = GunShotBallistics.ConsumeRange(remaining, (float)hit.Distance);

                // Coming OUT of something. The original marks the far side and says nothing: no sound, no
                // hit message, no penetration test. It only remembers that it happened.
                if (hit.IsExit)
                {
                    Record(hit, richHit, exitCrossing: true);

                    leftASurface = true;
                    if (remaining <= 0f)
                    {
                        return;
                    }

                    origin = StepOff(hit.Position, direction);
                    continue;
                }

                GroundImpact?.Invoke(hit.Position);

                _impacts.Add(new GunImpact(
                    hit.Position,
                    true,
                    remaining,
                    richHit.Owner,
                    richHit.Placement,
                    Wall: hit,
                    Direction: travel));

                // A round that has already come out of something stops on the next thing it enters,
                // whatever that thing is made of — it has spent its one pass through.
                if (leftASurface)
                {
                    Record(hit, richHit, exitCrossing: false);
                    return;
                }

                GameMaterial? struck = Materials is { } rows && rows.TryGet(hit.Material, out GameMaterial row)
                    ? row
                    : null;
                if (struck is null || remaining <= 0f)
                {
                    Record(hit, richHit, exitCrossing: false);
                    return;
                }

                // The graze first, then the bite. The collision index has already turned the normal to face
                // the ray, so the angle between the reversed travel and that normal is the incidence 0x4B2B30
                // returns. The original will not let a round through a surface whose trace named no object,
                // whatever the margin, so a hit on the bare level shell always stops it.
                GunShotImpact outcome = GunShotBallistics.ResolveEntry(
                    penetrationPower,
                    struck,
                    namedObject: richHit.Owner is not null,
                    GunShotBallistics.IncidenceRadians(direction, hit.Normal),
                    deflected,
                    _random.NextSingle);
                if (outcome == GunShotImpact.Deflected)
                {
                    // 0x47BD7B sounds the whizz directly here rather than through the material's
                    // window, so a round that glances always gets one.
                    deflected = true;
                    Deflected?.Invoke(hit.Position);

                    // 0x47C262: a round that has glanced marks every surface it met as an EXIT, which
                    // on metal and concrete is the dent rather than the hole. The latch is set here,
                    // before the impact is recorded at 0x47BDED; CommitMarks reads it.
                    Record(hit, richHit, exitCrossing: false);
                    direction = Reflect(direction, hit.Normal);
                    origin = StepOff(hit.Position, direction);
                    continue;
                }

                if (outcome == GunShotImpact.Stopped)
                {
                    Record(hit, richHit, exitCrossing: false);
                    return;
                }

                Record(hit, richHit, exitCrossing: false);
                origin = StepOff(hit.Position, direction);
            }
        }

        // A body is a surface like any other to 0x47B750: the trace's nearest contact, run through the same
        // branches as a wall. An entry face takes the hit -- the damage the Humans callback has already
        // applied -- and then the same graze and bite as a wall, against the face's own material: Flesh and
        // BloodyFlesh resist 1, so a power-2 or power-3 round goes on through and a power-1 round three times
        // in five. An exit face is the round leaving the body: no handler, and the latch that stops the round
        // on the next thing it enters, after that thing has taken the hit. Probed on the guards' rounds
        // through the player (pairs of hits from one round); the player's rounds take the same march.
        // Returns whether the march carries on.
        bool ResolveBody(in HumanHit human, Vector3f travel)
        {
            remaining = GunShotBallistics.ConsumeRange(remaining, (float)human.Distance);

            if (human.IsExit)
            {
                leftASurface = true;
                if (remaining <= 0f)
                {
                    return false;
                }

                origin = StepOff(human.Position, direction);
                return true;
            }

            HumanHits++;
            LastHumanZone = human.Zone;
            _impacts.Add(new GunImpact(human.Position, true, remaining, StruckHuman: true, Direction: travel));

            if (leftASurface
                || remaining <= 0f
                || human.Material < 0
                || Materials is not { } rows
                || !rows.TryGet(human.Material, out GameMaterial skin))
            {
                return false;
            }

            GunShotImpact outcome = GunShotBallistics.ResolveEntry(
                penetrationPower,
                skin,
                namedObject: true,
                GunShotBallistics.IncidenceRadians(direction, human.Normal),
                deflected,
                _random.NextSingle);
            if (outcome == GunShotImpact.Stopped)
            {
                return false;
            }

            if (outcome == GunShotImpact.Deflected)
            {
                deflected = true;
                Deflected?.Invoke(human.Position);
                direction = Reflect(direction, human.Normal);
            }

            origin = StepOff(human.Position, direction);
            return true;
        }

        // One recorded impact. The surface decides what it looks like, out of the material its own face
        // names, and the struck OBJECT decides whether it takes a mark at all -- terrain and the types
        // that refuse the decal message never do.
        void Record(in WorldRayHit surface, in WorldTraceHit contact, bool exitCrossing)
        {
            if (_pendingMarks.Count < GunShotBallistics.MaximumRecordedImpacts)
            {
                _pendingMarks.Add(new PendingMark(surface, BulletMarks.SurfaceOf(contact), exitCrossing, contact.Placement));
            }
        }
    }

    /// <summary>Marks the impacts one round recorded, with the flags the original gives them.</summary>
    /// <param name="deflected">Whether the round glanced at any point.</param>
    /// <remarks>
    /// <c>0x47C262</c>: every impact is an exit once the round has glanced. Otherwise each is an entry
    /// except the last, which is an exit when its surface faced away from the round.
    /// </remarks>
    private void CommitMarks(bool deflected)
    {
        if (Materials is not { } table)
        {
            _pendingMarks.Clear();
            return;
        }

        for (int i = 0; i < _pendingMarks.Count; i++)
        {
            PendingMark pending = _pendingMarks[i];
            bool isEntry = !deflected && (i < _pendingMarks.Count - 1 || !pending.Hit.IsExit);
            Marks.TryAdd(pending.Hit, pending.Surface, table, isEntry, pending.ExitCrossing, MarkProbe, pending.Placement);
        }

        _pendingMarks.Clear();
    }

    /// <summary>How far the knife reaches, in world units: <c>0x46E033</c> scales the blade by 8192.</summary>
    internal const double KnifeReachInUnits = 8192.0;

    /// <summary>The blade angles <c>KnifeStab</c> tries, in order, in radians about the view's up axis.</summary>
    /// <remarks>
    /// <c>0x46DF70</c> starts at <c>-0.2617994</c> (-15 degrees) and adds <c>0.17453294</c> (10 degrees)
    /// twice, stopping at the first angle that lands.
    /// </remarks>
    internal static readonly float[] KnifeFanRadians = [-0.2617994f, -0.2617994f + 0.17453294f, -0.2617994f + (2f * 0.17453294f)];

    /// <summary>The direction of one blade angle.</summary>
    /// <param name="forward">The view's forward (+Y) axis.</param>
    /// <param name="right">The view's right (+X) axis.</param>
    /// <param name="angle">The angle, in radians.</param>
    /// <returns>The blade direction.</returns>
    /// <remarks>
    /// <c>0x4B3790</c> post-multiplies the view by the Z rotation <c>0x4B3BE0</c> builds, and the stab
    /// takes the result's middle column: <c>cos a * forward - sin a * right</c>. A negative angle
    /// therefore swings the blade to the right.
    /// </remarks>
    internal static Vector3d KnifeBladeDirection(Vector3f forward, Vector3f right, float angle)
    {
        double c = Math.Cos(angle);
        double s = Math.Sin(angle);
        return new Vector3d(
            (c * forward.X) - (s * right.X),
            (c * forward.Y) - (s * right.Y),
            (c * forward.Z) - (s * right.Z));
    }

    /// <summary>Resolves a knife stab the way <c>KnifeStab</c> (<c>0x46DFB0</c>) does.</summary>
    /// <remarks>
    /// <para>
    /// The knife is not a gun ray. Its task sweeps the blade across <see cref="KnifeFanRadians"/>, a
    /// <see cref="KnifeReachInUnits"/> cast at each, and hands the first thing that answers the hit
    /// message that message and nothing else: it never records an impact, so there is no
    /// <c>GunShotEffect</c>, no mark, no impact sound and no dust.
    /// </para>
    /// <para>
    /// A body answers, so it ends the sweep. The port cannot ask a wall whether its type answers, so a
    /// wall does not end it; the first wall met is reported, flagged <see cref="GunImpact.Melee"/>, only
    /// when no angle reached a body -- that is what lets a stab break a pane or silence a speaker.
    /// </para>
    /// </remarks>
    private void ResolveStab(GunProjectile shot, Vector3f right, float damageFactor, WeaponCategory category)
    {
        Vector3f forward = shot.Direction;
        Vector3d origin = shot.Origin;
        float range = (float)KnifeReachInUnits;
        GunImpact? firstWall = null;

        foreach (float angle in KnifeFanRadians)
        {
            Vector3d direction = KnifeBladeDirection(forward, right, angle);
            Vector3d end = origin + (direction * KnifeReachInUnits);
            Vector3f travel = new((float)direction.X, (float)direction.Y, (float)direction.Z);

            WorldTraceHit contact = default;
            bool struckWall = Walls is not null
                && Walls.TryTrace(new WorldTraceRequest(origin, end, WorldTraceMask.World), out contact);

            // A body behind the wall is out of reach: the blade stops where the wall is. The stab is no
            // GunShot round, so nobody hears a near miss (0x4606F0 walks only the round list).
            Vector3d reach = struckWall ? contact.Position : end;
            if (Humans is { } humans
                && humans(
                    origin, reach, damageFactor, category, double.PositiveInfinity, alreadyHeard: true, out HumanHit human)
                && !human.IsExit)
            {
                HumanHits++;
                LastHumanZone = human.Zone;
                _impacts.Add(new GunImpact(
                    human.Position, true, range, StruckHuman: true, Direction: travel, Melee: true));
                return;
            }

            if (struckWall && firstWall is null)
            {
                firstWall = new GunImpact(
                    contact.Position,
                    true,
                    range,
                    contact.Owner,
                    contact.Placement,
                    Wall: contact.RayHit,
                    Direction: travel,
                    Melee: true);
            }
        }

        _impacts.Add(firstWall ?? new GunImpact(
            origin + (new Vector3d(forward.X, forward.Y, forward.Z) * KnifeReachInUnits),
            false,
            range,
            Direction: forward,
            Melee: true));
    }

    private readonly record struct PendingMark(
        WorldRayHit Hit,
        object? Surface,
        bool ExitCrossing,
        WorldPlacement? Placement);

    /// <summary>Mirrors a direction about a surface normal, as the deflection at <c>0x47BC8E</c> does.</summary>
    internal static Vector3d Reflect(Vector3d direction, Vector3f normal)
    {
        double dot = (direction.X * normal.X) + (direction.Y * normal.Y) + (direction.Z * normal.Z);
        Vector3d reflected = new(
            direction.X - (2.0 * dot * normal.X),
            direction.Y - (2.0 * dot * normal.Y),
            direction.Z - (2.0 * dot * normal.Z));
        double length = Math.Sqrt(
            (reflected.X * reflected.X) + (reflected.Y * reflected.Y) + (reflected.Z * reflected.Z));
        return length > 0.0
            ? new Vector3d(reflected.X / length, reflected.Y / length, reflected.Z / length)
            : direction;
    }

    /// <summary>Where the next cast starts — five millimetres past the face, so it cannot meet it again.</summary>
    private static Vector3d StepOff(Vector3d position, Vector3d direction) =>
        new(
            position.X + (direction.X * GunShotBallistics.PenetrationStepOffUnits),
            position.Y + (direction.Y * GunShotBallistics.PenetrationStepOffUnits),
            position.Z + (direction.Z * GunShotBallistics.PenetrationStepOffUnits));
}
