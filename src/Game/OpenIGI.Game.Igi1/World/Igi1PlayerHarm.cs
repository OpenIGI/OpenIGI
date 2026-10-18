using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What the player's damage and death ask of whatever plays and shows them.</summary>
public interface IIgi1PlayerHarmHost
{
    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Gets the game's loop, whose clock the player's grunts are gated on.</summary>
    GameLoop Game { get; }

    /// <summary>Gets the soldiers as they are drawn, which pose the player's own body when it falls.</summary>
    ISoldierPresenter? SoldierPresenter { get; }

    /// <summary>Plays a human's hit grunt: one of a prefix's variants.</summary>
    /// <param name="prefix">The definitions' common prefix.</param>
    /// <param name="variants">How many there are.</param>
    /// <param name="positionInUnits">Where, or <see langword="null"/> for the listener's own.</param>
    void PlayHitSound(string prefix, int variants, Vector3d? positionInUnits);

    /// <summary>Plays a human's death cry: one of a prefix's variants.</summary>
    /// <param name="prefix">The definitions' common prefix.</param>
    /// <param name="variants">How many there are.</param>
    /// <param name="positionInUnits">Where, or <see langword="null"/> for the listener's own.</param>
    void PlayDeathSound(string prefix, int variants, Vector3d? positionInUnits);

    /// <summary>Plays the sound of the surface the player landed on.</summary>
    /// <param name="landing">The definition and where.</param>
    void PlayLandingSound((string Sound, Vector3d PositionInUnits) landing);

    /// <summary>Plays a sound definition.</summary>
    /// <param name="name">The definition.</param>
    /// <param name="positionInUnits">Where, or <see langword="null"/> for the listener's own.</param>
    /// <param name="priority">Its priority.</param>
    void PlayDefinition(string name, Vector3d? positionInUnits = null, int priority = 0);

    /// <summary>Stops the binoculars' zoom sound.</summary>
    void StopBinocularTransitionSound();

    /// <summary>Retires the player's weapon loops, which no further tick will quiet.</summary>
    void ResetWeaponSounds();

    /// <summary>Hands the view to the camera that pulls back over the fallen body.</summary>
    /// <param name="position">The body, in world units.</param>
    /// <param name="facing">The way it was thrown.</param>
    /// <param name="walls">What pulls the camera in.</param>
    void BeginDeathCamera(Vector3d position, float facing, WorldRayCast walls);
}

/// <summary>
/// What hurts Project I.G.I.'s player and how it dies: a soldier's blow (<c>0x412CE0</c>), a fall (<c>0x408460</c>), a
/// powered fence (<c>0x412F80</c>), and the death state every lethal hit enters (state 47, <c>0x40E560</c>).
/// </summary>
/// <param name="mission">The mission's world, which holds the player, its health and the draws they take.</param>
/// <param name="host">What plays and shows them.</param>
public sealed class Igi1PlayerHarm(MissionWorld mission, IIgi1PlayerHarmHost host)
{
    // 0x412CE0's two zones: 13 for a blow from in front, 14 from behind.
    private const int PlayerBluntFrontZone = 13;
    private const int PlayerBluntBackZone = 14;

    /// <summary>
    /// The shake a bullet or a blow puts in the view — <c>0x412440</c> and <c>0x412CE0</c>: a draw clamped
    /// to <c>[0.25, 1]</c> scales the full 5 cm and 0.75 degrees, for fifteen ticks.
    /// </summary>
    public void InstallPlayerHitShake()
    {
        float share = Math.Clamp(mission.SoundRandom.NextSingle(), 0.25f, 1f);
        mission.Player!.InstallViewShake(
            HumanViewShake.HitPositionUnits * share, HumanViewShake.HitAngleRadians * share, HumanViewShake.HitTicks);
    }

    /// <summary>The player's grunt — <c>player_hit_</c> through <c>0x4637C0</c>'s per-human rate limit.</summary>
    /// <remarks>
    /// Flat, not positional: the sound belongs to the player, who is the listener. The limit is what lets
    /// sustained fire stagger the player without stuttering the sound.
    /// </remarks>
    public void PlayPlayerGrunt()
    {
        if (host.Game.Clock.TickCount < mission.PlayerHitQuietUntil)
        {
            return;
        }

        mission.PlayerHitQuietUntil = host.Game.Clock.TickCount + HumanVariantSound.HitCooldownTicks(mission.SoundRandom);
        host.PlayHitSound(HumanVariantSound.PlayerHitPrefix, HumanVariantSound.PlayerHitVariants, null);
    }

