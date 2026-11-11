using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// Project I.G.I.'s player, <c>HumanPlayer</c>: its rig and its life, and what hurts it and how it dies -- a round
/// (<c>0x412440</c>), a blast (<c>0x412990</c>), a soldier's blow (<c>0x412CE0</c>), a fall (<c>0x408460</c>), a powered fence
/// (<c>0x412F80</c>), and the death state every lethal hit enters (state 47, <c>0x40E560</c>).
/// </summary>
/// <remarks>
/// It is the player's task's events (<see cref="IHumanEvents"/>), as <c>0x40FE30</c> links <c>WeaponHit</c> and
/// <c>ExplosionHit</c> to the player's own handlers; whatever level it stands in gives it its sounds, its corpse and its
/// death camera (<see cref="IIgi1PlayerSurroundings"/>).
/// </remarks>
/// <param name="rig">The rig.</param>
/// <param name="life">Its life.</param>
/// <param name="surroundings">What it is hurt and dies in.</param>
public sealed class Igi1HumanPlayer(PlayerCamera rig, Igi1PlayerLife life, IIgi1PlayerSurroundings surroundings) : IHumanEvents
{
    // 0x412CE0's two zones: 13 for a blow from in front, 14 from behind.
    private const int PlayerBluntFrontZone = 13;
    private const int PlayerBluntBackZone = 14;

    // 0x412990's shove, units a tick into the body's slide -- 758.51849.
    private const float BlastShoveUnitsPerTick = 758.51849f;

    // The explosion message's duration (+20), which 0x47F200 fills with 90 ticks.
    private const int ExplosionShakeTicks = 90;

    /// <summary>Gets the rig.</summary>
    public PlayerCamera Rig { get; } = rig ?? throw new ArgumentNullException(nameof(rig));

    /// <summary>Gets its life.</summary>
    public Igi1PlayerLife Life { get; } = life ?? throw new ArgumentNullException(nameof(life));

    private IIgi1PlayerSurroundings Surroundings { get; } = surroundings ?? throw new ArgumentNullException(nameof(surroundings));

    /// <inheritdoc/>
    public bool EvaluateWeaponCollision(HumanTask human, in WeaponCollisionQuery query) => true;

    /// <inheritdoc/>
    public void WeaponHit(HumanTask human, in WeaponHit hit)
    {
        if (hit.Context is Igi1WeaponHit round)
        {
            TakeRound(round);
        }
        else
        {
            // Another game's round: where it meets the player's body is this game's to find.
            TakeRound(Igi1WeaponHit.FromShared(hit, Igi1WeaponHit.Meet(hit, Rig.Body.Position, new Vector3f(-MathF.Sin(Rig.Body.Yaw), MathF.Cos(Rig.Body.Yaw), 0f))));
        }
    }

    /// <inheritdoc/>
    public void ExplosionHit(HumanTask human, in ExplosionHit hit)
    {
        Igi1ExplosionHit blast = hit.Context as Igi1ExplosionHit ?? Igi1ExplosionHit.FromShared(hit);
        Blast(blast.Position, blast.RadiusUnits, blast.FalloffUnits, blast.DamageFactor, blast.KilledBy);
    }

    /// <summary>A round striking the player -- <c>0x412440</c>: the damage, the grunt, the shake, the flash and the shove.</summary>
    /// <param name="round">The round.</param>
    public void TakeRound(Igi1WeaponHit round)
    {
        ArgumentNullException.ThrowIfNull(round);
        HumanHealth health = Life.Health;
        HumanHit struck = round.Hit;
        health.Apply(struck.Zone, round.DamageFactor);
        Surroundings.PlayBulletImpact(struck.Position);

        // The retail handler transforms the round into camera space before writing the four corner alphas. It picks 0.4-0.8
        // rather than one opaque wash and replaces the prior hit, so automatic fire remains a directional cue instead of a
        // full-screen strobe.
        Vector3d travel = round.End - round.Origin;
        Vector3f incoming = new((float)travel.X, (float)travel.Y, (float)travel.Z);
        Vector3f viewIncoming = Rig.Camera.Orientation.Transposed().Transform(incoming);

        // 0x412440's order: the grunt, then the shake, then the flash, each on its own draw.
        if (!health.IsDead)
        {
            PlayGrunt();
        }

        InstallHitShake();
        Life.DamageFlash.Hit(viewIncoming, Surroundings.SoundRandom.NextSingle());

        // Every connecting round SHOVES the player 5 km/h along its travel -- the hit handler (0x412440) normalises the
        // round's direction and adds x/y x 189.62962 into the slide accumulator, whose 0.9-a-tick damping then bleeds it off.
        // This is what makes sustained fire stagger the player in retail.
        if (!health.IsDead)
        {
            double length = Math.Sqrt((travel.X * travel.X) + (travel.Y * travel.Y) + (travel.Z * travel.Z));
            if (length > 1e-6)
            {
                Rig.Body.AddHitShove(
                    (float)(travel.X / length * PlayerBody.HitShoveUnitsPerTick),
                    (float)(travel.Y / length * PlayerBody.HitShoveUnitsPerTick));
            }
        }

        if (health.IsDead)
        {
            Die(round.Origin, struck.Zone, struck.Bone, round.Category);
        }
    }

