using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Host;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>Co-op: what one player does to the level -- seen by a camera, a rocket into a truck -- is so on both games.</summary>
[Collection(ParityGroup.Name)]
public sealed class Igi1CoopWorldSyncTests
{
    // Mission 1's escape truck, which only a LAW rocket blows up.
    private const int EscapeTruckId = 1099;

    [RequiresGameDataFact]
    public void A_camera_sees_the_joiner_and_both_games_show_it_seen()
    {
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        SecurityCamera there = pair.Host.World.SecurityCameras!.Cameras.First(static c => c.IsActive);
        SecurityCamera here = Assert.Single(pair.Joiner!.World.SecurityCameras!.Cameras, c => c.Task.Id == there.Task.Id);

        // The joiner stands five metres in front of the lens, on both games; the host's own player is nowhere near it.
        Vector3d feet = Ahead(there, 5.0);
        Assert.True(Vector3d.Distance(pair.Host.World.Player!.Body.Position, feet) > 20 * WorldScene.UnitsPerMetre);
        bool seen = false;
        for (int frame = 0; frame < 240 && !(there.IsDetection && here.IsDetection); frame++)
        {
            partner.Player.Body.Position = feet;
            pair.Joiner.World.Player!.Body.Position = feet;
            Assert.True(pair.Step());
            seen |= there.Beep != SecurityCameraBeep.None;
        }

        Assert.True(seen, $"the host's {there.Task.Name} never saw the joiner");
        Assert.True(there.IsDetection, $"the host's {there.Task.Name} never confirmed the joiner");
        Assert.True(here.IsDetection, $"the joiner's {here.Task.Name} never showed the detection");
        Assert.Equal(SecurityCameraLamp.Red, here.Lamp);
    }

    [RequiresGameDataFact]
    public void The_joiners_rocket_blows_the_truck_up_on_both_games()
    {
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        static DestructibleObject Truck(Game.Igi1.World.Igi1Gameplay game) => Assert.Single(
            game.World.Destructibles!.Objects,
            static item => item.Owner.Id == EscapeTruckId && item.Kind == DestructibleObjectKind.Vehicle);
        DestructibleObject hostTruck = Truck(pair.Host);
        DestructibleObject joinerTruck = Truck(pair.Joiner!);

        // A LAW rocket of the joiner's, on the host's game, a metre above the truck and flying into it.
        Vector3d at = hostTruck.PositionUnits;
        Assert.True(partner.Player.Missiles.Spawn(
            at + new Vector3d(0, 0, 8192),
            at,
            MissileType.MissileDirect,
            60f,
            sourceWeaponId: DestructibleObjectRegistry.CarDestroyingWeaponId));

        bool both = pair.RunUntil(() => hostTruck.IsDestroyed && joinerTruck.IsDestroyed, 300) >= 0;
        Assert.True(
            both,
            $"host truck dying {hostTruck.IsDying} destroyed {hostTruck.IsDestroyed}; joiner truck dying {joinerTruck.IsDying} destroyed {joinerTruck.IsDestroyed}");
    }

    [RequiresGameDataFact]
    public void A_camera_the_host_shoots_out_is_shot_out_on_the_joiners_game()
    {
        using Igi1CoopPair pair = Joined();
        SecurityCamera there = pair.Host!.World.SecurityCameras!.Cameras[0];
        SecurityCamera here = Assert.Single(pair.Joiner!.World.SecurityCameras!.Cameras, c => c.Task.Id == there.Task.Id);

        Assert.True(pair.Host.Blasts.ShootOut(there.Task));
        Assert.True(there.IsExploded);
        Assert.True(pair.RunUntil(() => here.IsExploded, 60) >= 0, "the joiner's camera still stands");
        Assert.True(pair.Joiner.World.AlarmSpeakers!.IsDestroyed(here.Task));
    }

    [RequiresGameDataFact]
    public void Each_games_grenades_and_rockets_fly_on_the_other_game()
    {
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Game.Igi1.Player.PlayerCamera host = pair.Host.World.Player!;

        // The host's grenade (the pool the soldiers lob into too) and the joiner's rocket, each on the host's game.
        Assert.True(host.Grenades.Spawn(host.Body.Eye, new Vector3f(0f, 0f, 1f), 5f, GrenadeExplosionType.Normal));
        Vector3d from = partner.Player.Body.Eye + new Vector3d(0, 0, 40960);
        Assert.True(partner.Player.Missiles.Spawn(from, from + new Vector3d(409600, 0, 0), MissileType.MissileDirect, 60f));

        // On the joiner's game: the host's grenade is drawn where the host has it, the joiner's own rocket is its own to draw.
        bool shown = pair.RunUntil(() => pair.Joiner!.World.ShownProjectiles.Any(static p => p.Model == "135_01_1"), 30) >= 0;
        Assert.True(shown, $"the joiner shows {pair.Joiner!.World.ShownProjectiles.Count} of the host's projectiles");
        Assert.DoesNotContain(pair.Joiner.World.ShownProjectiles, p => p.Owner == pair.Client.LocalEntity);
        Assert.All(pair.Joiner.World.ShownProjectiles, static p => Assert.Equal(Igi1ReplicatedWorld.HostEntity, p.Owner));
        Game.Weapons.ShownProjectile grenade = pair.Joiner.World.ShownProjectiles.First(static p => p.Model == "135_01_1");
        LiveGrenade there = Assert.Single(host.Grenades.Grenades);
        Assert.True(Vector3d.Distance(grenade.Position, there.Position) < 4096, "the joiner draws the host's grenade far from it");
    }

