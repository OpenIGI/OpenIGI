using OpenIGI.Game.Hosting;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// What hurts Project I.G.I.'s player and how it dies: a soldier's blow (<c>0x412CE0</c>), a fall (<c>0x408460</c>), a
/// powered fence (<c>0x412F80</c>), and the death state every lethal hit enters (state 47, <c>0x40E560</c>).
/// </summary>
/// <param name="mission">The mission's world, which holds the player, its health and the draws they take.</param>
/// <param name="sounds">The cries a hit and a death make.</param>
/// <param name="audio">The player's held voices, which a death stops, and a landing's sound.</param>
/// <param name="host">What plays and shows them.</param>
/// <remarks>
/// Every harm is dealt to an <see cref="IHarmedPlayer"/>: the mission's own player (<see cref="Own"/>) or a co-op partner,
/// by the same handler. The handler decides what happens -- the damage and the death -- and the player's
/// <see cref="IHarmedPlayer.Senses"/> make what it feels of it, on whichever screen it is played.
/// </remarks>
public sealed class Igi1PlayerHarm(MissionWorld mission, Igi1HumanSounds sounds, Igi1MissionAudio audio, IPlayerHarmHost host)
{
    // 0x412CE0's two zones: 13 for a blow from in front, 14 from behind.
    private const int PlayerBluntFrontZone = 13;
    private const int PlayerBluntBackZone = 14;

    /// <summary>Gets the mission's own player, felt on this machine.</summary>
    public IHarmedPlayer Own { get; } = new MissionPlayer(mission, new LocalPlayerSenses(mission, sounds, audio, host));

    /// <summary>A soldier's close-combat blow landing on the mission's own player.</summary>
    /// <param name="attacker">The striking soldier's position.</param>
    /// <param name="damage">The blow's damage.</param>
    public void StrikePlayer(Vector3d attacker, float damage) => StrikePlayer(Own, attacker, damage);