    /// <summary>A blast reaching the player -- <c>0x412990</c>: the shove and the shake by distance, then the dose.</summary>
    /// <param name="position">The blast's centre, in units.</param>
    /// <param name="radiusUnits">The blast radius, in units.</param>
    /// <param name="falloffUnits">The blast's falloff, in units.</param>
    /// <param name="damageFactor">The blast's damage factor.</param>
    /// <param name="killedBy">The category a death by it falls as.</param>
    public void Blast(Vector3d position, float radiusUnits, float falloffUnits, float damageFactor, WeaponCategory killedBy)
    {
        if (Life.Health.IsDead)
        {
            return;
        }

        ReachWithBlast(position, radiusUnits, falloffUnits);
        float dose = ExplosionDamage.Dose(
            (float)(Rig.Body.Position - position).Length,
            ExplosionDamage.EffectiveBodyRadius((float)HumanSeparation.RadiusInUnits),
            radiusUnits,
            falloffUnits,
            damageFactor);
        if (dose > 0f)
        {
            Life.Health.Apply(ExplosionDamage.Zone, dose);
            FlashEveryCorner();

            // A blast that zeroes the health kills exactly the way a bullet does -- without this the player stood at zero for
            // ever.
            if (Life.Health.IsDead)
            {
                Die(position, ExplosionDamage.Zone, -1, killedBy);
            }
        }
    }

    /// <summary>A soldier's close-combat blow landing on the player -- the blunt-hit handler <c>0x412CE0</c>.</summary>
    /// <param name="attacker">The striking soldier's position.</param>
    /// <param name="damage">The blow's damage, the settings row's close-combat figure.</param>
    /// <remarks>
    /// The zone is 13 when the attacker is in front of the player and 14 when behind, the damage is that zone's multiplier
    /// times the blow (scaled by difficulty, as every player hit is), and a surviving player grunts and then hears
    /// <c>punched_01</c> or <c>punched_02</c> by the parity of a random draw. Each corner of the red flash takes its own draw.
    /// </remarks>
    public void Strike(Vector3d attacker, float damage)
    {
        if (Life.IsDead)
        {
            return;
        }

        // The body's forward axis against the direction from the attacker: negative when facing it.
        Vector3d towardsPlayer = new(Rig.Body.Position.X - attacker.X, Rig.Body.Position.Y - attacker.Y, Rig.Body.Position.Z - attacker.Z);
        float yaw = Rig.Body.Yaw;
        double facing = (towardsPlayer.X * -Math.Sin(yaw)) + (towardsPlayer.Y * Math.Cos(yaw));
        int zone = facing < 0.0 ? PlayerBluntFrontZone : PlayerBluntBackZone;

        Life.Health.Apply(zone, damage);
        if (!Life.Health.IsDead)
        {
            PlayGrunt();
            Surroundings.PlayDefinition((Surroundings.SoundRandom.NextRaw() & 1) != 0 ? "punched_01" : "punched_02", priority: 250);
        }

        InstallHitShake();
        FlashEveryCorner();

        if (Life.Health.IsDead)
        {
            // A close-combat blow is melee, not a round.
            Die(attacker, zone, bone: -1, WeaponCategory.Knife);
        }
    }

