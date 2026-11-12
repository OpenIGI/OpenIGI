using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// What hurts a Project I.G.I. player and how it dies: a round, a blast, a blow, a fall, a powered fence, and the death
/// they lead to. One set of rules for every player -- the mission's own, a co-op partner, and the game's player standing
/// in another game's level.
/// </summary>
/// <remarks>
/// <para>
/// Each rule is the original's handler on the player's task (<c>0x412440</c> a round, <c>0x412990</c> a blast,
/// <c>0x412CE0</c> a blow, <c>0x408460</c> a landing, <c>0x412F80</c> a fence, <c>0x40E560</c> the death), in its order
/// and from its streams of chance. Who is hurt is an <see cref="IHarmedPlayer"/>: what the harm does to its body is done
/// here, and what it is shown and told -- the grunt, the shake, the wash, the fall -- goes through its senses, so a player
/// on another machine is sent them rather than given them.
/// </para>
/// <para>
/// What the player is hurt in -- the clock, the streams, the sounds of the place and who hears a landing -- is its
/// surroundings (<see cref="IIgi1PlayerSurroundings"/>), which the game's own mission provides and another game's level can.
/// </para>
/// </remarks>
/// <param name="surroundings">What the players are hurt in.</param>
public sealed class Igi1PlayerRules(IIgi1PlayerSurroundings surroundings)
{
    // 0x412CE0's two zones: 13 for a blow from in front, 14 from behind.
    private const int BluntFrontZone = 13;
    private const int BluntBackZone = 14;

    // 0x412990's shove, units a tick into the body's slide -- 758.51849.
    private const float BlastShoveUnitsPerTick = 758.51849f;

    // The explosion message's duration (+20), which 0x47F200 fills with 90 ticks.
    private const int ExplosionShakeTicks = 90;

    private readonly IIgi1PlayerSurroundings _surroundings = surroundings ?? throw new ArgumentNullException(nameof(surroundings));

    /// <summary>
    /// Gets or sets what is told of a round a player took: the player, who is credited with it (none for a guard's), and
    /// whether it struck the neck, the one zone that counts as a headshot.
    /// </summary>
    public Action<IHarmedPlayer, ShotTally?, bool>? RoundTaken { get; set; }

    /// <summary>A round's hit on a player (<c>0x412440</c>): the dose, the grunt, the shake, the flash, the shove and the death.</summary>
    /// <param name="target">The player.</param>
    /// <param name="round">The round.</param>
    public void TakeRound(IHarmedPlayer target, Igi1WeaponHit round)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(round);
        if (target.Rig is not { } rig)
        {
            return;
        }

        HumanHealth health = target.Health;
        HumanHit struck = round.Hit;

        // A versus match's player takes I.G.I. 2's damage for the gun that fired, and a knife in its back kills it as an unaware
        // guard's does.
        bool versus = ReferenceEquals(health.Zones, HumanHitZoneTable.Versus);
        float damage = !versus ? round.DamageFactor
            : Multiplayer.Igi1VersusDamage.IsBackstab(round.Category, rig.Body.Position - round.Origin, rig.Body.Yaw) ? SoldierSimulation.KnifeUnawareDamageFactor
            : Multiplayer.Igi1VersusDamage.Of(round.Weapon, round.DamageFactor);
        health.Apply(struck.Zone, damage);
        RoundTaken?.Invoke(target, round.Credit, struck.Zone == HumanDamage.NeckZone);
        _surroundings.PlayBulletImpact(struck.Position);

        // 0x412440's order: the grunt, then the shake, then the flash from the corners the round came in through, each on
        // its own draw.
        Vector3d travel = round.End - round.Origin;
        if (!health.IsDead)
        {
            target.Senses.Grunt();
        }

        target.Senses.HitShake();
        target.Senses.FlashFrom(new Vector3f((float)travel.X, (float)travel.Y, (float)travel.Z));

        // Every connecting round SHOVES the player 5 km/h along its travel -- the hit handler (0x412440) normalises the
        // round's direction and adds x/y x 189.62962 into the slide accumulator, whose 0.9-a-tick damping then bleeds it
        // off. This is what makes sustained fire stagger the player in retail.
        if (!health.IsDead)
        {
            double length = Math.Sqrt((travel.X * travel.X) + (travel.Y * travel.Y) + (travel.Z * travel.Z));
            if (length > 1e-6)
            {
                target.Shove(
                    rig,
                    (float)(travel.X / length * PlayerBody.HitShoveUnitsPerTick),
                    (float)(travel.Y / length * PlayerBody.HitShoveUnitsPerTick));
            }
        }

