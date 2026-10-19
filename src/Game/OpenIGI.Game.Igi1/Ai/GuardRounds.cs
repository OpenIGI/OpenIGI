using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>What a guard's round asks of whatever plays and shows it: the sound and the burst of the hits.</summary>
public interface IGuardRoundHost
{
    /// <summary>Plays a round's impact.</summary>
    /// <param name="position">Where it struck, in world units.</param>
    /// <param name="struckHuman">Whether it struck a body.</param>
    /// <param name="surfaceMaterial">The surface's material, when it struck the world.</param>
    void PlayBulletImpactSound(Vector3d position, bool struckHuman, int surfaceMaterial);

    /// <summary>Kicks the burst of a round landing on a surface.</summary>
    /// <param name="hit">Where it landed.</param>
    void SpawnImpactBurst(in WorldRayHit hit);
}

/// <summary>A guard's rounds, made casts: against the player's body, the soldiers behind it and the world.</summary>
/// <remarks>
/// The guard raised the trigger and the weapon's cadence let one out. The AI's hit probability decides whether the round
/// is on target -- the original hands that same figure to the gun with every shot (ai.md 11.6), so a miss is a round
/// that went somewhere else rather than a round that never existed.
/// </remarks>
public sealed class GuardRounds
{
    private readonly SoldierSimulation _soldiers;
    private readonly WorldTraceCast _trace;
    private readonly MissionWorld _mission;
    private readonly Igi1PlayerHarm _harm;
    private readonly Igi1Blasts _blasts;
    private readonly IGuardRoundHost _host;
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="GuardRounds"/> class.</summary>
    /// <param name="soldiers">The soldiers, who fire the rounds and may take the ones that pass the player.</param>
    /// <param name="trace">Where the world stops a round.</param>
    /// <param name="mission">The mission's world: the player, its health and the shot stream.</param>
    /// <param name="harm">What a hit does to the player.</param>
    /// <param name="blasts">What a round's contact does to a speaker or a destructible.</param>
    /// <param name="host">The sound and the burst of the hits.</param>
    /// <param name="logger">Told about every hit on the player.</param>
    public GuardRounds(
        SoldierSimulation soldiers,
        WorldTraceCast trace,
        MissionWorld mission,
        Igi1PlayerHarm harm,
        Igi1Blasts blasts,
        IGuardRoundHost host,
        IOpenIgiLogger logger)
    {
        _mission = mission ?? throw new ArgumentNullException(nameof(mission));
        _harm = harm ?? throw new ArgumentNullException(nameof(harm));
        _blasts = blasts ?? throw new ArgumentNullException(nameof(blasts));
        _soldiers = soldiers ?? throw new ArgumentNullException(nameof(soldiers));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets how many rounds the guards have fired this mission.</summary>
    public int Fired { get; private set; }

    /// <summary>Gets how many of them struck the player.</summary>
    public int HitsOnPlayer { get; private set; }

    /// <summary>Resolves one round; a <see cref="SoldierSimulation.SoldierRound"/>.</summary>
    /// <param name="from">Where it leaves, in world units.</param>
    /// <param name="at">Where it is aimed, in world units.</param>
    /// <param name="probability">The chance the AI gave it of being on target.</param>
    /// <param name="weapon">What fired it.</param>
    /// <param name="shooter">The soldier that fired.</param>
    public void Resolve(Vector3d from, Vector3d at, float probability, WeaponType weapon, SoldierView? shooter)
    {
        Fired++;
        if (_mission.Player is not { } player)
        {
            return;
        }

        HumanHealth health = _mission.PlayerHealth;
        Random random = _mission.ShotRandom;

        // The ray runs where the GUN points -- the tracked aim (or a patrol's own facing) --
        // extended past the target, exactly as the retail round is a cast from the muzzle to
        // the end of its remaining range (GunShot per-tick 0x47B750). A moving player walks
        // out of the lagging aim and the round geometrically misses; that is retail's miss.
        Vector3d to = new(
            from.X + ((at.X - from.X) * 4.0),
            from.Y + ((at.Y - from.Y) * 4.0),
            from.Z + ((at.Z - from.Z) * 4.0));

        Vector3d feet = player.Body.Position;

        // Which side fired decides who the round may hurt. A guard's round (opposing the
        // player's team) may strike the player; a friendly's may not -- it is aimed at an
        // opposing soldier and a friendly's own gun never endangers the player. Null is
        // defensive: the player's own shots do not flow through this handler, so no real
        // round is hostile-neutral; treating it as hostile keeps a round from vanishing.
        bool shooterHostileToPlayer = shooter is null
            || shooter.Soldier.Team != _soldiers.PlayerTeam;

        // The player's living body is a physical obstacle to every round, whatever the
        // shooter's team -- retail's cast collides with a same-team human too (it just deals
        // no damage). A DEAD player is not: the detection scan unregisters the corpse, so a
        // round passes through where the body fell instead of hanging in the air on it.
        bool playerInTheRay = !health.IsDead;

        // Resolve the guard's round against the player's OWN skinned body (limb hits do less,
        // and the hitbox is the real body, not a fat cylinder). Only if the body is not loaded
        // does it fall back to the cylinder-and-trunk stand-in. Skipped entirely for a corpse.
        HumanHit hit = default;
        bool met = false;
        IReadOnlyList<HumanBodyCrossing> crossings = [];
        if (playerInTheRay)
        {
            if (_soldiers.TryCastPlayerBody(from, to, feet, player.Body.Yaw, out crossings)
                && crossings.Count > 0)
            {
                hit = crossings[0].Hit;
                met = true;
            }

            if (!met)
            {
                Vector3f facing = new(-MathF.Sin(player.Body.Yaw), MathF.Cos(player.Body.Yaw), 0f);
                met = HumanHitTest.TryCast(from, to, feet, facing, pose: null, out hit);
            }
        }

        // Where the world stops this ray, over its whole length -- one cast serves cover,
        // fly-pasts and outright misses alike, and EVERY round that meets a surface leaves
        // the same mark a player's round does. In retail a miss is visible: the round lands
        // on the wall or the dirt near you (10.4's decals) -- rounds that silently vanished
        // are why the guards seemed to hit with every bullet.
        // A vehicle's gun casts with the vehicle as its owner (0x413D10 hands Gun_Fire's
        // owner through), so its own hull never stops the round.
        bool worldBlocked = _trace(
            new WorldTraceRequest(from, to, WorldTraceMask.World, IgnoredOwner: _mission.VehicleRoundOwner),
            out WorldTraceHit blocker);
        WorldRayHit worldHit = worldBlocked ? blocker.RayHit : default;

        // A round that meets no hostile body falls through to the soldiers behind the
        // player -- an opposing soldier for a guard, a guard for a friendly. This is the
        // NPC-vs-NPC half of the gate fight: the round is offered to every soldier on the
        // ray, nearest first, and the first one it meets (per its own probability roll)
        // takes it. True when a soldier consumed the round; false when it flew past them all.
        bool SoldiersTakeTheRound()
            => _soldiers.TryShootAiRound(
                    from, to, probability, weapon, shooter, worldBlocked ? worldHit : null, out _);

        // 0x4606F0 for a round that stops before reaching any soldier's own resolution.
        void OfferNearMiss(Vector3d stop)
        {
            if (shooter is not null)
            {
                _soldiers.NotifyRoundPassed(from, stop, shooter, struck: null);
            }
        }

        // The streak, before the outcome: visual only, along the very ray this handler is
        // resolving, dying where the world stops it. Whether the round connects is decided
        // below exactly as ever -- from inside the head, a streak that reaches the eye and
        // a round that hits look the same.
        if (GameplayCVars.Ballistics.Value && GameplayCVars.BallisticsAiTracers.Value)
        {
            Vector3d streakEnd = worldBlocked ? worldHit.Position : to;
            Vector3d line = streakEnd - from;
            double lineLength = line.Length;
            if (lineLength > 1e-6)
            {
                AmmoType? streakAmmo = _mission.AmmoTypes is { } table
                    && table.TryGetByCalibre(weapon.CalibreId, out AmmoType found)
                    ? found
                    : null;
                player.Ballistics.SpawnVisual(
                    from,
                    new Vector3f(
                        (float)(line.X / lineLength),
                        (float)(line.Y / lineLength),
                        (float)(line.Z / lineLength)),
                    streakEnd,
                    weapon,
                    streakAmmo);
            }
        }

        void MarkTheImpact()
        {
            if (!worldBlocked)
            {
                return;
            }

            if (_mission.Materials is { } table)
            {
                player.Marks.TryAdd(
                    worldHit,
                    BulletMarks.SurfaceOf(blocker),
                    table,
                    probe: player.MarkProbe,
                    placement: blocker.Placement);
            }

            _host.PlayBulletImpactSound(
                worldHit.Position,
                struckHuman: false,
                surfaceMaterial: worldHit.Material);

            // A guard's stray round pops a speaker exactly as the player's does -- the
            // retail damage message never asked who fired.
            _blasts.ApplyRoundContact(
                worldHit.Position,
                blocker.Owner,
                blocker.Placement,
                weapon.DamageFactor);

            // A guard's round kicks the same burst a player's does -- misses landing
            // around you are half of what sells incoming fire.
            _host.SpawnImpactBurst(worldHit);
        }

        if (!met)
        {
            // Geometric miss -- aim lag against a moving target, a running patrol's spray, or
            // a dead player whose corpse stops nothing. The round flies on: offer it to the
            // soldiers, and only then does it land where the world says.
            if (SoldiersTakeTheRound())
            {
                return;
            }
            MarkTheImpact();
            return;
        }

        // The world is in the way: the retail cast meets the nearest surface first, so a wall
        // or a crate between the muzzle and the body simply takes the round (0x47B750 -- the
        // round penetrates or stops per the surface, but it does not reach the player). The
        // small margin keeps the player's own contact from re-testing the surface it stands on.
        if (worldBlocked && worldHit.Distance < hit.Distance - 40.0)
        {
            OfferNearMiss(worldHit.Position);
            MarkTheImpact();
            _logger.Debug(
                "Weapon",
                $"Guard round blocked by {blocker.Kind} '{blocker.Label}' at "
                    + $"{blocker.Fraction:P1} before the player body.");
            return;
        }

        // Only now the probability gate, and it is rolled TWICE: the cast offers the player to
        // the Human's evaluate (0x45E3E0, `random <= probability` for player 0 only) once as the
        // octree visit's gate (0x45F500 -> 0x45F53D) and again for the narrow contact after the
        // 0.94 m sphere test (0x45F580, then the per-shape dispatch -> 0x4D76DA). Measured on
        // Hard at 16 m: 105 rounds, 74 passed the first roll (p 0.695), 30 hit. One roll made
        // the port's guards hit about 1/p times as often. A failed roll is a round that flew
        // past -- through to whatever stands behind the player, where it marks.
        if (random.NextDouble() > probability || random.NextDouble() > probability)
        {
            // The roll failed against the player -- the round flies past to whoever stands
            // behind them: an opposing soldier if the shooter is hostile, a guard if the
            // shooter is a friendly.
            if (SoldiersTakeTheRound())
            {
                return;
            }
            MarkTheImpact();
            return;
        }

        // The round stops at the player: the soldiers it passed on the way still hear it go by.
        OfferNearMiss(hit.Position);

        // The roll connected with the player's body. A friendly's round that meets the
        // player is consumed there with no damage -- retail's cast collides with a same-team
        // human but deals none, and it never flies on to a soldier behind the player. Only a
        // hostile round may hurt the player.
        if (!shooterHostileToPlayer)
        {
            return;
        }

        if (StrikePlayer(hit) || crossings.Count == 0)
        {
            return;
        }

        // The round does not stop at the body (0x47B750). The body's entry face is a surface like any
        // other: the player's handler takes the hit, and the round goes on only if it beats the face's
        // material (power against bullet resistance; the human materials author no ricochet angle).
        // A round that goes on leaves through an exit face and stops at the next surface it enters,
        // after that surface has taken the hit too -- another part of the body behind, which hits
        // the player a second time, or the world beyond. Each cast that meets the body rolls the
        // player's evaluate again. Probed on mission 1 (a guard's power-2 rounds) and mission 7 (the
        // T80's M2HB): pairs of hits from one round.
        if (!Penetrates(crossings[0].Material))
        {
            return;
        }

        bool leftBody = false;
        double next = hit.Distance + GunShotBallistics.PenetrationStepOffUnits;
        for (int index = 1; index < crossings.Count; index++)
        {
            HumanBodyCrossing crossing = crossings[index];
            if (crossing.Hit.Distance < next)
            {
                continue;
            }

            if ((worldBlocked && worldHit.Distance < crossing.Hit.Distance)
                || random.NextDouble() > probability
                || random.NextDouble() > probability)
            {
                break;
            }

            if (!crossing.Entering)
            {
                leftBody = true;
                next = crossing.Hit.Distance + GunShotBallistics.PenetrationStepOffUnits;
                continue;
            }

            if (StrikePlayer(crossing.Hit) || leftBody || !Penetrates(crossing.Material))
            {
                return;
            }

            next = crossing.Hit.Distance + GunShotBallistics.PenetrationStepOffUnits;
        }

        // Past the body the round meets the world, which takes it.
        if (worldBlocked && worldHit.Distance > hit.Distance)
        {
            MarkTheImpact();
        }

        return;

        // Whether the round gets through a face of this material; the equal case draws its sample.
        bool Penetrates(int material)
        {
            if (_mission.Materials is not { } materials || !materials.TryGet(material, out GameMaterial face))
            {
                return false;
            }

            float margin = weapon.PenetrationPower - face.BulletResistance;
            float sample = margin == 0f ? (float)random.NextDouble() : 0f;
            return GunShotBallistics.Penetrates(weapon.PenetrationPower, face.BulletResistance, sample);
        }

        // One call of the player's hit handler (0x412440): the damage, the grunt, the shake, the flash
        // and the shove. Returns whether it killed the player.
        bool StrikePlayer(HumanHit struck)
        {
            health.Apply(struck.Zone, weapon.DamageFactor);
            _host.PlayBulletImpactSound(struck.Position, struckHuman: true, surfaceMaterial: 0);
            HitsOnPlayer++;

            // The retail handler transforms the round into camera space before writing the four
            // corner alphas. It picks 0.4–0.8 rather than one opaque wash and replaces the prior
            // hit, so automatic fire remains a directional cue instead of a full-screen strobe.
            Vector3f incoming = new(
                (float)(to.X - from.X),
                (float)(to.Y - from.Y),
                (float)(to.Z - from.Z));
            Vector3f viewIncoming = player.Camera.Orientation.Transposed().Transform(incoming);

            // 0x412440's order: the grunt, then the shake, then the flash, each on its own draw.
            if (!health.IsDead)
            {
                _harm.PlayPlayerGrunt();
            }

            _harm.InstallPlayerHitShake();
            _mission.DamageFlash.Hit(viewIncoming, _mission.SoundRandom.NextSingle());

            // Every connecting round SHOVES the player 5 km/h along its travel -- the hit
            // handler (0x412440) normalises the round's direction and adds x/y x 189.62962
            // into the slide accumulator, whose 0.9-a-tick damping then bleeds it off. This is
            // what makes sustained fire stagger the player in retail.
            if (!health.IsDead)
            {
                double shoveX = to.X - from.X;
                double shoveY = to.Y - from.Y;
                double shoveZ = to.Z - from.Z;
                double length = Math.Sqrt((shoveX * shoveX) + (shoveY * shoveY) + (shoveZ * shoveZ));
                if (length > 1e-6)
                {
                    player.Body.AddHitShove(
                        (float)(shoveX / length * PlayerBody.HitShoveUnitsPerTick),
                        (float)(shoveY / length * PlayerBody.HitShoveUnitsPerTick));
                }
            }

            _logger.Information(
                "Weapon",
                $"A guard's {weapon.ScriptId} hit the player in the {HumanDamage.ZoneName(struck.Zone)} "
                    + $"({HitsOnPlayer} of {Fired} round(s); "
                    + $"{health.RemainingFraction:P0} left).");

            if (health.IsDead)
            {
                _harm.BeginPlayerDeath(from, struck.Zone, struck.Bone, weapon.Category);
                _logger.Information(
                    "Weapon",
                    $"The player is DEAD after {HitsOnPlayer} hit(s) from {Fired} round(s), "
                        + $"struck in zone {struck.Zone}; the death camera has pulled back.");
            }

            return health.IsDead;
        }
    }
}
