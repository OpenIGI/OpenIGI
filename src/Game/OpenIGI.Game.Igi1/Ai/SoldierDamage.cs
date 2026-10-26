using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// What rounds, blades and blasts do to the soldiers: the damage, the flinch and the grunt, the blood, and the death
/// state a killing hit puts a body into.
/// </summary>
/// <remarks>
/// A round marched by a soldier's gun only marks the body it kills: its machine takes the death on its next update
/// (<see cref="TryDieOfPendingRound"/>). Every other hit -- another game's round, a blade, a blast -- kills there and then,
/// as the original's handlers do.
/// </remarks>
/// <param name="context">The soldiers, their brains, health and combat rows.</param>
/// <param name="footsteps">The soldiers' steps, whose cursor a dying soldier gives up.</param>
/// <param name="scriptSound">Plays a definition at a position, attached or not.</param>
internal sealed class SoldierDamage(SoldierContext context, SoldierFootsteps footsteps, Action<string, Vector3d, Func<Vector3d>?> scriptSound)
{
    // The blade's own sound, which 0x45D3C0 adds to the grunt when a knife lands.
    private const string KnifeStabSound = "stab_01";

    private readonly SoldierContext _context = context;

    private readonly SoldierFootsteps _footsteps = footsteps;

    private readonly Action<string, Vector3d, Func<Vector3d>?> _scriptSound = scriptSound;

    private readonly Dictionary<SoldierView, HumanRecoil> _recoil = [];

    private readonly Dictionary<SoldierView, int> _hitQuietUntil = [];

    // Soldiers a round killed since their last update. The damage only marks the body dead; its machine takes the
    // death state -- the clip, the fall and the cry (0x45C440) -- on the soldier's next update, after its AI has run
    // once more on the damage event.
    private readonly Dictionary<SoldierView, PendingDeath> _pendingDeaths = [];

    private int _deathsSoFar;

    /// <summary>Gets or sets what is told as a soldier falls dead: where its body lies and how, for the corpse that is drawn.</summary>
    public Action<SoldierFall>? BodyFell { get; set; }

    /// <summary>Gets or sets where a soldier's death cry is sounded, at the position the body fell from.</summary>
    public Action<Vector3d>? DeathSound { get; set; }

    /// <summary>Gets or sets where a dead soldier's weapon is published as it becomes a world pickup.</summary>
    public Action<WeaponType, Vector3d, Matrix3f, Vector3d>? WeaponDrop { get; set; }

    /// <summary>Gets or sets where a struck soldier's grunt is sounded.</summary>
    public Action<string, int, Vector3d>? HitSound { get; set; }

    /// <summary>Gets or sets what draws how long a struck soldier stays quiet for, in ticks.</summary>
    public Func<int>? HitCooldown { get; set; }

    /// <summary>Gets or sets where a wounded body's blood is thrown: the body, where the round entered, and its direction.</summary>
    public Action<object, Vector3d, Vector3f>? BloodSpray { get; set; }

    /// <summary>Chooses the simulation weapon that a dead soldier drops, independent of its drawn model.</summary>
    public static WeaponType? WeaponForDrop(WeaponType? carriedWeaponType, WeaponType? _renderedWeaponType) =>
        carriedWeaponType;

    /// <summary>What a gun leaves a dying hand with: the body's own motion plus a share of the round's knock.</summary>
    /// <param name="carried">The body's motion, units per tick, in world space.</param>
    /// <param name="travel">The killing round's line.</param>
    /// <returns>The launch velocity, units per tick.</returns>
    /// <remarks>
    /// The grip opens as the round lands, so the gun keeps what the body was doing and takes a small
    /// push along the shot -- sixty centimetres a second, a sideways stumble rather than a throw -- with a
    /// little lift from the arm flinching. Only the realistic drop reads it; the original's ignores it.
    /// </remarks>
    public static Vector3d DropVelocity(Vector3d carried, Vector3d travel)
    {
        const double knock = 0.6 * 4096.0 / 30.0;
        const double lift = 0.3 * 4096.0 / 30.0;
        Vector3d flat = new(travel.X, travel.Y, 0.0);
        double length = flat.Length;
        Vector3d push = length > 1e-6 ? flat * (knock / length) : default;
        return carried + push + new Vector3d(0.0, 0.0, lift);
    }