    /// <summary>A soldier's close-combat blow landing on a player — the blunt-hit handler <c>0x412CE0</c>.</summary>
    /// <param name="target">The player struck.</param>
    /// <param name="attacker">The striking soldier's position.</param>
    /// <param name="damage">The blow's damage, the settings row's close-combat figure.</param>
    /// <remarks>
    /// The zone is 13 when the attacker is in front of the player and 14 when behind, the damage is that zone's multiplier
    /// times the blow (scaled by difficulty, as every player hit is), and a surviving player grunts and then hears
    /// <c>punched_01</c> or <c>punched_02</c> by the parity of a random draw. Each corner of the red flash takes its own draw.
    /// </remarks>
    public void StrikePlayer(IHarmedPlayer target, Vector3d attacker, float damage)
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
        int zone = facing < 0.0 ? PlayerBluntFrontZone : PlayerBluntBackZone;

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
            BeginPlayerDeath(target, attacker, zone, bone: -1, WeaponCategory.Knife);
        }
    }

    /// <summary>Brings the mission's own player back where it fell or where it is put.</summary>
    /// <param name="at">Where it stands up, or <see langword="null"/> where it fell.</param>
    /// <param name="facing">Its heading there.</param>
    public void RevivePlayer(Vector3d? at, float facing) => RevivePlayer(Own, at, facing);

    /// <summary>Brings a dead player back, as a co-op teammate revives it: whole again, armed, and seen from its own eyes.</summary>
    /// <param name="target">The player.</param>
    /// <param name="at">Where it stands up, or <see langword="null"/> where it fell.</param>
    /// <param name="facing">Its heading there.</param>
    /// <remarks>Not the original's: a single player's death ends the mission, a co-op player's waits for a teammate.</remarks>
    public static void RevivePlayer(IHarmedPlayer target, Vector3d? at, float facing)
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

    /// <summary>Puts the mission's own player through the whole death sequence.</summary>
    /// <param name="from">Where the killing thing came from.</param>
    /// <param name="zone">The zone the killing damage landed on.</param>
    /// <param name="bone">The bone the round went through, or <c>-1</c>.</param>
    /// <param name="category">The killing weapon's category.</param>
    public void BeginPlayerDeath(Vector3d from, int zone, int bone, WeaponCategory category) =>
        BeginPlayerDeath(Own, from, zone, bone, category);

    /// <summary>Puts a player through the whole death sequence, whatever killed it.</summary>
    /// <param name="target">The player.</param>
    /// <param name="from">Where the killing thing came from — the shooter's muzzle, or the blast.</param>
    /// <param name="zone">The zone the killing damage landed on.</param>
    /// <param name="bone">The bone the round went through, or <c>-1</c> when nothing so precise.</param>
    /// <param name="category">The killing weapon's category, which picks the fall's family.</param>
    /// <remarks>
    /// Extracted from the guard-round handler so that EVERYTHING lethal runs it — a grenade that zeroes the health must kill
    /// exactly the way a bullet does, or the player stands at zero health forever, which is precisely the bug this fixed.
    /// </remarks>
    public void BeginPlayerDeath(IHarmedPlayer target, Vector3d from, int zone, int bone, WeaponCategory category)
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
        int fall = HumanPlayerDeath.Choose(zone, HumanHitZoneTable.Player, mission.ShotRandom.Next(2));

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

    /// <summary>Routes a resolved body landing into the desktop-owned presentation and death state.</summary>
    public void ResolvePlayerFallImpact(PlayerCamera player, HumanFallImpact impact)
    {
        // 0x408460 arms the camera's dip on every ground contact, before it has decided whether the
        // landing hurt: a step off a crate drops the view a little, a long drop drops it the full
        // quarter-metre. 0x4659B0 is two stores, so this is unconditional and cannot fail.
        player.InstallLandingViewKick(impact.ViewKickUnits);

        // The ground's own landing sound, which is what every harmless drop sounds like. Both airborne
        // states play it on contact from the struck material, independently of the fall-damage cue
        // below -- a hurting fall is heard twice, once as the surface and once as the player.
        if (player.TakeLandingSound() is { } landing)
        {
            audio.PlayLandingSound(landing);
        }

        // State 35 names one of these three definitions only when damage is non-zero. Like the player's
        // own hit and death sounds it belongs to the listener, so it is flat rather than world-panned.
        if (!string.IsNullOrEmpty(impact.SoundName))
        {
            Own.Senses.Sound(impact.SoundName);
        }

        // Every ground contact posts type 11. A safe landing carries five metres; a damaging one ten.
        mission.Soldiers?.NotifyGroundImpact(
            player.Body.Position,
            player.Body.Position,
            impact.HearingRadiusUnits);

        if (impact.Damage <= 0f)
        {
            return;
        }

        // PlayerBody has already added the direct, difficulty-independent dose to the shared health.
        // Raise the desktop wash that the render pass actually draws rather than the body's standalone one.
        Own.Senses.FlashCentre();
        host.Logger.Information(
            "Player",
            $"Fall impact {impact.SpeedMetresPerSecond:F2} m/s dealt {impact.Damage:F3} "
                + $"({mission.PlayerHealth.RemainingFraction:P0} left), sound '{impact.SoundName}'.");

        if (mission.PlayerHealth.IsDead)
        {
            // Zone 13 is the retail fall bucket. With no external attacker the source is the feet,
            // leaving the death fall aligned to the player's current facing.
            BeginPlayerDeath(
                player.Body.Position,
                HumanDamage.FrontZone,
                bone: -1,
                WeaponCategory.Gun);
        }
    }

    /// <summary>Applies one native electric-fence shock after the player's 45-tick guard permits it.</summary>
    public void ResolvePlayerElectricFence(PlayerCamera player, WorldInteraction fence)
    {
        if (mission.PlayerHealth.IsDead)
        {
            return;
        }

        float healthBefore = mission.PlayerHealth.RemainingFraction;
        host.Logger.Information(
            "Player.Fence",
            $"contact task={fence.Task.Id} powered={(fence.IsElectrified ? "true" : "false")} "
                + $"healthBefore={healthBefore:P0} position=({player.Body.Position.X:F0},"
                + $"{player.Body.Position.Y:F0},{player.Body.Position.Z:F0})");

        ElectricFenceShock shock = ElectricFenceContact.Shock(mission.PlayerHealth, mission.ElectricFenceDose);
        float dealt = shock.Damage;
        if (dealt <= 0f)
        {
            host.Logger.Warning(
                "Player.Fence",
                $"contact produced no damage task={fence.Task.Id} "
                    + $"health={mission.PlayerHealth.RemainingFraction:P0} hits={mission.PlayerHealth.Hits}.");
            return;
        }

        // 0x412F80: the full shake for forty-five ticks, and four separate corner draws.
        Own.Senses.Shake(HumanViewShake.HitPositionUnits, HumanViewShake.HitAngleRadians, HumanViewShake.FenceTicks);
        Own.Senses.FlashCorners();
        if (!shock.IsLethal)
        {
            // 0x412F80 selects this fence-specific cue after it has added the direct zone-13 dose.
            Own.Senses.Sound("elfence_spark1");
        }

        host.Logger.Information(
            "Player",
            $"Electric fence shock dealt {dealt:F3} ({mission.PlayerHealth.RemainingFraction:P0} left).");

        if (shock.IsLethal)
        {
            BeginPlayerDeath(fence.Anchor, ElectricFenceContact.Zone, bone: -1, WeaponCategory.Gun);
        }
    }
}