    /// <summary>A landing, as the body resolved it: the camera's dip, the ground's sound, the soldiers hearing it, and its damage.</summary>
    /// <param name="impact">The landing.</param>
    public void Land(HumanFallImpact impact)
    {
        // 0x408460 arms the camera's dip on every ground contact, before it has decided whether the landing hurt: a step off a
        // crate drops the view a little, a long drop drops it the full quarter-metre. 0x4659B0 is two stores, so this is
        // unconditional and cannot fail.
        Rig.InstallLandingViewKick(impact.ViewKickUnits);

        // The ground's own landing sound, which is what every harmless drop sounds like. Both airborne states play it on
        // contact from the struck material, independently of the fall-damage cue below -- a hurting fall is heard twice, once
        // as the surface and once as the player.
        if (Rig.TakeLandingSound() is { } landing)
        {
            Surroundings.PlayLanding(landing);
        }

        // State 35 names one of these three definitions only when damage is non-zero. Like the player's own hit and death
        // sounds it belongs to the listener, so it is flat rather than world-panned.
        if (!string.IsNullOrEmpty(impact.SoundName))
        {
            Surroundings.PlayDefinition(impact.SoundName, priority: 250);
        }

        // Every ground contact posts type 11. A safe landing carries five metres; a damaging one ten.
        Surroundings.HearLanding(Rig.Body.Position, impact.HearingRadiusUnits);
        if (impact.Damage <= 0f)
        {
            return;
        }

        // PlayerBody has already added the direct, difficulty-independent dose to the shared health.
        Life.DamageFlash.Hit(Vector3f.Zero, Surroundings.SoundRandom.NextSingle());
        Surroundings.Logger.Information(
            "Player",
            $"Fall impact {impact.SpeedMetresPerSecond:F2} m/s dealt {impact.Damage:F3} "
                + $"({Life.Health.RemainingFraction:P0} left), sound '{impact.SoundName}'.");

        if (Life.Health.IsDead)
        {
            // Zone 13 is the retail fall bucket. With no external attacker the source is the feet, leaving the death fall
            // aligned to the player's current facing.
            Die(Rig.Body.Position, HumanDamage.FrontZone, bone: -1, WeaponCategory.Gun);
        }
    }

    /// <summary>One electric-fence shock, after the player's 45-tick guard permits it.</summary>
    /// <param name="fence">The fence.</param>
    public void Shock(WorldInteraction fence)
    {
        ArgumentNullException.ThrowIfNull(fence);
        if (Life.Health.IsDead)
        {
            return;
        }

        float healthBefore = Life.Health.RemainingFraction;
        Surroundings.Logger.Information(
            "Player.Fence",
            $"contact task={fence.Task.Id} powered={(fence.IsElectrified ? "true" : "false")} "
                + $"healthBefore={healthBefore:P0} position=({Rig.Body.Position.X:F0},"
                + $"{Rig.Body.Position.Y:F0},{Rig.Body.Position.Z:F0})");

        ElectricFenceShock shock = ElectricFenceContact.Shock(Life.Health, Life.ElectricFenceDose);
        float dealt = shock.Damage;
        if (dealt <= 0f)
        {
            Surroundings.Logger.Warning(
                "Player.Fence",
                $"contact produced no damage task={fence.Task.Id} "
                    + $"health={Life.Health.RemainingFraction:P0} hits={Life.Health.Hits}.");
            return;
        }

        // 0x412F80: the full shake for forty-five ticks, and four separate corner draws.
        Rig.InstallViewShake(HumanViewShake.HitPositionUnits, HumanViewShake.HitAngleRadians, HumanViewShake.FenceTicks);
        FlashEveryCorner();
        if (!shock.IsLethal)
        {
            // 0x412F80 selects this fence-specific cue after it has added the direct zone-13 dose.
            Surroundings.PlayDefinition("elfence_spark1", priority: 250);
        }

        Surroundings.Logger.Information("Player", $"Electric fence shock dealt {dealt:F3} ({Life.Health.RemainingFraction:P0} left).");
        if (shock.IsLethal)
        {
            Die(fence.Anchor, ElectricFenceContact.Zone, bone: -1, WeaponCategory.Gun);
        }
    }

    /// <summary>Puts the player through the whole death sequence, whatever killed it.</summary>
    /// <param name="from">Where the killing thing came from -- the shooter's muzzle, or the blast.</param>
    /// <param name="zone">The zone the killing damage landed on.</param>
    /// <param name="bone">The bone the round went through, or <c>-1</c> when nothing so precise.</param>
    /// <param name="category">The killing weapon's category, which picks the fall's family.</param>
    /// <remarks>
    /// Everything lethal runs it -- a grenade that zeroes the health must kill exactly the way a bullet does, or the player stands
    /// at zero health for ever.
    /// </remarks>
    public void Die(Vector3d from, int zone, int bone, WeaponCategory category)
    {
        if (Life.IsDead)
        {
            return;
        }

        // Locomotion state 47 (0x40E560): the clip comes from the LAST ZONE STRUCK's row in the player's own hit-zone table, one
        // of its two reactions by rand(2). The player stops taking input from here on -- it is a state, not a message.
        Life.IsDead = true;
        Rig.ResetOptics();
        Rig.StopWeaponsForDeath();

        // The player receives no further simulation ticks after death, so the weapon sound player cannot observe a quiet tick
        // and retire an automatic loop on its own.
        Surroundings.SilenceForDeath();
        int fall = HumanPlayerDeath.Choose(zone, HumanHitZoneTable.Player, Surroundings.ShotRandom.Next(2));

        // The lethal hit has already written the retail corner flash. Do not replace it with a uniform full-screen wash here:
        // 0x412440 writes the flash as part of damage receipt, before it changes the locomotion state to death.

        // Which way the body is thrown: the facing whose backward axis points away from the killer, exactly as the soldier
        // death faces the incoming round (SoldierDeath.TurnToTheShot).
        Vector3d feet = Rig.Body.Position;
        double throwX = feet.X - from.X;
        double throwY = feet.Y - from.Y;
        double throwLength = Math.Sqrt((throwX * throwX) + (throwY * throwY));
        float throwFacing = throwLength > 1e-6 ? (float)Math.Atan2(throwX / throwLength, -(throwY / throwLength)) : Rig.Body.Yaw;
        Surroundings.KillBody(
            fall,
            feet,
            throwFacing,
            bone,
            new Vector3d(feet.X - from.X, feet.Y - from.Y, feet.Z - from.Z),
            category,
            new Vector3d(Rig.Body.Velocity.X, Rig.Body.Velocity.Y, Rig.Body.Velocity.Z));
        Surroundings.BeginDeathCamera(feet, throwFacing);

        // And the death sound -- state 47's own sub_4638A0(human, "player_death_", 3).
        Surroundings.PlayDeath(HumanPlayerDeath.SoundPrefix, HumanPlayerDeath.SoundVariants);
    }