    /// <summary>Forgets the deaths no update has taken yet, for a new level.</summary>
    public void ClearPendingDeaths() => _pendingDeaths.Clear();

    /// <summary>Puts a soldier into the general death state — <c>0x45C440</c>, joined up.</summary>
    /// <remarks>
    /// <para>
    /// Four things happen here and every one of them is read. The body <b>turns to the round</b>
    /// (<c>0x45DAA0</c>); the death <b>animation is chosen</b> from the killing weapon's category, the zone
    /// struck and which way the round came (<see cref="SoldierDeath.Choose"/>); the <b>fall solver</b>
    /// fans its nine rays and returns the corrective velocity (<see cref="SoldierDeathFall"/>); and the
    /// brain is told, so it steps the body every tick from then on instead of freezing it.
    /// </para>
    /// </remarks>
    public void Die(
        SoldierView view,
        HumanHit hit,
        Vector3d origin,
        Vector3d end,
        WeaponCategory category,
        SoldierDeathMode mode)
    {
        Matrix3f orientation = view.Orientation;
        Vector3f forward = new(orientation.M12, orientation.M22, orientation.M32);

        HumanSoldierClass soldierClass = view.Soldier.Class;
        _context.Brains.TryGetValue(view, out Igi1HumanAi? brain);
        int scriptOverride = brain?.DeathAnimationOverride ?? -1;
        Vector3d travel = new(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z);
        // The heading the body has now. The authored one (the spawn's gamma) snapped every soldier that died back
        // to the way it was placed.
        float facing = brain?.Facing ?? MathF.Atan2(orientation.M21, orientation.M11);

        int animation;
        if (brain?.Body.PostureDeathAnimation is { } postureDeath)
        {
            // A kneeling or prone body dies in its posture's own state (0x45C5A0, 0x45C650), which plays a
            // fixed clip and does not turn.
            animation = postureDeath;
        }
        else
        {
            // 0x45C440 turns the body to the round (0x45DAA0) only in its thrown branch, which the script's
            // override skips: a crumple falls with the facing it died with.
            bool front = true;
            if (scriptOverride == -1 && soldierClass.TurnsToTheShot(mode))
            {
                front = SoldierDeath.TurnToTheShot(travel, forward, ref facing);
            }

            // 0x45C440's coin, drawn from the level's stream unless the script named the death clip.
            int flip = scriptOverride == -1 ? (int)_context.AiRandom.NextBounded(2) : _deathsSoFar++ & 1;
            animation = soldierClass.ChooseDeathAnimation(mode, hit.Zone, front, flip, scriptOverride);
        }

        Vector3d fall = default;
        if (animation >= 0 && view.Bones.Set.TryGet(animation, out BoneAnimation clip) && _context.Cast is { } cast)
        {
            // The nine rays leave from Human+32 -- the origin the last tick left, not the feet -- at 0.8 m below it, 0.4 m
            // below and level (0x45D5B0). From the feet they started underground and met nothing, and no corpse was held
            // off a wall.
            fall = SoldierDeathFall.Solve(
                clip,
                _context.FindAgent(view)?.OriginBeforeTick ?? SoldierContext.OccupantOrigin(view),
                facing,
                (from, to) => cast(from, to, out WorldRayHit blocked)
                    ? new SoldierDeathRay(true, blocked.Position, Vector3d.Distance(from, blocked.Position))
                    : default);
        }

        // The gun leaves from where it hangs (Gun_Drop hands GunPickup_Create the gun's own position, gun+32): the origin
        // the last tick left plus the hand's offset in the palette as last built.
        SoldierAgent? dying = _context.FindAgent(view);
        WeaponType? droppedWeapon = dying is null ? null : WeaponForDrop(dying.CarriedWeaponType, dying.WeaponType);
        Vector3d dropPosition = view.Position;
        Matrix3f dropRotation = view.Orientation;
        if (droppedWeapon is not null && dying is not null)
        {
            dropPosition = dying.OriginBeforeTick + dying.GunOffset;
            BonePose pose = view.UnrotatedPose();
            if (HumanAttachPoints.ThirdPersonWeapon < pose.BoneCount)
            {
                dropRotation = view.Orientation * pose.Rotations[HumanAttachPoints.ThirdPersonWeapon];
            }
        }

        view.PlaceAt(view.Position, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));
        view.Kill(animation);

