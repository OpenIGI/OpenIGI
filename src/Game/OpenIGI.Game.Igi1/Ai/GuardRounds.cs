using OpenIGI.Game.Hosting;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>A guard's rounds, made casts: against the player's body, the soldiers behind it and the world.</summary>
/// <remarks>
/// The guard raised the trigger and the weapon's cadence let one out. The AI's hit probability decides whether the round
/// is on target -- the original hands that same figure to the gun with every shot, so a miss is a round
/// that went somewhere else rather than a round that never existed.
/// </remarks>
public sealed class GuardRounds
{
    private readonly SoldierSimulation _soldiers;
    private readonly WorldTraceCast _trace;
    private readonly MissionWorld _mission;
    private readonly Igi1PlayerHarm _harm;
    private readonly Igi1Blasts _blasts;
    private readonly Igi1HumanSounds _sounds;
    private readonly Igi1ImpactEffects _impacts;
    private readonly IGuardRoundHost _host;
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="GuardRounds"/> class.</summary>
    /// <param name="soldiers">The soldiers, who fire the rounds and may take the ones that pass the player.</param>
    /// <param name="trace">Where the world stops a round.</param>
    /// <param name="mission">The mission's world: the player, its health and the shot stream.</param>
    /// <param name="harm">What a hit does to the player.</param>
    /// <param name="blasts">What a round's contact does to a speaker or a destructible.</param>
    /// <param name="sounds">The sound of the hits.</param>
    /// <param name="impacts">The material's own effects where a marched round lands.</param>
    /// <param name="host">The burst of the hits.</param>
    /// <param name="logger">Told about every hit on the player.</param>
    public GuardRounds(
        SoldierSimulation soldiers,
        WorldTraceCast trace,
        MissionWorld mission,
        Igi1PlayerHarm harm,
        Igi1Blasts blasts,
        Igi1HumanSounds sounds,
        Igi1ImpactEffects impacts,
        IGuardRoundHost host,
        IOpenIgiLogger logger)
    {
        _impacts = impacts ?? throw new ArgumentNullException(nameof(impacts));
        _mission = mission ?? throw new ArgumentNullException(nameof(mission));
        _harm = harm ?? throw new ArgumentNullException(nameof(harm));
        _blasts = blasts ?? throw new ArgumentNullException(nameof(blasts));
        _sounds = sounds ?? throw new ArgumentNullException(nameof(sounds));
        _soldiers = soldiers ?? throw new ArgumentNullException(nameof(soldiers));
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets how many rounds the guards have fired this mission.</summary>
    public int Fired { get; private set; }

    /// <summary>Gets how many of them struck the player.</summary>
    public int HitsOnPlayer { get; private set; }

    /// <summary>Hands the soldiers the world their guns' <c>GunShot</c> tasks march through.</summary>
    /// <remarks>
    /// The soldiers' rounds as the original marches them: each gun's <c>GunShot</c> task casts its rounds after the level's
    /// walk, through the world, the soldiers and the player, drawing from the level's one stream.
    /// </remarks>
    public void AttachGunShotWorld()
    {
        _impacts.Runtime.UseRandom(_mission.SoundRandom);
        _soldiers.SetGunShotWorld(new MarchedWorld(this), new GunShotSoundWindows(), _impacts.Runtime.DetonatePendingChips);
    }

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

        // A soldier's round is already in its gun's GunShot task, to be marched after the walk; all that is left here is
        // the streak it draws.
        if (shooter is not null && _soldiers.MarchesRounds)
        {
            Vector3d streakTo = _trace(new WorldTraceRequest(from, to, WorldTraceMask.World), out WorldTraceHit surface)
                ? surface.Position
                : to;
            SpawnStreak(player, from, streakTo, weapon);
            return;
        }

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

        // Where the world stops this ray, over its whole length -- one walk serves cover,
        // fly-pasts and outright misses alike, and EVERY round that meets a surface leaves
        // the same mark a player's round does. In retail a miss is visible: the round lands
        // on the wall or the dirt near you (10.4's decals) -- rounds that silently vanished
        // are why the guards seemed to hit with every bullet.
        // A vehicle's gun casts with the vehicle as its owner (0x413D10 hands Gun_Fire's
        // owner through), so its own hull never stops the round.
        //
        // It is a WALK, not one cast: 0x47B750 marches every round, the player's or a guard's,
        // through each surface its power beats (GunShotBallistics.ResolveEntry). Stopping at the
        // first surface had the mission-1 water-tower sniper empty its rifle into its own
        // railing -- Fence, resistance 0, against the Dragunov's power 3. The probe saw all 18 of
        // retail's rounds strike that railing 4.3 m out and carry on, 15 of them to hit; the
        // port's 37 all stopped there. A glance ends the walk here: the reflected leg is not
        // followed. Distances are along the whole round, from the muzzle.
        List<WorldTraceHit> passed = [];
        bool worldBlocked = false;
        bool worldDeflected = false;
        WorldTraceHit blocker = default;
        {
            Vector3d line = to - from;
            double length = line.Length;
            Vector3d direction = length > 0.0 ? line / length : default;
            Vector3d origin = from;
            double travelled = 0.0;
            for (int pass = 0; pass < GunShotBallistics.MaximumRecordedImpacts && travelled < length; pass++)
            {
                if (!_trace(
                    new WorldTraceRequest(origin, to, WorldTraceMask.World, IgnoredOwner: _mission.VehicleRoundOwner),
                    out WorldTraceHit surface))
                {
                    break;
                }

                surface = surface with { Distance = travelled + surface.Distance };
                GunShotImpact outcome = _mission.Materials is { } faces && faces.TryGet(surface.Material, out GameMaterial face)
                    ? GunShotBallistics.ResolveEntry(
                        weapon.PenetrationPower,
                        face,
                        namedObject: surface.Owner is not null,
                        GunShotBallistics.IncidenceRadians(direction, surface.Normal),
                        alreadyDeflected: false,
                        () => (float)random.NextDouble())
                    : GunShotImpact.Stopped;
                if (outcome != GunShotImpact.Penetrated)
                {
                    worldBlocked = true;
                    worldDeflected = outcome == GunShotImpact.Deflected;
                    blocker = surface;
                    break;
                }

                passed.Add(surface);
                travelled = surface.Distance + GunShotBallistics.PenetrationStepOffUnits;
                origin = from + (direction * travelled);
            }
        }

        WorldRayHit worldHit = worldBlocked ? blocker.RayHit : default;

        // A round that meets no hostile body falls through to the soldiers behind the
        // player -- an opposing soldier for a guard, a guard for a friendly. This is the
        // NPC-vs-NPC half of the gate fight: the round is offered to every soldier on the
        // ray, nearest first, and the first one it meets (per its own probability roll)
        // takes it. True when a soldier consumed the round; false when it flew past them all.
        bool SoldiersTakeTheRound()
        {
            if (!_soldiers.TryShootAiRound(
                from, to, probability, weapon, shooter, worldBlocked ? worldHit : null, out HumanHit taken))
            {
                return false;
            }

            MarkPassed(taken.Distance);
            return true;
        }

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
        SpawnStreak(player, from, worldBlocked ? worldHit.Position : to, weapon);

        void MarkTheImpact()
        {
            MarkPassed(double.MaxValue);
            if (!worldBlocked)
            {
                return;
            }

            MarkSurface(blocker);
            if (worldDeflected)
            {
                _sounds.PlayRicochet(blocker.Position);
            }
        }

        // The surfaces the round went through before the distance it ended at, marked once each.
        void MarkPassed(double upTo)
        {
            foreach (WorldTraceHit surface in passed)
            {
                if (surface.Distance < upTo)
                {
                    MarkSurface(surface);
                }
            }

            passed.RemoveAll(surface => surface.Distance < upTo);
        }

        void MarkSurface(in WorldTraceHit surface)
        {
            WorldRayHit face = surface.RayHit;
            if (_mission.Materials is { } table)
            {
                player.Marks.TryAdd(
                    face,
                    BulletMarks.SurfaceOf(surface),
                    table,
                    probe: player.MarkProbe,
                    placement: surface.Placement);
            }

            _sounds.PlayBulletImpact(
                face.Position,
                struckHuman: false,
                surfaceMaterial: face.Material);

            // A guard's stray round pops a speaker exactly as the player's does -- the
            // retail damage message never asked who fired.
            _blasts.ApplyRoundContact(
                face.Position,
                surface.Owner,
                surface.Placement,
                weapon.DamageFactor);

            // A guard's round kicks the same burst a player's does -- misses landing
            // around you are half of what sells incoming fire.
            _host.SpawnImpactBurst(face);
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
        MarkPassed(hit.Distance);

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

        // The round's WeaponHit to the player (HumanPlayer's handler, 0x412440: the damage, the grunt, the shake, the
        // flash and the shove). Returns whether it killed the player.
        bool StrikePlayer(HumanHit struck) => StrikePlayerWith(struck, from, to, weapon, weapon.DamageFactor, shooter);
    }

    // The round's WeaponHit to the player (HumanPlayer's handler, 0x412440: the damage, the grunt, the shake, the flash and
    // the shove). Returns whether it killed the player.
    private bool StrikePlayerWith(HumanHit struck, Vector3d from, Vector3d to, WeaponType weapon, float damageFactor, SoldierView? shooter)
    {
        HumanHealth health = _mission.PlayerHealth;
        HitsOnPlayer++;
        Igi1WeaponHit round = new(struck, from, to, damageFactor, weapon.Category, shooter, FromPlayer: false);
        if (_mission.PlayerTask is { } task)
        {
            task.WeaponHit(round.ToShared());
        }
        else
        {
            // A level that declares no HumanPlayer still has the player the opening camera stood.
            _mission.PlayerEvents?.TakeRound(round);
        }

        _logger.Information(
            "Weapon",
            $"A guard's {weapon.ScriptId} hit the player in the {HumanDamage.ZoneName(struck.Zone)} "
                + $"({HitsOnPlayer} of {Fired} round(s); "
                + $"{health.RemainingFraction:P0} left).");

        if (health.IsDead)
        {
            _logger.Information(
                "Weapon",
                $"The player is DEAD after {HitsOnPlayer} hit(s) from {Fired} round(s), "
                    + $"struck in zone {struck.Zone}; the death camera has pulled back.");
        }

        return health.IsDead;
    }

    /// <summary>What a contact on a soldier's body carries to the strike.</summary>
    private sealed record SoldierContact(SoldierView View, HumanHit Hit);

    /// <summary>What a contact on the player's body carries to the strike.</summary>
    private sealed record PlayerContact(HumanHit Hit);

    /// <summary>The world a soldier's marched round is cast through: placed objects and terrain, the soldiers, the player.</summary>
    private sealed class MarchedWorld(GuardRounds rounds) : IGunShotWorld
    {
        public bool Cast(GunShotRound round, Vector3d start, Vector3d limit, out GunShotContact contact)
        {
            contact = default;
            bool found = false;
            double best = double.PositiveInfinity;
            MissionWorld mission = rounds._mission;
            SoldierView? shooter = round.Shooter as SoldierView;
            int side = shooter?.Soldier.Team ?? 1;

            if (rounds._trace(
                    new WorldTraceRequest(
                        start, limit, WorldTraceMask.World, IgnoredOwner: mission.VehicleRoundOwner, RetailArithmetic: true),
                    out WorldTraceHit surface))
            {
                GameMaterial? row = mission.Materials is { } rows && rows.TryGet(surface.Material, out GameMaterial face)
                    ? face
                    : null;
                contact = new GunShotContact(
                    surface.Position,
                    surface.Normal,
                    surface.Distance,
                    surface.IsExit ? null : row,
                    surface.IsExit ? row : null,
                    HasObject: surface.Owner is not null,
                    StruckHuman: false,
                    surface);
                best = surface.Distance;
                found = true;
            }

            if (rounds._soldiers.TryCastRoundAtSoldiers(start, limit, shooter, side, out SoldierView? struck, out HumanHit body)
                && struck is not null
                && body.Distance < best)
            {
                contact = BodyContact(body, new SoldierContact(struck, body));
                best = body.Distance;
                found = true;
            }

            // The player is visible to a round only from another side, and only through the evaluate's two rolls of the
            // shooter's probability: the visit's gate and the narrow contact's (0x45F53D, 0x4D76DA).
            if (mission.Player is { } player
                && !mission.PlayerHealth.IsDead
                && side != rounds._soldiers.PlayerTeam
                && rounds._soldiers.TryCastPlayerBody(start, limit, player.Body.Position, player.Body.Yaw, out IReadOnlyList<HumanBodyCrossing> crossings)
                && crossings.Count > 0
                && crossings[0].Hit.Distance < best
                && mission.SoundRandom.NextDouble() <= round.Probability
                && mission.SoundRandom.NextDouble() <= round.Probability)
            {
                HumanBodyCrossing first = crossings[0];
                HumanHit hit = first.Hit with { Material = first.Material, IsExit = !first.Entering };
                contact = BodyContact(hit, new PlayerContact(hit));
                found = true;
            }

            return found;
        }

        public void Strike(GunShotRound round, in GunShotContact contact, Vector3f travel)
        {
            Vector3d end = new(round.Origin.X + travel.X, round.Origin.Y + travel.Y, round.Origin.Z + travel.Z);
            switch (contact.Payload)
            {
                case SoldierContact soldier:
                    rounds._soldiers.StrikeWithRound(
                        soldier.View, soldier.Hit, round.Origin, end, round.Weapon, round.DamageFactor, round.Shooter as SoldierView);
                    break;
                case PlayerContact player:
                    rounds.StrikePlayerWith(player.Hit, round.Origin, end, round.Weapon, round.DamageFactor, round.Shooter as SoldierView);
                    break;
                case WorldTraceHit surface:
                    rounds._blasts.ApplyRoundContact(surface.Position, surface.Owner, surface.Placement, round.DamageFactor);
                    break;
            }
        }

        public void PlaySound(string definition, Vector3d position) => rounds._sounds.PlayMarchedRoundSound(definition, position);

        public AmmoType? AmmunitionOf(WeaponType weapon) =>
            rounds._mission.AmmoTypes is { } table && table.TryGetByCalibre(weapon.CalibreId, out AmmoType found) ? found : null;

        public void Effect(GunShotRound round, in GunShotRecord record)
        {
            if (record.Contact.Payload is not WorldTraceHit surface)
            {
                rounds._impacts.RunGunShotEffect(
                    record.Contact.Position, record.Contact.Normal, record.Vector, record.Effect, struckHuman: true);
                return;
            }

            if (rounds._mission.Player is { } player && rounds._mission.Materials is { } table)
            {
                player.Marks.TryAdd(
                    surface.RayHit,
                    BulletMarks.SurfaceOf(surface),
                    table,
                    record.IsEntry,
                    record.ExitCrossing,
                    probe: player.MarkProbe,
                    placement: surface.Placement);
            }

            rounds._impacts.RunGunShotEffect(surface.Position, surface.Normal, record.Vector, record.Effect, struckHuman: false);

            // The remaster's own burst beside it, from its own stream.
            if (!record.ExitCrossing)
            {
                rounds._host.SpawnImpactBurst(surface.RayHit);
            }
        }

        private GunShotContact BodyContact(in HumanHit hit, object payload)
        {
            GameMaterial? row = null;
            if (rounds._mission.Materials is { } rows)
            {
                if (hit.Material >= 0 && rows.TryGet(hit.Material, out GameMaterial skin))
                {
                    row = skin;
                }
                else if (rows.TryGetByName(BulletImpactSounds.FleshMaterialName, out GameMaterial flesh))
                {
                    row = flesh;
                }
            }

            return new GunShotContact(
                hit.Position,
                hit.Normal,
                hit.Distance,
                hit.IsExit ? null : row,
                hit.IsExit ? row : null,
                HasObject: true,
                StruckHuman: true,
                payload);
        }
    }

    // The streak a soldier's round draws, to where the world stops it: visual only.
    private void SpawnStreak(PlayerCamera player, Vector3d from, Vector3d end, WeaponType weapon)
    {
        if (!GameplayCVars.Ballistics.Value || !GameplayCVars.BallisticsAiTracers.Value)
        {
            return;
        }

        Vector3d line = end - from;
        double length = line.Length;
        if (length <= 1e-6)
        {
            return;
        }

        AmmoType? ammo = _mission.AmmoTypes is { } table && table.TryGetByCalibre(weapon.CalibreId, out AmmoType found)
            ? found
            : null;
        player.Ballistics.SpawnVisual(
            from,
            new Vector3f((float)(line.X / length), (float)(line.Y / length), (float)(line.Z / length)),
            end,
            weapon,
            ammo);
    }
}