    /// <summary>A soldier's close-combat blow landing on the player — the blunt-hit handler <c>0x412CE0</c>.</summary>
    /// <param name="attacker">The striking soldier's position.</param>
    /// <param name="damage">The blow's damage, the settings row's close-combat figure.</param>
    /// <remarks>
    /// The zone is 13 when the attacker is in front of the player and 14 when behind, the damage is that
    /// zone's multiplier times the blow (scaled by difficulty, as every player hit is), and a surviving
    /// player grunts and then hears <c>punched_01</c> or <c>punched_02</c> by the parity of a random draw.
    /// Each corner of the red flash takes its own draw.
    /// </remarks>
    public void StrikePlayer(Vector3d attacker, float damage)
    {
        if (mission.Player is null || mission.IsPlayerDead)
        {
            return;
        }

        // The body's forward axis against the direction from the attacker: negative when facing it.
        Vector3d towardsPlayer = new(
            mission.Player.Body.Position.X - attacker.X,
            mission.Player.Body.Position.Y - attacker.Y,
            mission.Player.Body.Position.Z - attacker.Z);
        float yaw = mission.Player.Body.Yaw;
        double facing = (towardsPlayer.X * -Math.Sin(yaw)) + (towardsPlayer.Y * Math.Cos(yaw));
        int zone = facing < 0.0 ? PlayerBluntFrontZone : PlayerBluntBackZone;

        mission.PlayerHealth.Apply(zone, damage);
        if (!mission.PlayerHealth.IsDead)
        {
            PlayPlayerGrunt();
            host.PlayDefinition(
                (mission.SoundRandom.NextRaw() & 1) != 0 ? "punched_01" : "punched_02",
                positionInUnits: null,
                priority: 250);
        }

        InstallPlayerHitShake();
        mission.DamageFlash.HitEveryCorner(
            mission.SoundRandom.NextSingle(),
            mission.SoundRandom.NextSingle(),
            mission.SoundRandom.NextSingle(),
            mission.SoundRandom.NextSingle());

        if (mission.PlayerHealth.IsDead)
        {
            // A close-combat blow is melee, not a round.
            BeginPlayerDeath(attacker, zone, bone: -1, WeaponCategory.Knife);
        }
    }

    /// <summary>Puts the player through the whole death sequence, whatever killed them.</summary>
    /// <param name="from">Where the killing thing came from — the shooter's muzzle, or the blast.</param>
    /// <param name="zone">The zone the killing damage landed on.</param>
    /// <param name="bone">The bone the round went through, or <c>-1</c> when nothing so precise.</param>
    /// <param name="category">The killing weapon's category, which picks the fall's family.</param>
    /// <remarks>
    /// Extracted from the guard-round handler so that EVERYTHING lethal runs it — a grenade that
    /// zeroes the health must kill exactly the way a bullet does, or the player stands at zero
    /// health forever, which is precisely the bug this fixed.
    /// </remarks>
    public void BeginPlayerDeath(Vector3d from, int zone, int bone, WeaponCategory category)
    {
        if (mission.Player is null || mission.IsPlayerDead)
        {
            return;
        }

        // Locomotion state 47 (0x40E560): the clip comes from the LAST ZONE STRUCK's row in the
        // player's own hit-zone table, one of its two reactions by rand(2). The player stops taking
        // input from here on -- it is a state, not a message.
        mission.IsPlayerDead = true;
        mission.Player.ResetOptics();
        mission.Player.StopWeaponsForDeath();
        host.StopBinocularTransitionSound();

        // The player receives no further simulation ticks after death, so the weapon sound player
        // cannot observe a quiet tick and retire an automatic loop on its own.
        host.ResetWeaponSounds();
        int fall = HumanPlayerDeath.Choose(zone, HumanHitZoneTable.Player, mission.ShotRandom.Next(2));

        // The lethal hit has already written the retail corner flash. Do not replace it with a uniform
        // full-screen wash here: 0x412440 writes the flash as part of damage receipt, before it changes
        // the locomotion state to death.

        // Which way the body is thrown: the facing whose backward axis points away from the killer,
        // exactly as the soldier death faces the incoming round (SoldierDeath.TurnToTheShot).
        double throwX = mission.Player.Body.Position.X - from.X;
        double throwY = mission.Player.Body.Position.Y - from.Y;
        double throwLength = Math.Sqrt((throwX * throwX) + (throwY * throwY));
        float throwFacing = throwLength > 1e-6
            ? (float)Math.Atan2(throwX / throwLength, -(throwY / throwLength))
            : mission.Player.Body.Yaw;

        host.SoldierPresenter?.KillPlayerBody(
            fall,
            mission.Player.Body.Position,
            throwFacing,
            bone,
            new Vector3d(
                mission.Player.Body.Position.X - from.X,
                mission.Player.Body.Position.Y - from.Y,
                mission.Player.Body.Position.Z - from.Z),
            category,
            new Vector3d(
                mission.Player.Body.Velocity.X,
                mission.Player.Body.Velocity.Y,
                mission.Player.Body.Velocity.Z));

        if (mission.Solids is { } deathSolids)
        {
            WorldRayCast deathCaster = deathSolids.Caster();
            host.BeginDeathCamera(mission.Player.Body.Position, throwFacing, deathCaster);
        }

        // And the death sound -- state 47's own sub_4638A0(human, "player_death_", 3).
        host.PlayDeathSound(HumanPlayerDeath.SoundPrefix, HumanPlayerDeath.SoundVariants, null);
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
            host.PlayLandingSound(landing);
        }

        // State 35 names one of these three definitions only when damage is non-zero. Like the player's
        // own hit and death sounds it belongs to the listener, so it is flat rather than world-panned.
        if (!string.IsNullOrEmpty(impact.SoundName))
        {
            host.PlayDefinition(impact.SoundName, priority: 250);
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
        mission.DamageFlash.Hit(Vector3f.Zero, mission.SoundRandom.NextSingle());
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
        player.InstallViewShake(
            HumanViewShake.HitPositionUnits, HumanViewShake.HitAngleRadians, HumanViewShake.FenceTicks);
        mission.DamageFlash.HitEveryCorner(
            mission.SoundRandom.NextSingle(),
            mission.SoundRandom.NextSingle(),
            mission.SoundRandom.NextSingle(),
            mission.SoundRandom.NextSingle());
        if (!shock.IsLethal)
        {
            // 0x412F80 selects this fence-specific cue after it has added the direct zone-13 dose.
            host.PlayDefinition("elfence_spark1", priority: 250);
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