        if (brain is not null)
        {
            brain.Kill(fall, facing);
        }

        // And the death cry, once, on the alive-to-dead edge: the death state's entry (0x45C440 -> 0x4638A0).
        DeathSound?.Invoke(view.Position);
        _footsteps.Forget(view);

        // Everything above is the original's, and it stays that way whatever follows: the body's
        // authoritative position, the clip that moves it and what the guards around it are told are all
        // exactly what they were. The ragdoll its renderer seeds from the fall replaces THE POSE THAT IS
        // DRAWN, and nothing else.
        if (_context.FindAgent(view) is { } agent)
        {
            Vector3f carried = view.RootMotion();
            double grounded = _context.SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
            Vector3d ragdollOrigin = new(view.Position.X, view.Position.Y, grounded + view.RootSample.Z);

            // What the body was already doing. A soldier's clip root motion is its only velocity -- the port gives
            // one nothing else -- and it is a per-tick delta already, so it goes in as it stands, turned out of body
            // space by the heading.
            Vector3d velocity = SoldierDeathFall.RotateAboutZ(new Vector3d(carried.X, carried.Y, carried.Z), facing);
            BodyFell?.Invoke(new SoldierFall(view, ragdollOrigin, hit.Bone, travel, category, velocity, droppedWeapon is not null));

            if (droppedWeapon is { } dropped)
            {
                SoldierSimulation.WeaponDropTraced?.Invoke(view.Soldier.Id, dropPosition, view.Position, view.RootSample.Z);
                WeaponDrop?.Invoke(dropped, dropPosition, dropRotation, DropVelocity(velocity, travel));
            }
        }
    }

    /// <summary>Enters the death state a marched round left a soldier in, on the update after it struck.</summary>
    /// <param name="view">The soldier updating.</param>
    /// <returns>Whether a round had killed it.</returns>
    /// <remarks>
    /// The machine sees the dead bit the killing round set and enters the death state (<c>0x45C440</c>). The entry only
    /// starts the clip: the death state's per-tick (<c>0x45C700</c>), which moves the body, runs from the next tick.
    /// </remarks>
    public bool TryDieOfPendingRound(SoldierView view)
    {
        if (!_pendingDeaths.Remove(view, out PendingDeath death))
        {
            return false;
        }

        Die(view, death.Hit, death.Origin, death.End, death.Category, SoldierDeath.ModeFor(death.Category));
        return true;
    }

    /// <summary>A round's entry into a soldier: the Human's gunshot handler (<c>0x45F930</c>).</summary>
    /// <param name="struck">The soldier.</param>
    /// <param name="hit">The entry.</param>
    /// <param name="origin">The pass's start.</param>
    /// <param name="end">The pass's end.</param>
    /// <param name="weapon">The firing weapon.</param>
    /// <param name="damageFactor">The round's damage factor.</param>
    /// <param name="shooter">The soldier that fired, or <see langword="null"/>.</param>
    /// <remarks>
    /// The blood first (<c>0x45F94A</c>), then the damage handler (<c>0x45D3C0</c>): an invulnerable body (the
    /// mission 4 squad's <c>SetInvulnerability(TRUE)</c>) takes the round and nothing else.
    /// </remarks>
    public void StrikeWithRound(
        SoldierView struck,
        in HumanHit hit,
        Vector3d origin,
        Vector3d end,
        WeaponType weapon,
        float damageFactor,
        SoldierView? shooter)
    {
        ArgumentNullException.ThrowIfNull(struck);
        ArgumentNullException.ThrowIfNull(weapon);
        HumanHealth health = _context.HealthOf(struck);
        if (BloodSpray is { } bleed)
        {
            Vector3d line = end - origin;
            double length = line.Length;
            bleed(
                struck,
                hit.Position,
                length > 1e-6
                    ? new Vector3f((float)(line.X / length), (float)(line.Y / length), (float)(line.Z / length))
                    : default);
        }

        Igi1HumanAi? brain = _context.Brains.TryGetValue(struck, out Igi1HumanAi? b) ? b : null;
        bool invulnerable = brain?.IsInvulnerable ?? false;
        if (health.IsDead)
        {
            // A corpse bleeds and nothing more: it is already dead.
            return;
        }

        if (!invulnerable)
        {
            float dealt = health.Apply(hit.Zone, damageFactor, brain?.IsInstantDeathDisabled ?? false);
            brain?.ReceiveDamage(origin, shooter?.Soldier.Id ?? AiDetectionCandidate.PlayerId);
            if (health.IsDead)
            {
                _pendingDeaths[struck] = new PendingDeath(hit, origin, end, weapon.Category);
            }
            else
            {
                ReactToHit(struck, brain, hit.Zone, dealt, explosion: false);
            }
        }

        SoldierSimulation.DamageTraced?.Invoke(struck.Soldier.Id, hit.Zone, hit.Bone, damageFactor, health.Damage, health.MaximumHealth);
        _context.Logger.Information(
            SoldierContext.LogCategory + ".Damage",
            $"port zone={hit.Zone} ({HumanDamage.ZoneName(hit.Zone)}) bone={hit.Bone} "
                + $"dmgFactor={damageFactor:0.000} invuln={(invulnerable ? 1 : 0)} "
                + $"instDeathDisabled={(brain?.IsInstantDeathDisabled ?? false ? 1 : 0)} "
                + $"accum={health.Damage:0.000} max={health.MaximumHealth:0.000} "
                + $"dead={(health.IsDead ? 1 : 0)}");
    }

    /// <summary>A round or a blade on a soldier, whoever fired it: <c>HumanSoldier</c>'s <c>WeaponHit</c>, <c>0x45D3C0</c>.</summary>
    /// <remarks>
    /// <para>
    /// Invulnerability (<c>HumanAI+15036</c>) skips the whole damage block -- the round still meets the body, it just does
    /// nothing; instant death disabled (<c>+15037</c>) makes a head or neck hit take the plain damage. The blood follows the
    /// damage (<c>0x45D45C</c>), then the death or the flinch and the grunt, and the blade's own sound for a knife.
    /// </para>
    /// <para>
    /// Whatever the round did, the brain is told last: the <c>TAKINGDAMAGE</c> event (type 15) goes out at <c>0x45D577</c>
    /// to an invulnerable soldier as much as to a wounded one.
    /// </para>
    /// </remarks>
    public void TakeRound(SoldierView struckView, Igi1WeaponHit round)
    {
        HumanHealth health = _context.HealthOf(struckView);
        HumanHit hit = round.Hit;
        Igi1HumanAi? brain = _context.Brains.TryGetValue(struckView, out Igi1HumanAi? b) ? b : null;
        bool invulnerable = brain?.IsInvulnerable ?? false;

        // KnifeStab (0x46DFB0) replaces the blade's damage factor with 1000.0 when the stabbed soldier has no fight on
        // (sub_4528F0: HumanAI+15016 clear) -- the silent kill from behind.
        float damageFactor = round.Category == WeaponCategory.Knife && brain is { HasCombatRecord: false }
            ? SoldierSimulation.KnifeUnawareDamageFactor
            : round.DamageFactor;
        if (!invulnerable)
        {
            float dealt = health.Apply(hit.Zone, damageFactor, brain?.IsInstantDeathDisabled ?? false);
            round.Credit?.Hit(hit.Zone == HumanDamage.NeckZone);
            if (BloodSpray is { } bleed)
            {
                Vector3d line = round.End - round.Origin;
                double length = line.Length;
                bleed(
                    struckView,
                    hit.Position,
                    length > 1e-6
                        ? new Vector3f((float)(line.X / length), (float)(line.Y / length), (float)(line.Z / length))
                        : default);
            }

            if (health.IsDead)
            {
                // The brain hears of the round that kills it too: the event is posted to an AI that is not dead yet,
                // and the body falls after.
                brain?.ReceiveDamage(round.Origin, round.Shooter?.Soldier.Id ?? AiDetectionCandidate.PlayerId);
                round.Credit?.Killed(hit.Zone == HumanDamage.NeckZone);
                Die(struckView, hit, round.Origin, round.End, round.Category, SoldierDeath.ModeFor(round.Category));
            }
            else
            {
                ReactToHit(struckView, brain, hit.Zone, dealt, explosion: false);
                if (round.Category == WeaponCategory.Knife && struckView.Soldier.Class.SoundsKnifeStab)
                {
                    _scriptSound(KnifeStabSound, struckView.Position, () => struckView.Position);
                }
            }
        }

        if (!health.IsDead || invulnerable)
        {
            brain?.ReceiveDamage(round.Origin, round.Shooter?.Soldier.Id ?? AiDetectionCandidate.PlayerId);
        }

        // Deliberately the same shape as the probe's [dmg] line from the retail game (probes.cpp
        // OnDamageHandler), so a run of the port and a capture of the original diff directly.
        _context.Logger.Information(
            SoldierContext.LogCategory + ".Damage",
            $"port zone={hit.Zone} ({HumanDamage.ZoneName(hit.Zone)}) bone={hit.Bone} dmgFactor={damageFactor:0.000} "
                + $"invuln={(invulnerable ? 1 : 0)} instDeathDisabled={(brain?.IsInstantDeathDisabled ?? false ? 1 : 0)} "
                + $"accum={health.Damage:0.000} max={health.MaximumHealth:0.000} dead={(health.IsDead ? 1 : 0)}");
    }

    /// <summary>A blow from another soldier's close combat -- the soldiers' punch handler (<c>0x45A5E0</c>, <c>0x457620</c>).</summary>
    /// <param name="struckView">The soldier struck.</param>
    /// <param name="attacker">The soldier who struck.</param>
    /// <param name="damage">The attacker's close-combat damage.</param>
    /// <remarks>
    /// The blow lands on the back (zone 14) when the attacker stands behind the struck soldier's facing, else on the front
    /// (zone 13), scaled by that zone's multiplier. Short of the soldier's maximum it reacts as to a round; at it the soldier
    /// crumples away from the attacker. Either way the brain is told who hit it, an invulnerable one too.
    /// </remarks>
    public void TakePunch(SoldierView struckView, SoldierView attacker, float damage)
    {
        HumanHealth health = _context.HealthOf(struckView);
        if (health.IsDead)
        {
            return;
        }

        Igi1HumanAi? brain = _context.Brains.TryGetValue(struckView, out Igi1HumanAi? b) ? b : null;
        int zone = PunchZone(struckView.Orientation, struckView.Position, attacker.Position);
        if (!(brain?.IsInvulnerable ?? false))
        {
            float dealt = health.Apply(zone, damage, brain?.IsInstantDeathDisabled ?? false);
            if (health.IsDead)
            {
                brain?.ReceiveDamage(attacker.Position, attacker.Soldier.Id);
                Die(
                    struckView,
                    new HumanHit(struckView.Position, 0.0, zone, 0f, 0f),
                    attacker.Position,
                    struckView.Position,
                    WeaponCategory.Knife,
                    SoldierDeathMode.Crumple);
                return;
            }

            ReactToHit(struckView, brain, zone, dealt, explosion: false);
        }

        brain?.ReceiveDamage(attacker.Position, attacker.Soldier.Id);
    }

    /// <summary>Where a blow lands: the back (zone 14) when it comes from behind the struck soldier's facing, else the front (13).</summary>
    /// <param name="facing">The struck soldier's matrix, whose column 1 is its forward.</param>
    /// <param name="struck">Where the struck soldier stands.</param>
    /// <param name="attacker">Where the attacker stands.</param>
    /// <returns>The zone.</returns>
    public static int PunchZone(Matrix3f facing, Vector3d struck, Vector3d attacker)
    {
        Vector3d away = struck - attacker;
        double along = (away.X * facing.M12) + (away.Y * facing.M22) + (away.Z * facing.M32);
        return along >= 0.0 ? HumanDamage.RearZone : HumanDamage.FrontZone;
    }

    /// <summary>Applies one explosion to every living soldier in reach.</summary>
    /// <param name="position">The blast's centre, in world units.</param>
    /// <param name="radiusUnits">The full-damage radius.</param>
    /// <param name="falloffUnits">The taper past it — zero for the shipped grenade.</param>
    /// <param name="damageFactor">The blast's damage factor.</param>
    /// <remarks>
    /// The retail rules through the port's own pieces: the dose is
    /// <see cref="ExplosionDamage.Dose"/> with the body radius at a third, and it lands on
    /// <b>zone 13</b> — an explosion never headshots, however the target stands. A kill goes through
    /// the same death path a bullet's does, with the blast direction as the round.
    /// </remarks>
    public void ApplyExplosion(Vector3d position, float radiusUnits, float falloffUnits, float damageFactor)
    {
        // Every soldier on the explosion list is told (0x45A3D0 is HumanSoldier's ExplosionHit); each decides for itself.
        ExplosionHit blast = new Igi1ExplosionHit(position, radiusUnits, falloffUnits, damageFactor).ToShared();
        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                member.View.Soldier.ExplosionHit(blast);
            }
        }
    }

    /// <summary>One blast on one soldier.</summary>
    public void TakeExplosion(SoldierView soldier, Vector3d position, float radiusUnits, float falloffUnits, float damageFactor)
    {
        if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier))
        {
            return;
        }
        HumanHealth health = _context.HealthOf(soldier);

        if (health.IsDead)
        {
            return;
        }

        Vector3d feet = soldier.Position;
        double distance = Math.Sqrt(
            ((feet.X - position.X) * (feet.X - position.X))
            + ((feet.Y - position.Y) * (feet.Y - position.Y))
            + ((feet.Z - position.Z) * (feet.Z - position.Z)));

        float dose = ExplosionDamage.Dose(
            (float)distance,
            ExplosionDamage.EffectiveBodyRadius((float)HumanSeparation.RadiusInUnits),
            radiusUnits,
            falloffUnits,
            damageFactor);
        if (dose <= 0f)
        {
            return;
        }

        Igi1HumanAi? brain = _context.Brains.TryGetValue(soldier, out Igi1HumanAi? b) ? b : null;
        if (brain?.IsInvulnerable ?? false)
        {
            return;
        }

        // 0x45A3D0 posts no AI event: what a soldier makes of a blast it hears through NotifyExplosion.
        float dealt = health.Apply(ExplosionDamage.Zone, dose, brain?.IsInstantDeathDisabled ?? false);

        if (health.IsDead)
        {
            // 0x45A3D0 records death mode 0 for a blast: the body is thrown, turned to it.
            HumanHit hit = new(feet, distance, ExplosionDamage.Zone, 0f, 0f);
            Die(soldier, hit, position, feet, WeaponCategory.Grenade, SoldierDeathMode.Untouched);
        }
        else
        {
            ReactToHit(soldier, brain, ExplosionDamage.Zone, dealt, explosion: true);
        }
    }

    /// <summary>A non-lethal hit: the soldier's class decides whether it flinches, and it grunts.</summary>
    /// <remarks>
    /// Every hit handler plays the grunt on the non-lethal branch, and an explosion only when it made the
    /// soldier react (<c>0x45A3D0</c>). The flinch is the bit the handlers raise for the body machine.
    /// </remarks>
    private void ReactToHit(SoldierView soldier, Igi1HumanAi? brain, int zone, float dealt, bool explosion)
    {
        if (!_recoil.TryGetValue(soldier, out HumanRecoil? recoil))
        {
            float? configured = _context.Combat.TryGetValue(soldier, out AiCombatParameters? row) ? row.RecoilThreshold : null;
            recoil = soldier.Soldier.Class.CreateRecoil(configured);
            _recoil[soldier] = recoil;
        }

        HumanRecoilOutcome outcome = recoil.Absorb(dealt);
        if (outcome.Flinches)
        {
            brain?.Flinch(zone);
        }

        if (!explosion)
        {
            ReportHitSound(soldier);
        }
        else if (outcome.Reacted)
        {
            ReportHitSound(soldier, HumanVariantSound.AiExplosionHitPrefix, HumanVariantSound.AiExplosionHitVariants);
        }
    }

    /// <summary>Plays a struck soldier's grunt unless it is still inside its cooldown.</summary>
    private void ReportHitSound(
        SoldierView view,
        string prefix = HumanVariantSound.AiHitPrefix,
        int variants = HumanVariantSound.AiHitVariants)
    {
        if (HitSound is not { } hitSound || HitCooldown is not { } hitCooldown)
        {
            return;
        }

        // now >= human[529], then human[529] = now + 30..60. Held per soldier, as the original holds
        // it per human, so one guard's grunt does not silence another's.
        if (_hitQuietUntil.TryGetValue(view, out int quietUntil) && _context.TickCount < quietUntil)
        {
            return;
        }

        _hitQuietUntil[view] = _context.TickCount + hitCooldown();
        hitSound(prefix, variants, view.Position);
    }

    private readonly record struct PendingDeath(HumanHit Hit, Vector3d Origin, Vector3d End, WeaponCategory Category);
}