        if (health.IsDead)
        {
            Die(target, round.Origin, struck.Zone, struck.Bone, round.Category);
        }
    }

    /// <summary>A blast's share of a player (<c>0x412990</c>): the shove and the shake, then the dose, the flash and the death.</summary>
    /// <param name="target">The player.</param>
    /// <param name="position">The blast's centre, in units.</param>
    /// <param name="radiusUnits">The full-damage radius, in units.</param>
    /// <param name="falloffUnits">The falloff beyond it, in units.</param>
    /// <param name="damageFactor">The blast's damage.</param>
    /// <param name="killedBy">The weapon category a death it causes is reported under.</param>
    /// <param name="bodyRadiusUnits">The bound of the player's body model (<c>Human+0xA0</c>), a third of which the blast is weighed with.</param>
    public void Blast(
        IHarmedPlayer target, Vector3d position, float radiusUnits, float falloffUnits, float damageFactor, WeaponCategory killedBy, float bodyRadiusUnits)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Rig is not { } rig || target.Health.IsDead)
        {
            return;
        }

        ReachWithBlast(target, rig, position, radiusUnits, falloffUnits);

        // 0x412990 weighs the blast at the origin (Human+32) with a third of the body model's bound (Human+0xA0).
        float dose = ExplosionDamage.Dose(
            (float)(rig.Body.Origin - position).Length,
            ExplosionDamage.EffectiveBodyRadius(bodyRadiusUnits),
            radiusUnits,
            falloffUnits,
            damageFactor);
        if (dose > 0f)
        {
            target.Health.Apply(ExplosionDamage.Zone, dose);

            // 0x412990's flash: four independent corner draws.
            target.Senses.FlashCorners();

            // A blast that zeroes the health kills exactly the way a bullet does -- without this the player stood at
            // zero for ever.
            if (target.Health.IsDead)
            {
                Die(target, position, ExplosionDamage.Zone, bone: -1, killedBy);
            }
        }
    }

    /// <summary>Applies a close-combat blow to a player, as the original's blunt-attack handler does (<c>0x412CE0</c>).</summary>
    /// <param name="target">The player struck.</param>
    /// <param name="attacker">The attacker's position, which decides front or back.</param>
    /// <param name="damage">The already-scaled damage (the attacker's Blunt weapon factor).</param>
    /// <remarks>
    /// The handler picks the zone from the body's forward axis dotted with the direction from the attacker: negative means
    /// the player faces the attacker, zone 13; otherwise zone 14. It then applies the damage, and on a kill starts the death.
    /// A blow shakes the view and flashes the corners like a round, and only while the player lives it makes its grunt and
    /// one of the two punches.
    /// </remarks>
    public void Strike(IHarmedPlayer target, Vector3d attacker, float damage)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Rig is not { } rig || target.IsDead)
        {
            return;
        }

        // The body's forward axis against the direction from the attacker: negative when facing it.
        Vector3d feet = rig.Body.Position;
        Vector3d towardsPlayer = new(feet.X - attacker.X, feet.Y - attacker.Y, feet.Z - attacker.Z);
        float yaw = rig.Body.Yaw;
        double facing = (towardsPlayer.X * -Math.Sin(yaw)) + (towardsPlayer.Y * Math.Cos(yaw));
        int zone = facing < 0.0 ? BluntFrontZone : BluntBackZone;

        target.Health.Apply(zone, damage);
        bool alive = !target.Health.IsDead;
        if (alive)
        {
            target.Senses.Grunt();
            target.Senses.Blow();
        }

        target.Senses.HitShake();
        target.Senses.FlashCorners();
        if (!alive)
        {
            // A close-combat blow is melee, not a round.
            Die(target, attacker, zone, bone: -1, WeaponCategory.Knife);
        }
    }

    /// <summary>Puts a player that has died back on its feet: standing, armed and at full health, where it is or at a place.</summary>
    /// <param name="target">The player.</param>
    /// <param name="at">Where its feet go, in units, or <see langword="null"/> to stand where it fell.</param>
    /// <param name="facing">Which way it faces.</param>
    /// <remarks>
    /// Not retail: the original's dead player restarts the mission. A multiplayer session needs a round to end and another to
    /// begin with everyone alive, so this undoes what a death did -- the flag, the health, the weapons a death put away, and
    /// the corpse and the death camera -- and nothing else.
    /// </remarks>
    public static void Revive(IHarmedPlayer target, Vector3d? at, float facing)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Rig is not { } rig || (!target.IsDead && !target.Health.IsDead))
        {
            return;
        }

        target.IsDead = false;
        target.Health.Reset();
        if (at is { } feet)
        {
            rig.Body.Position = feet;
            rig.Body.Yaw = facing;
        }

        rig.ReviveWeapons();
        target.Senses.Revived();
    }

    /// <summary>Starts a player's death: its fall and what follows it (state 47, <c>0x40E560</c>).</summary>
    /// <param name="target">The player dying.</param>
    /// <param name="from">Where the kill came from, which decides which way the body is thrown.</param>
    /// <param name="zone">The last zone struck, which picks the fall.</param>
    /// <param name="bone">The bone the lethal hit landed on, or -1.</param>
    /// <param name="category">The weapon category the death is reported under.</param>
    public void Die(IHarmedPlayer target, Vector3d from, int zone, int bone, WeaponCategory category)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Rig is not { } rig || target.IsDead)
        {
            return;
        }

        // Locomotion state 47 (0x40E560): the clip comes from the LAST ZONE STRUCK's row in the player's own hit-zone
        // table, one of its two reactions by rand(2). The player stops taking input from here on -- it is a state, not a
        // message.
        target.IsDead = true;
        rig.ResetOptics();
        rig.StopWeaponsForDeath();

        int fall = HumanPlayerDeath.Choose(zone, HumanHitZoneTable.Player, _surroundings.ShotRandom.Next(2));

        // Which way the body is thrown: the facing whose backward axis points away from the killer, exactly as the soldier
        // death faces the incoming round (SoldierDeath.TurnToTheShot).
        Vector3d feet = rig.Body.Position;
        double throwX = feet.X - from.X;
        double throwY = feet.Y - from.Y;
        double throwLength = Math.Sqrt((throwX * throwX) + (throwY * throwY));
        float throwFacing = throwLength > 1e-6
            ? (float)Math.Atan2(throwX / throwLength, -(throwY / throwLength))
            : rig.Body.Yaw;
        target.Senses.Died(new PlayerFall(
            fall,
            feet,
            throwFacing,
            bone,
            new Vector3d(feet.X - from.X, feet.Y - from.Y, feet.Z - from.Z),
            category,
            new Vector3d(rig.Body.Velocity.X, rig.Body.Velocity.Y, rig.Body.Velocity.Z)));
    }

    /// <summary>A player's landing (<c>0x408460</c>): the camera's dip, the ground's sound, the noise the AI hears, and what a hard one costs.</summary>
    /// <param name="target">The player that landed.</param>
    /// <param name="impact">The landing.</param>
    public void Land(IHarmedPlayer target, HumanFallImpact impact)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Rig is not { } rig)
        {
            return;
        }

        // 0x408460 arms the camera's dip on every ground contact, before it has decided whether the
        // landing hurt: a step off a crate drops the view a little, a long drop drops it the full
        // quarter-metre. 0x4659B0 is two stores, so this is unconditional and cannot fail.
        rig.InstallLandingViewKick(impact.ViewKickUnits);

        // The ground's own landing sound, which is what every harmless drop sounds like. Both airborne
        // states play it on contact from the struck material, independently of the fall-damage cue
        // below -- a hurting fall is heard twice, once as the surface and once as the player.
        if (rig.TakeLandingSound() is { } landing)
        {
            _surroundings.PlayLanding(landing);
        }

        // State 35 names one of these three definitions only when damage is non-zero. Like the player's
        // own hit and death sounds it belongs to the listener, so it is flat rather than world-panned.
        if (!string.IsNullOrEmpty(impact.SoundName))
        {
            target.Senses.Sound(impact.SoundName);
        }

        // Every ground contact posts type 11. A safe landing carries five metres; a damaging one ten.
        _surroundings.HearLanding(rig.Body.Position, impact.HearingRadiusUnits);
        if (impact.Damage <= 0f)
        {
            return;
        }

        // PlayerBody has already added the direct, difficulty-independent dose to the player's health.
        target.Senses.FlashCentre();
        _surroundings.Logger.Information(
            "Player",
            $"Fall impact {impact.SpeedMetresPerSecond:F2} m/s dealt {impact.Damage:F3} "
                + $"({target.Health.RemainingFraction:P0} left), sound '{impact.SoundName}'.");
        if (target.Health.IsDead)
        {
            // Zone 13 is the retail fall bucket. With no external attacker the source is the feet,
            // leaving the death fall aligned to the player's current facing.
            Die(target, rig.Body.Position, HumanDamage.FrontZone, bone: -1, WeaponCategory.Gun);
        }
    }

    /// <summary>A powered fence's shock (<c>0x412F80</c>): the dose, the shake, the flash, its spark, and the death.</summary>
    /// <param name="target">The player that touched it.</param>
    /// <param name="fence">The fence.</param>
    /// <param name="dose">The damage one contact adds, which <c>humanplayer.qvm</c> gives.</param>
    public void Shock(IHarmedPlayer target, WorldInteraction fence, float dose)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(fence);
        if (target.Rig is not { } rig || target.Health.IsDead)
        {
            return;
        }

        float healthBefore = target.Health.RemainingFraction;
        _surroundings.Logger.Information(
            "Player.Fence",
            $"contact task={fence.Task.Id} powered={(fence.IsElectrified ? "true" : "false")} "
                + $"healthBefore={healthBefore:P0} position=({rig.Body.Position.X:F0},"
                + $"{rig.Body.Position.Y:F0},{rig.Body.Position.Z:F0})");
        ElectricFenceShock shock = ElectricFenceContact.Shock(target.Health, dose);
        float dealt = shock.Damage;
        if (dealt <= 0f)
        {
            _surroundings.Logger.Warning(
                "Player.Fence",
                $"contact produced no damage task={fence.Task.Id} "
                    + $"health={target.Health.RemainingFraction:P0} hits={target.Health.Hits}.");
            return;
        }

        // 0x412F80: the full shake for forty-five ticks, and four separate corner draws.
        target.Senses.Shake(HumanViewShake.HitPositionUnits, HumanViewShake.HitAngleRadians, HumanViewShake.FenceTicks);
        target.Senses.FlashCorners();
        if (!shock.IsLethal)
        {
            // 0x412F80 selects this fence-specific cue after it has added the direct zone-13 dose.
            target.Senses.Sound("elfence_spark1");
        }

        _surroundings.Logger.Information(
            "Player",
            $"Electric fence shock dealt {dealt:F3} ({target.Health.RemainingFraction:P0} left).");
        if (shock.IsLethal)
        {
            Die(target, fence.Anchor, ElectricFenceContact.Zone, bone: -1, WeaponCategory.Gun);
        }
    }

    // What any blast does to a player before its damage is counted -- the head of 0x412990. Explosion0 (0x47F200) hands its
    // message to every human with no range test of its own, and the player's handler acts on distance from the eye before,
    // and whatever, the dose: inside one and a half times radius plus falloff the body is shoved away from the blast by 758.5
    // units a tick (into the same slide the bullet shove uses), and the view shakes by k of a hit's shake -- all of it inside
    // three radii, fading to nothing at nine -- for the explosion's 90 ticks. The same handler raises Human+3528 within 25 m,
    // which plays the earcandy loop; no shipped sound set defines it, so the original is silent there too.
    private static void ReachWithBlast(IHarmedPlayer target, PlayerCamera rig, Vector3d blastPosition, float radiusUnits, float falloffUnits)
    {
        if (target.Health.IsDead || radiusUnits <= 0f)
        {
            return;
        }

        Vector3d away = rig.EyeUnits - blastPosition;
        double distance = away.Length;
        if (distance < (radiusUnits + falloffUnits) * 1.5 && distance > 0.0)
        {
            target.Shove(
                rig,
                (float)(away.X / distance * BlastShoveUnitsPerTick),
                (float)(away.Y / distance * BlastShoveUnitsPerTick));
        }

        double beyond = distance - (radiusUnits * 3.0);
        double share = beyond < 0.0 ? 1.0 : 1.0 - (beyond / (radiusUnits * 6.0));
        if (share > 0.0)
        {
            target.Senses.Shake(
                (float)(HumanViewShake.HitPositionUnits * share),
                (float)(HumanViewShake.HitAngleRadians * share),
                ExplosionShakeTicks);
        }
    }
}