    [RequiresGameDataFact]
    public void A_grenade_goes_off_while_the_hosts_player_lies_down()
    {
        // The soldiers lob into the host's pool: the level must run it on while the host waits for a revive.
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Game.Igi1.Player.PlayerCamera host = pair.Host.World.Player!;
        partner.Player.Body.Position += new Vector3d(20000, 0, 0);
        Assert.True(host.Grenades.Spawn(host.Body.Eye + new Vector3d(0, 0, 4096), new Vector3f(0f, 0f, 1f), 1f, GrenadeExplosionType.Normal, fuseTicks: 20));
        pair.Host.Harm.BeginPlayerDeath(host.Body.Position, zone: 0, bone: -1, WeaponCategory.Gun);
        Assert.True(pair.Host.World.IsPlayerDead);

        bool gone = pair.RunUntil(() => host.Grenades.Grenades.Count == 0, 120) >= 0;
        Assert.True(gone, "the host's grenade hung in the air while its player lay down");
    }

    [RequiresGameDataFact]
    public void A_fallen_partners_grenade_goes_off_where_it_lies()
    {
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        partner.Player.Body.Position += new Vector3d(20000, 0, 0);
        Assert.True(partner.Player.Grenades.Spawn(
            partner.Player.Body.Eye + new Vector3d(0, 0, 4096), new Vector3f(0f, 0f, 1f), 1f, GrenadeExplosionType.Normal, fuseTicks: 20));
        pair.Host.Harm.BeginPlayerDeath(partner, partner.Player.Body.Position, zone: 0, bone: -1, WeaponCategory.Gun);
        Assert.True(partner.IsDead);

        bool gone = pair.RunUntil(() => partner.Player.Grenades.Grenades.Count == 0, 120) >= 0;
        Assert.True(gone, "the fallen partner's grenade hung in the air");
    }

    [RequiresGameDataFact]
    public void The_hosts_grenade_goes_off_while_the_host_stands_at_the_map_computer()
    {
        // Computer_Create (0x46BB10) pauses nothing; the grenade is the level's own task (Grenade_Throw under Level_GetDynamic).
        using Igi1CoopPair pair = Joined();
        Game.Igi1.Player.PlayerCamera host = pair.Host!.World.Player!;
        // Opened as the host's computer opens it: its own flag, and the player's state 45.
        typeof(GameHost).GetField("_computerOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(pair.HostGame, true);
        host.SetMapComputerOpen(true);
        pair.RunUntil(() => false, 5);
        Assert.True(host.IsMapComputerOpen, "the host's map computer never opened");

        Assert.True(host.Grenades.Spawn(host.Body.Eye + new Vector3d(0, 0, 4096), new Vector3f(0f, 0f, 1f), 1f, GrenadeExplosionType.Normal, fuseTicks: 20));
        bool gone = pair.RunUntil(() => host.Grenades.Grenades.Count == 0, 120) >= 0;
        Assert.True(host.IsMapComputerOpen, "the computer closed on its own");
        Assert.True(gone, "the host's grenade hung in the air while the map was up");
    }

    [RequiresGameDataFact]
    public void A_task_the_host_finishes_is_finished_on_the_joiners_game()
    {
        using Igi1CoopPair pair = Joined();
        WorldInteraction there = pair.Host!.World.Interactions!.Items.First(
            static i => i.Type is InteractionActivateType.Terminal or InteractionActivateType.Cabinet or InteractionActivateType.Generic
                && !i.IsCompleted && !i.IsSearched);
        WorldInteraction here = Assert.Single(pair.Joiner!.World.Interactions!.Items, i => i.Task.Id == there.Task.Id);

        there.CompleteUse();
        bool followed = pair.RunUntil(() => here.IsCompleted || here.IsSearched, 60) >= 0;
        Assert.True(followed, $"the joiner's {here.Task.Name} ({here.Type}) never followed the host's");
        Assert.Equal(there.IsOn, here.IsOn);
    }

    private static Vector3d Ahead(SecurityCamera camera, double metres)
    {
        double distance = metres * WorldScene.UnitsPerMetre;
        return new Vector3d(
            camera.ViewPositionUnits.X + (camera.Orientation.M12 * distance),
            camera.ViewPositionUnits.Y + (camera.Orientation.M22 * distance),
            camera.ViewPositionUnits.Z + (camera.Orientation.M32 * distance) - 2828.8);
    }

    private static Igi1CoopPair Joined()
    {
        Igi1CoopPair pair = new(mission: 1);
        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");
        return pair;
    }
}