    /// <summary>
    /// The shake a bullet or a blow puts in the view -- <c>0x412440</c> and <c>0x412CE0</c>: a draw clamped to <c>[0.25, 1]</c>
    /// scales the full 5 cm and 0.75 degrees, for fifteen ticks.
    /// </summary>
    public void InstallHitShake()
    {
        float share = Math.Clamp(Surroundings.SoundRandom.NextSingle(), 0.25f, 1f);
        Rig.InstallViewShake(HumanViewShake.HitPositionUnits * share, HumanViewShake.HitAngleRadians * share, HumanViewShake.HitTicks);
    }

    /// <summary>The player's grunt -- <c>player_hit_</c> through <c>0x4637C0</c>'s per-human rate limit.</summary>
    /// <remarks>
    /// Flat, not positional: the sound belongs to the player, who is the listener. The limit is what lets sustained fire
    /// stagger the player without stuttering the sound.
    /// </remarks>
    public void PlayGrunt()
    {
        if (Surroundings.Tick < Life.HitQuietUntil)
        {
            return;
        }

        Life.HitQuietUntil = Surroundings.Tick + HumanVariantSound.HitCooldownTicks(Surroundings.SoundRandom);
        Surroundings.PlayHit(HumanVariantSound.PlayerHitPrefix, HumanVariantSound.PlayerHitVariants);
    }

    // What any blast does to the player before its damage is counted -- the head of 0x412990. Explosion0 (0x47F200) hands its
    // message to every human with no range test of its own, and the player's handler acts on distance from the eye before, and
    // whatever, the dose: inside one and a half times radius plus falloff the body is shoved away from the blast by 758.5 units
    // a tick (into the same slide the bullet shove uses), and the view shakes by k of a hit's shake -- all of it inside three
    // radii, fading to nothing at nine -- for the explosion's 90 ticks. The same handler raises Human+3528 within 25 m, which
    // plays the earcandy loop; no shipped sound set defines it, so the original is silent there too.
    private void ReachWithBlast(Vector3d blastPosition, float radiusUnits, float falloffUnits)
    {
        if (radiusUnits <= 0f)
        {
            return;
        }

        Vector3d away = Rig.EyeUnits - blastPosition;
        double distance = away.Length;
        if (distance < (radiusUnits + falloffUnits) * 1.5 && distance > 0.0)
        {
            Rig.Body.AddHitShove((float)(away.X / distance * BlastShoveUnitsPerTick), (float)(away.Y / distance * BlastShoveUnitsPerTick));
        }

        double beyond = distance - (radiusUnits * 3.0);
        double share = beyond < 0.0 ? 1.0 : 1.0 - (beyond / (radiusUnits * 6.0));
        if (share > 0.0)
        {
            Rig.InstallViewShake((float)(HumanViewShake.HitPositionUnits * share), (float)(HumanViewShake.HitAngleRadians * share), ExplosionShakeTicks);
        }
    }

    // The wash a blow, a fence or a damaging blast raises: four independent corner draws.
    private void FlashEveryCorner() =>
        Life.DamageFlash.HitEveryCorner(
            Surroundings.SoundRandom.NextSingle(),
            Surroundings.SoundRandom.NextSingle(),
            Surroundings.SoundRandom.NextSingle(),
            Surroundings.SoundRandom.NextSingle());
}
