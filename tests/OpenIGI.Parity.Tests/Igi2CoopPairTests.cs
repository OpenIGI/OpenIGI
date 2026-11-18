using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Input;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>I.G.I. 2 missions played by two real games together: what one player does, the other's game shows.</summary>
[Collection(ParityGroup.Name)]
public sealed class Igi2CoopPairTests
{
    [RequiresTitleDataFact("igi2")]
    public void The_joiner_is_welcomed_its_level_let_go_and_each_sees_the_other_walk()
    {
        using Igi2CoopPair pair = Joined();
        Assert.False(pair.Client.HoldsLevel, "the joiner's level is still held behind its loading card");

        Igi2NetworkHuman partner = Assert.Single(pair.Host!.NetworkHumans);
        Vector3d from = partner.Human.Position;
        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        pair.RunUntil(() => false, 300);
        Igi2CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);
        double walked = Vector3d.Distance(from, partner.Human.Position);
        Assert.True(walked > 2048, $"the joiner's walk moved its player on the host {walked} units");

        // The host's player stands in the joiner's level where it stands in the host's.
        Igi2NetworkHuman host = Assert.Single(pair.Joiner!.NetworkHumans);
        double apart = Vector3d.Distance(host.Human.Position, pair.Host.Human.Position);
        Assert.True(apart < 1024, $"the host's player is {apart} units from where the joiner shows it");
    }

    [RequiresTitleDataFact("igi2")]
    public void Every_soldier_is_sent_and_the_joiner_poses_its_copies_where_the_hosts_stand()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => false, 120);
        Snapshot? latest = pair.Client.Session!.Latest;
        Assert.NotNull(latest);
        IReadOnlyList<Igi2HumanSoldier> host = pair.Host!.Soldiers!.Bodies;
        IReadOnlyList<Igi2HumanSoldier> joiner = pair.Joiner!.Soldiers!.Bodies;
        Assert.Equal(host.Count, joiner.Count);
        int sent = latest.Humans.Count(static h => ReplicatedEntity.IsSoldier(h.Id));
        Assert.Equal(host.Count(static s => s.IsActive), sent);

        // No mind of the joiner's runs a soldier: every one is the host's puppet, standing where the snapshots it draws put it --
        // the host's a moment ago, as every other player is drawn.
        ClientSession session = pair.Client.Session!;
        Assert.NotNull(session.DrawnTick);
        for (int i = 0; i < joiner.Count; i++)
        {
            Assert.True(joiner[i].Puppet is not null, $"soldier {i} runs its own mind on the joiner");
            Assert.True(host[i].Puppet is null, $"soldier {i} is a puppet on the host");
            if (host[i].IsActive && !host[i].IsDead && session.Remote.Sample(session.DrawnTick.Value, Igi2ReplicatedWorld.SoldierEntity(i), out ReplicatedHuman seen))
            {
                double apart = Vector3d.Distance(seen.Position.ToVector(), joiner[i].Position);
                Assert.True(
                    apart < 1024,
                    $"soldier {i} stands {apart} units from where the snapshots put it (active here {joiner[i].IsActive}, {Vector3d.Distance(host[i].Position, joiner[i].Position)} from the host's)");
            }
        }
    }

    [RequiresTitleDataFact("igi2")]
    public void A_soldier_the_host_kills_falls_on_the_joiner()
    {
        using Igi2CoopPair pair = Joined();
        int index = IndexOfLiving(pair.Host!.Soldiers!.Bodies);
        Assert.True(index >= 0, "the mission has no living soldier");
        pair.Host.Soldiers.Bodies[index].KillOutright();
        Igi2HumanSoldier copy = pair.Joiner!.Soldiers!.Bodies[index];
        Assert.True(pair.RunUntil(() => copy.IsDead, 300) >= 0, "the joiner's copy of the soldier never fell");
    }

    [RequiresTitleDataFact("igi2")]
    public void The_joiners_copy_of_the_host_is_no_target_of_its_soldiers_and_the_hosts_partner_is()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300);
        Igi2NetworkHuman hostShown = Assert.Single(pair.Joiner!.NetworkHumans);
        Assert.DoesNotContain(hostShown.Human, Players(pair.Joiner));

        Igi2NetworkHuman partner = Assert.Single(pair.Host!.NetworkHumans);
        Assert.Contains(partner.Human, Players(pair.Host));
    }

    [RequiresTitleDataFact("igi2")]
    public void The_hosts_partner_carries_the_missions_loadout()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => false, 30);
        string[] own = Carried(pair.Host!.Weapons!.Inventory);
        Assert.NotEmpty(own);
        Igi2NetworkHuman partner = Assert.Single(pair.Host.NetworkHumans);
        Assert.Equal(own, Carried(partner.Weapons!.Inventory));
        Assert.Equal(own, Carried(pair.Joiner!.Weapons!.Inventory));
    }

    [RequiresTitleDataFact("igi2")]
    public void A_level_variable_the_host_changes_is_changed_on_the_joiner()
    {
        using Igi2CoopPair pair = Joined();
        NativeRegistry hostNatives = pair.Host!.Level!.Natives;
        NativeRegistry joinerNatives = pair.Joiner!.Level!.Natives;
        // A level variable the host's own tasks leave as a script writes it: one its task works out afresh each tick (an alarm's
        // isActive) is no test of what is sent.
        string? name = null;
        QvmSymbol? hostVariable = null;
        int value = 0;
        foreach (string candidate in hostNatives.Names.Where(static n => n.Contains('.', StringComparison.Ordinal)
            && !n.StartsWith("Human", StringComparison.Ordinal)).Order(StringComparer.Ordinal).Take(400))
        {
            if (!hostNatives.TryResolve(candidate, out QvmSymbol? symbol) || symbol.Variable is not { ValueType: QvmValueKind.Integer } variable)
            {
                continue;
            }

            int was = variable.Read().ToInt32();
            int wanted = was == 0 ? 1 : 0;
            variable.TryWrite(QvmValue.FromInt32(wanted), out _);
            pair.RunUntil(() => false, 12);
            if (variable.Read().ToInt32() == wanted)
            {
                (name, hostVariable, value) = (candidate, symbol, wanted);
                break;
            }

            variable.TryWrite(QvmValue.FromInt32(was), out _);
        }

        Assert.NotNull(name);
        Assert.True(joinerNatives.TryResolve(name, out QvmSymbol? joinerVariable));
        Assert.True(
            pair.RunUntil(() => joinerVariable.Read().ToInt32() == value, 120) >= 0,
            $"{name} is {joinerVariable.Read().ToInt32()} on the joiner, {value} on the host");
    }

    [RequiresTitleDataFact("igi2")]
    public void A_fallen_partner_lies_down_on_its_own_game_and_the_host_standing_by_brings_it_back()
    {
        using Igi2CoopPair pair = Joined();
        Igi2NetworkHuman partner = Assert.Single(pair.Host!.NetworkHumans);
        partner.Human.Damage.Damage = partner.Human.Damage.MaxDamage + 1f;
        partner.Human.UpdateDie();
        Assert.True(pair.RunUntil(() => pair.Joiner!.Human.IsDead, 300) >= 0, "the joiner's own player never fell");

        // The host walks to it: put beside it, and held there as long as a revival takes.
        pair.Host.Human.Position = partner.Human.Position + new Vector3d(2048, 0, 0);
        Assert.True(
            pair.RunUntil(() => !partner.Human.IsDead && !pair.Joiner!.Human.IsDead, CoopSquad.ReviveTicks * 4 + 300) >= 0,
            $"not revived: on the host {partner.Human.IsDead}, on the joiner {pair.Joiner!.Human.IsDead}");
    }

    [RequiresTitleDataFact("igi2")]
    public void A_door_the_host_opens_is_told_open_on_the_joiner()
    {
        using Igi2CoopPair pair = Joined();
        Game.Igi2.Interaction.Igi2DoorTask[] hostDoors = [.. pair.Host!.LevelTasks().OfType<Game.Igi2.Interaction.Igi2DoorTask>()];
        Game.Igi2.Interaction.Igi2DoorTask[] joinerDoors = [.. pair.Joiner!.LevelTasks().OfType<Game.Igi2.Interaction.Igi2DoorTask>()];
        int index = Array.FindIndex(hostDoors, static d => !d.IsCommandedOpen);
        Assert.True(index >= 0, $"no shut door among {hostDoors.Length}");
        hostDoors[index].Command(open: true, swingsBack: false);
        Assert.True(pair.RunUntil(() => joinerDoors[index].IsCommandedOpen, 120) >= 0, $"door {index} never opened on the joiner");
    }

    [RequiresTitleDataFact("igi2")]
    public void A_weapon_taken_on_the_host_is_gone_on_the_joiner()
    {
        using Igi2CoopPair pair = Joined();
        Game.Igi2.Pickups.Igi2GunPickupTask[] host = [.. pair.Host!.LevelTasks().OfType<Game.Igi2.Pickups.Igi2GunPickupTask>()];
        Game.Igi2.Pickups.Igi2GunPickupTask[] joiner = [.. pair.Joiner!.LevelTasks().OfType<Game.Igi2.Pickups.Igi2GunPickupTask>()];
        int index = Array.FindIndex(host, static g => g.Pickup is { IsDelete: false });
        Assert.True(index >= 0, $"no weapon lying among {host.Length}");
        host[index].Pickup!.TakenElsewhere();
        Assert.True(pair.RunUntil(() => joiner[index].Pickup is not { IsDelete: false }, 120) >= 0, $"weapon {index} still lies on the joiner");
    }

    [RequiresTitleDataFact("igi2")]
    public void The_joiner_follows_the_host_into_the_mission_it_goes_on_to()
    {
        using Igi2CoopPair pair = Joined();
        object hostLevel = pair.Host!, joinerLevel = pair.Joiner!;
        pair.RunUntil(() => false, 60);
        const int next = 21;
        typeof(Host.GameHost).GetMethod("StartMission", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(pair.HostGame, [next]);
        // Every level the joiner plays on the way, by the mission it is: one, the host's next, loaded once.
        List<int> joinerMissions = [];
        object? lastJoiner = null;
        Assert.True(
            pair.RunUntil(
                () =>
                {
                    if (pair.Joiner is { } now && !ReferenceEquals(now, joinerLevel) && !ReferenceEquals(now, lastJoiner))
                    {
                        lastJoiner = now;
                        joinerMissions.Add(now.MissionId);
                    }

                    return pair.Host is { } h && !ReferenceEquals(h, hostLevel) && pair.Joiner is { } j && !ReferenceEquals(j, joinerLevel)
                        && h.NetworkHumans.Count == 1 && !pair.Client.HoldsLevel && j.TickCount > 150;
                },
                6000) >= 0,
            $"the pair never came into the next level: host on {pair.Host?.MissionId}, joiner on {pair.Joiner?.MissionId}, held {pair.Client.HoldsLevel}");
        Assert.Equal(next, pair.Host!.MissionId);
        Assert.Equal([next], joinerMissions);
    }

    [RequiresTitleDataFact("igi2")]
    public void An_explosion_on_the_host_goes_off_on_the_joiner_once_and_harms_nobody_there()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => false, 30);
        Game.Igi2.Weapons.Igi2ExplosionWorld host = pair.Host!.Explosions!;
        Game.Igi2.Weapons.Igi2ExplosionWorld joiner = pair.Joiner!.Explosions!;
        int replays = 0;
        joiner.Started = _ => replays++;
        int before = Blasts(joiner);
        Vector3d at = pair.Host.Human.Position + new Vector3d(40960, 0, 0);
        host.Start(new Game.Igi2.Weapons.Igi2ExplosionRequest(
            at, 1, 1, 1f, 4096f, 4096f, 1024f, 4, 2, null, string.Empty, string.Empty, -1, 0, 0f, new Core.Numerics.Vector3f(0, 0, 1),
            1.5707964f, 272f, 1360f, null, null, default));
        Assert.True(pair.RunUntil(() => Blasts(joiner) > before, 120) >= 0, "the explosion never went off on the joiner");
        pair.RunUntil(() => false, 60);
        Assert.Equal(0, replays);

        static int Blasts(Game.Igi2.Weapons.Igi2ExplosionWorld world) => world.FragmentGroups.Count + world.Dynamic.Tasks.OfType<Game.Igi2.Weapons.Igi2Explosion>().Count();
    }

    [RequiresTitleDataFact("igi2")]
    public void A_round_the_host_fires_is_fired_again_from_its_copy_on_the_joiner_and_harms_nobody_there()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300);
        Game.Igi2.Weapons.Igi2WeaponFiring host = pair.Host!.Weapons!.Firing;
        Game.Igi2.Weapons.Igi2WeaponTypeTask weapon = host.Inventory.Current!.WeaponType;
        Game.Igi2.Weapons.Igi2GunTask gun = Assert.IsType<Game.Igi2.Weapons.Igi2GunTask>(weapon.FireType(0));

        // Straight at the joiner's own player, from the host's eyes.
        Vector3d eye = pair.Host.Human.View.Position;
        Vector3d target = pair.Joiner!.Human.Position + new Vector3d(0, 0, 4096);
        Vector3d along = target - eye;
        float yaw = (float)Math.Atan2(-along.X, along.Y);
        float pitch = (float)Math.Atan2(along.Z, Math.Sqrt((along.X * along.X) + (along.Y * along.Y)));
        float[] orientation = Game.EngineEuler.Rotation(pitch, 0f, yaw);
        Game.Igi2.Weapons.Igi2WeaponFiring copy = Assert.Single(pair.Joiner.NetworkHumans).Weapons!.Firing;
        Assert.True(copy.IsReplica);
        int before = copy.GunShot.BulletsFired;
        float health = pair.Joiner.Human.Damage.Damage;
        host.Launched!(new Game.Igi2.Weapons.Igi2LaunchNote(
            new Game.Igi2.Weapons.Igi2ProjectileLaunch(gun.ProjectileTaskType, eye, orientation, default, gun.MuzzleVelocityMps, gun.EffectiveRangeMeters, 0, weapon, null),
            0));
        Assert.True(pair.RunUntil(() => copy.GunShot.BulletsFired > before, 120) >= 0, "the host's round was never fired again on the joiner");
        pair.RunUntil(() => false, 10);
        Assert.Equal(health, pair.Joiner.Human.Damage.Damage);
    }

    [RequiresTitleDataFact("igi2")]
    public void A_lift_travelling_on_the_host_travels_with_it_on_the_joiner()
    {
        using Igi2CoopPair pair = JoinedOn(LiftMission);
        Game.Igi2.Interaction.Igi2LiftTask[] host = [.. pair.Host!.LevelTasks().OfType<Game.Igi2.Interaction.Igi2LiftTask>()];
        Game.Igi2.Interaction.Igi2LiftTask[] joiner = [.. pair.Joiner!.LevelTasks().OfType<Game.Igi2.Interaction.Igi2LiftTask>()];
        Assert.Equal(host.Length, joiner.Length);
        int index = Array.FindIndex(host, static l => l.Children.OfType<Game.Igi2.Interaction.Igi2FloorTask>().Count() >= 2);
        Assert.True(index >= 0, $"the mission has no lift of two floors among {host.Length}");

        // The host's lift set off from its first floor to its second.
        Game.Igi2.Interaction.Igi2FloorTask[] floors = [.. host[index].Children.OfType<Game.Igi2.Interaction.Igi2FloorTask>()];
        host[index].Follow(new Game.Igi2.Interaction.Igi2LiftLeg(0, 1, floors[0].Position, 40f), 0);
        pair.RunUntil(() => false, 20);
        Game.Igi2.Interaction.Igi2LiftLeg there = host[index].Leg();
        Game.Igi2.Interaction.Igi2LiftLeg here = joiner[index].Leg();
        Assert.Equal(there.Target, here.Target);
        double apart = Vector3d.Distance(there.Position, here.Position);
        Assert.True(apart < 4096, $"the joiner's car is {apart} units from the host's");
    }

    [RequiresTitleDataFact("igi2")]
    public void The_joiners_cars_stand_where_the_hosts_do_and_one_blown_up_there_is_a_wreck_here()
    {
        using Igi2CoopPair pair = JoinedOn(CarMission);
        pair.RunUntil(() => false, 60);
        Game.Igi2.Vehicles.Igi2CarTask[] host = [.. pair.Host!.LevelTasks().OfType<Game.Igi2.Vehicles.Igi2CarTask>()];
        Game.Igi2.Vehicles.Igi2CarTask[] joiner = [.. pair.Joiner!.LevelTasks().OfType<Game.Igi2.Vehicles.Igi2CarTask>()];
        Assert.Equal(host.Length, joiner.Length);
        Assert.True(host.Length > 0, "the mission has no cars");
        for (int i = 0; i < host.Length; i++)
        {
            Assert.True(joiner[i].IsPuppet, $"car {i} drives itself on the joiner");
            Assert.False(host[i].IsPuppet, $"car {i} is a puppet on the host");
            double apart = Vector3d.Distance(host[i].CurrentPosition, joiner[i].CurrentPosition);
            Assert.True(apart < 4096, $"car {i} stands {apart} units from the host's");
        }

        int index = Array.FindIndex(host, static c => !c.IsExploded && c.CarType is not null);
        Assert.True(index >= 0, "no car that can blow up");
        host[index].HitZoneDestroyed();
        Assert.True(pair.RunUntil(() => joiner[index].IsExploded, 300) >= 0, $"car {index} blew up on the host and not on the joiner");
    }

    [RequiresTitleDataFact("igi2")]
    public void The_joiners_trains_run_where_the_hosts_do()
    {
        using Igi2CoopPair pair = JoinedOn(TrainMission);
        pair.RunUntil(() => false, 90);
        Game.Igi2.Vehicles.Igi2TrainTask[] host = [.. pair.Host!.LevelTasks().OfType<Game.Igi2.Vehicles.Igi2TrainTask>()];
        Game.Igi2.Vehicles.Igi2TrainTask[] joiner = [.. pair.Joiner!.LevelTasks().OfType<Game.Igi2.Vehicles.Igi2TrainTask>()];
        Assert.Equal(host.Length, joiner.Length);
        Assert.True(host.Length > 0, "the mission has no trains");
        for (int i = 0; i < host.Length; i++)
        {
            Assert.True(joiner[i].IsPuppet, $"train {i} runs itself on the joiner");
            double apart = Vector3d.Distance(host[i].CurrentPosition, joiner[i].CurrentPosition);
            Assert.True(apart < 16384, $"train {i} runs {apart} units from the host's");
        }
    }

    [RequiresTitleDataFact("igi2")]
    public void The_joiners_turrets_point_where_the_hosts_do()
    {
        using Igi2CoopPair pair = JoinedOn(TurretMission);
        pair.RunUntil(() => false, 60);
        Game.Igi2.Weapons.Igi2AiStationaryGunHolderTask[] host = [.. pair.Host!.LevelTasks().OfType<Game.Igi2.Weapons.Igi2AiStationaryGunHolderTask>()];
        Game.Igi2.Weapons.Igi2AiStationaryGunHolderTask[] joiner = [.. pair.Joiner!.LevelTasks().OfType<Game.Igi2.Weapons.Igi2AiStationaryGunHolderTask>()];
        Assert.True(host.Length > 0 && host.Length == joiner.Length, $"turrets: host {host.Length}, joiner {joiner.Length}");
        for (int i = 0; i < host.Length; i++)
        {
            if (host[i].Gun is not { } theirs || joiner[i].Gun is not { } mine)
            {
                continue;
            }

            Assert.True(mine.IsPuppet, $"turret {i} aims itself on the joiner");
            Assert.True(mine.Firing.IsReplica, $"turret {i}'s rounds harm on the joiner");
            Assert.InRange(Math.Abs(mine.Turn - theirs.Turn), 0f, 0.2f);
        }
    }

    [RequiresTitleDataFact("igi2")]
    public void Each_side_names_the_other_over_its_head_and_marks_a_fallen_partner_with_its_revival()
    {
        using Igi2CoopPair pair = Joined();
        pair.RunUntil(() => pair.Joiner!.NameTags().Count == 1, 300);
        Assert.Contains(pair.Host!.NameTags(), static t => t.Name == "Anya");
        Assert.Single(pair.Joiner!.NameTags());

        // The joiner falls: the host marks where it lies, and the joiner's own panel shows its revival under way.
        Igi2NetworkHuman partner = Assert.Single(pair.Host.NetworkHumans);
        partner.Human.Damage.Damage = partner.Human.Damage.MaxDamage + 1f;
        partner.Human.UpdateDie();
        Assert.True(pair.RunUntil(() => pair.Host.ReviveMarkers().Count == 1 && pair.Client.OwnRevive is not null, 300) >= 0,
            $"marks {pair.Host.ReviveMarkers().Count}, own revive {pair.Client.OwnRevive}");
        Assert.Empty(pair.Host.NameTags());
    }

    private static int IndexOfLiving(IReadOnlyList<Igi2HumanSoldier> soldiers)
    {
        for (int i = 0; i < soldiers.Count; i++)
        {
            if (soldiers[i] is { IsActive: true, IsDead: false })
            {
                return i;
            }
        }

        return -1;
    }

    private static List<object> Players(Game.Igi2.Player.Igi2TitlePlayer level)
    {
        List<object> players = [];
        if (level.Ai is { } ai)
        {
            foreach (object player in ai.Players)
            {
                players.Add(player);
            }
        }

        return players;
    }

    private static string[] Carried(Game.Igi2.Weapons.Igi2Inventory inventory) =>
        [.. inventory.Weapons.Where(static w => w.IsActive).Select(static w => w.WeaponType.WeaponId).Order(StringComparer.Ordinal)];

    // A mission with a lift of two floors or more.
    private const int LiftMission = Igi2CoopPair.FirstMission;

    // Missions with cars and a mounted gun, with trains, and with a turret.
    private const int CarMission = 15;
    private const int TrainMission = 12;
    private const int TurretMission = 17;

    /// <summary>
    /// A host alone in a campaign mission played together who falls fails the mission, as the level's own "player dead" flow
    /// fails it (HumanPlayer_0.isDead), and is not told to wait for a teammate who is not there.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_host_alone_who_falls_fails_the_mission_and_waits_for_nobody()
    {
        using Igi2CoopPair pair = new([], [], joinLater: true);
        Assert.True(pair.RunUntil(() => pair.Host is not null, 4000) >= 0, "the host's level never came up");
        pair.RunUntil(() => false, 60);

        pair.Host!.Human.Damage.Damage = pair.Host.Human.Damage.MaxDamage + 1f;
        pair.Host.Human.UpdateDie();
        pair.RunUntil(() => false, 30);
        Assert.Null(pair.Session.OwnRevive);
        Assert.True(
            pair.RunUntil(() => pair.Host.Outcome == OpenIGI.Game.Player.TitleMissionOutcome.Failed, 3000) >= 0,
            "the mission never failed on its only player's death");
    }

    /// <summary>A host down while its partner stands waits to be revived, and the mission goes on.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_host_down_while_its_partner_stands_waits_for_it_and_the_mission_goes_on()
    {
        using Igi2CoopPair pair = Joined();
        pair.Host!.Human.Damage.Damage = pair.Host.Human.Damage.MaxDamage + 1f;
        pair.Host.Human.UpdateDie();
        pair.RunUntil(() => false, 30);
        Assert.NotNull(pair.Session.OwnRevive);
        Assert.Equal(-1, pair.RunUntil(() => pair.Host.Outcome == OpenIGI.Game.Player.TitleMissionOutcome.Failed, 900));
    }

    /// <summary>
    /// Each player wears the look it chose: the joiner's body on the host is its look's, borrowed from the multiplayer game's pack
    /// as the first mission does not ship it, and the joiner draws the host in the host's.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Each_player_wears_its_look_on_the_others_game_a_body_the_level_lacks_borrowed()
    {
        using Igi2CoopPair pair = new("chn3", "igiagent2");
        Assert.True(
            pair.RunUntil(() => pair.Joined && pair.Host!.NetworkHumans.Count == 1 && pair.Joiner!.NetworkHumans.Count == 1, 4000) >= 0,
            "the joiner was never welcomed, or never drew the host");
        pair.RunUntil(() => false, 30);

        Assert.Equal("igiagent2_1", Assert.Single(pair.Host!.NetworkHumans).Human.Model?.Name);
        Assert.Equal("chn3_1", Assert.Single(pair.Joiner!.NetworkHumans).Human.Model?.Name);
        Assert.NotEmpty(Assert.Single(pair.Host.NetworkHumans).Human.Model!.BoneNames);
    }

    /// <summary>The joiner's copy of the host holds the weapon the host holds, drawn in its hand and posed with it.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_joiners_copy_of_the_host_holds_the_hosts_weapon()
    {
        using Igi2CoopPair pair = Joined();
        Assert.True(pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300) >= 0, "the joiner never drew the host");
        OpenIGI.Game.Igi2.Weapons.Igi2WeaponTypeTask? held = pair.Host!.Weapons?.Inventory.Current?.WeaponType;
        Assert.NotNull(held);

        Igi2NetworkHuman copy = Assert.Single(pair.Joiner!.NetworkHumans);
        Assert.True(
            pair.RunUntil(() => copy.Weapons?.Inventory.Current?.WeaponType.WeaponId == held.WeaponId, 120) >= 0,
            $"the copy holds {copy.Weapons?.Inventory.Current?.WeaponType.WeaponId ?? "nothing"}, the host {held.WeaponId}");
    }

    /// <summary>
    /// The joiner draws the host's gun in its copy's hands, the copy standing still the whole time: held is not enough, the
    /// clip the copy plays must hang it. A copy made unarmed and given its gun while it stood kept the unarmed clip -- the host
    /// stood empty-handed on the joiner's game while the host saw the joiner's gun.
    /// </summary>
    [RequiresTitleDataTheory("igi2")]
    [InlineData("", "")]
    [InlineData("chn3", "igiagent2")]
    public void The_joiner_draws_the_hosts_gun_in_its_copys_hands(string hostLook, string joinerLook)
    {
        using Igi2CoopPair pair = hostLook.Length == 0 ? Joined() : new(hostLook, joinerLook);
        Assert.True(
            pair.RunUntil(() => pair.Joined && pair.Joiner!.NetworkHumans.Count == 1, 4000) >= 0,
            "the joiner never drew the host");
        pair.RunUntil(() => false, 120);

        Igi2NetworkHuman copy = Assert.Single(pair.Joiner!.NetworkHumans);
        string? gun = copy.Weapons?.Inventory.Current?.WeaponType.FixedModel;
        Assert.NotNull(gun);
        IReadOnlyList<TitleWorldModel> drawn = Igi2BodyFrame.Build(copy.Human, copy.Human.BodyPose(), copy.Human.Model!.Name, gun, 0);
        Assert.Contains(drawn, part => string.Equals(part.Model, gun, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A joiner fallen while the host stands is not its own level's "player dead" (HumanPlayer_0.isDead), which shows "Mission
    /// failed": its level weighs the host's copy beside its own player, as the server's weighs both.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_joiner_down_while_the_host_stands_is_not_its_levels_player_dead()
    {
        using Igi2CoopPair pair = Joined();
        Assert.True(pair.RunUntil(() => pair.Joiner!.NetworkHumans.Count == 1, 300) >= 0, "the joiner never drew the host");
        Assert.True(pair.Joiner!.Level!.Natives.TryResolve("HumanPlayer_0.isDead", out QvmSymbol? isDead));

        Igi2NetworkHuman partner = Assert.Single(pair.Host!.NetworkHumans);
        partner.Human.Damage.Damage = partner.Human.Damage.MaxDamage + 1f;
        partner.Human.UpdateDie();
        Assert.True(pair.RunUntil(() => pair.Joiner.Human.IsDead, 300) >= 0, "the joiner's own player never fell");
        pair.RunUntil(() => false, 30);
        Assert.Equal(0, isDead.Variable!.Read().ToInt32());
    }

    /// <summary>
    /// Without friendly fire (coop_friendlyfire, off unless the host turns it on, as Project I.G.I.'s co-op has it) the host's
    /// rounds do its partner no harm, a soldier's still do; with it on, the host's do too.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_teammates_round_does_no_harm_without_friendly_fire()
    {
        using Igi2CoopPair pair = Joined();
        Igi2NetworkHuman partner = Assert.Single(pair.Host!.NetworkHumans);
        OpenIGI.Game.Igi2.Weapons.Igi2WeaponTypeTask gun = pair.Host.Weapons!.Inventory.Current!.WeaponType;
        OpenIGI.Game.Igi2.Weapons.Igi2WeaponHit From(OpenIGI.Game.Igi2.Weapons.IIgi2WeaponOwner? shooter) =>
            new(new OpenIGI.Game.Physics.WorldIntersection { Position = partner.Human.Position }, 0.05f, new OpenIGI.Core.Numerics.Vector3f(-40960f, 0f, 0f), shooter, gun, null);

        float before = partner.Human.Damage.Damage;
        partner.Human.WeaponHit(From(pair.Host.Human));
        Assert.Equal(before, partner.Human.Damage.Damage);
        pair.Host.Human.WeaponHit(From(partner.Human));
        Assert.Equal(0f, pair.Host.Human.Damage.Damage);

        partner.Human.WeaponHit(From(null));
        Assert.True(partner.Human.Damage.Damage > before, "a soldier's round did the partner no harm");

        float soldierHurt = partner.Human.Damage.Damage;
        CoopCVars.FriendlyFire.Value = true;
        try
        {
            partner.Human.WeaponHit(From(pair.Host.Human));
            Assert.True(partner.Human.Damage.Damage > soldierHurt, "with friendly fire on the host's round did no harm");
        }
        finally
        {
            CoopCVars.FriendlyFire.Value = false;
        }
    }

    internal static Igi2CoopPair Joined(params string[] hostArgs) => JoinedOn(Igi2CoopPair.FirstMission, hostArgs);

    internal static Igi2CoopPair JoinedOn(int mission, params string[] hostArgs)
    {
        Igi2CoopPair pair = new(mission, hostArgs: hostArgs);
        Assert.True(
            pair.RunUntil(() => pair.Joined && pair.Host!.NetworkHumans.Count == 1 && !pair.Client.HoldsLevel, 4000) >= 0,
            $"the joiner was never welcomed: entity {pair.Client.LocalEntity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}, joiner level {(pair.Joiner is null ? "none" : "up")}, held {pair.Client.HoldsLevel}");
        return pair;
    }
}
