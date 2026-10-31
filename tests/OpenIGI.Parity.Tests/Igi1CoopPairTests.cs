using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>I.G.I. missions played by two real games together: what one player does, the other's game shows.</summary>
[Collection(ParityGroup.Name)]
public sealed class Igi1CoopPairTests
{
    [RequiresGameDataFact]
    public void Both_games_come_up_the_joiner_is_welcomed_and_each_sees_the_other_walk()
    {
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Vector3d from = partner.Player.Body.Position;
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        pair.RunUntil(() => false, 300);
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);

        double walked = Vector3d.Distance(from, partner.Player.Body.Position);
        Assert.True(walked > 2048, $"the joiner's walk moved its player on the host {walked} units; joiner cutscene {pair.Joiner!.World.Cutscenes?.ActiveScene?.Name ?? "none"}");

        // The host's player stands in the joiner's level where it stands in the host's, and nobody else does.
        Game.Igi1.Ai.PartnerBody host = Assert.Single(pair.Joiner.World.PartnerBodies!());
        Assert.Equal(Igi1ReplicatedWorld.HostEntity, host.Key);
        double apart = Vector3d.Distance(host.Feet, pair.Host.World.Player!.Body.Position);
        Assert.True(apart < 1024, $"the host's player is {apart} units from where the joiner shows it");
    }

    [RequiresGameDataFact]
    public void Every_soldier_is_sent_once_and_the_joiner_poses_each_one()
    {
        using Igi1CoopPair pair = Joined();
        pair.RunUntil(() => false, 60);
        Game.Multiplayer.Snapshot? latest = pair.Client.Session!.Latest;
        Assert.NotNull(latest);
        int[] soldiers = [.. latest.Humans.Where(static h => h.Id >= Igi1ReplicatedWorld.SoldierEntityBase).Select(static h => h.Id)];
        Assert.Equal(pair.Host!.World.Soldiers!.SoldierViews.Count(), soldiers.Length);
        Assert.Equal(soldiers.Length, soldiers.Distinct().Count());

        // Each soldier the joiner has in play is the host's puppet, standing where the host's stands. One the level keeps
        // out of play is never posed, there or here.
        foreach (Game.Igi1.Player.SoldierView mine in pair.Joiner!.World.Soldiers!.SoldierViews
            .Where(static v => !v.IsDead && Game.World.ConditionalContainerGate.IsTaskActive(v.Soldier)))
        {
            int runtime = pair.Joiner.World.Soldiers.RuntimeIdOf(mine);
            Game.Igi1.Player.SoldierView? theirs = pair.Host.World.Soldiers.SoldierByRuntimeId(runtime);
            Assert.NotNull(theirs);
            double apart = Vector3d.Distance(mine.Position, theirs.Position);
            Assert.True(apart < 4096, $"soldier {runtime} ({mine.Soldier.Name}) stands {apart} units from the host's");
        }
    }

    [RequiresGameDataFact]
    public void A_partner_standing_in_an_area_is_counted_there_by_the_host_and_the_joiners_level()
    {
        using Igi1CoopPair pair = Joined();

        // A level area that counts the player's team, empty, and far from where the host stands.
        Vector3d hostAt = pair.Host!.World.Player!.Body.Position;
        DeclaredTask[] areas = [.. pair.Host.World.LevelRoot!.Descendants().OfType<DeclaredTask>().Where(static t => t.Type.Name == "AreaActivate")];
        DeclaredTask? area = areas
            .Where(t => Text(t, "Criteria") is "CRITERIA_HUMAN0" && At(t) is not null && Count(pair.Host, $"AreaActivate_{t.Id}.nActive") == 0)
            .OrderByDescending(t => Vector3d.Distance(hostAt, At(t)!.Value))
            .FirstOrDefault();
        Assert.True(area is not null, $"no player area among {areas.Length}: e.g. {string.Join(" | ", areas.Take(2).Select(Describe))}");
        Vector3d centre = At(area)!.Value;
        string active = $"{area.Type.Name}_{area.Id}.nActive";
        Assert.Equal(0, Count(pair.Host, active));

        // The joiner stands in it: the host's partner, and the joiner's own player, put there.
        Igi1NetworkPlayer partner = Assert.Single(pair.Host.World.NetworkPlayers);
        partner.Player.Body.Position = centre;
        pair.Joiner!.World.Player!.Body.Position = centre;
        pair.RunUntil(() => false, 3);
        Assert.True(Count(pair.Host, active) > 0, $"{active} on the host counts {Count(pair.Host, active)} with the partner inside it");

        // And the host walks there too: the joiner's level counts the host as its partner.
        pair.Host.World.Player.Body.Position = centre;
        pair.RunUntil(() => false, 20);
        Assert.True(Count(pair.Joiner, active) >= 2, $"{active} on the joiner counts {Count(pair.Joiner, active)} with both players inside it");
    }

    [RequiresGameDataFact]
    public void The_hosts_level_decides_and_the_joiners_follows_its_counters_and_scenes()
    {
        using Igi1CoopPair pair = Joined();

        // Mission 1's landing scene waits on a counter an area adds to: set on the host, the scene runs on both.
        Assert.True(pair.Host!.World.Soldiers!.ScriptVariables!.TryWrite("EditVariable_199.nValue", QvmValue.FromInt32(1)));
        Assert.True(pair.RunUntil(() => pair.Host.World.Cutscenes?.ActiveScene?.Id == 1204, 60) >= 0, "the host's scene never ran");
        Assert.True(pair.RunUntil(() => pair.Joiner!.World.Cutscenes?.ActiveScene?.Id == 1204, 60) >= 0, "the joiner's scene never ran");
        Assert.Equal(Count(pair.Host, "EditVariable_199.nValue"), Count(pair.Joiner!, "EditVariable_199.nValue"));

        // The joiner's own counters are the host's: one written there is put back.
        Assert.True(pair.Joiner!.World.Soldiers!.ScriptVariables!.TryWrite("EditVariable_199.nValue", QvmValue.FromInt32(7)));
        pair.RunUntil(() => false, 40);
        Assert.Equal(Count(pair.Host, "EditVariable_199.nValue"), Count(pair.Joiner, "EditVariable_199.nValue"));

        // The joiner can't break it: the scene is the host's to skip.
        pair.Joiner.World.Cutscenes!.RequestBreak();
        pair.RunUntil(() => false, 5);
        Assert.Equal(1204, pair.Joiner.World.Cutscenes.ActiveScene?.Id);

        // And the scene ends on both.
        Assert.True(pair.RunUntil(() => pair.Host.World.Cutscenes?.ActiveScene is null && pair.Joiner.World.Cutscenes?.ActiveScene is null, 3000) >= 0,
            $"the scene runs on: host {pair.Host.World.Cutscenes?.ActiveScene?.Id}, joiner {pair.Joiner.World.Cutscenes?.ActiveScene?.Id}");
    }

    [RequiresGameDataFact]
    public void The_host_starts_the_mission_again_and_the_joiner_follows_it_into_the_new_level()
    {
        using Igi1CoopPair pair = Joined("-runat", "400", "mission");
        // The titles live on; each load builds the level again.
        object firstHost = pair.Host!.World.LevelRoot!;
        object firstJoiner = pair.Joiner!.World.LevelRoot!;
        Assert.True(pair.RunUntil(() => pair.Host.World.LevelRoot is { } now && !ReferenceEquals(now, firstHost), 1500) >= 0, "the host never started the mission again");
        Assert.True(pair.RunUntil(() => pair.Joiner.World.LevelRoot is { } now && !ReferenceEquals(now, firstJoiner), 1500) >= 0, "the joiner never followed");

        // Both in the new level, the joiner's player the host's partner there, and walking it moves it on the host.
        Assert.True(pair.RunUntil(() => pair.Host!.World.NetworkPlayers.Count == 1 && pair.Client.Session?.Latest is not null, 1500) >= 0, "the joiner never came into the host's new level");
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Vector3d from = partner.Player.Body.Position;
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        pair.RunUntil(() => false, 300);
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);
        double walked = Vector3d.Distance(from, partner.Player.Body.Position);
        Assert.True(walked > 2048, $"after the restart the joiner's walk moved its player on the host {walked} units");
    }

    [RequiresGameDataFact]
    public void The_host_going_on_to_the_next_mission_takes_the_joiner_there_and_both_begin_it_together()
    {
        using Igi1CoopPair pair = Joined();
        Assert.Equal(1, pair.Joiner!.World.MissionId);

        // Into the mission a while, as a mission is finished: both levels long revealed.
        pair.RunUntil(() => false, 150);
        object hostLevel = pair.Host!.World.LevelRoot!;
        object joinerLevel = pair.Joiner.World.LevelRoot!;

        // As a completed mission goes on: the host starts the next one itself.
        typeof(Host.GameHost).GetMethod("StartMission", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(pair.HostGame, [2]);

        // Each level's first frame up, and the first frame it ran: the joiner loads alongside the host, and the two begin
        // together, whichever was up first holding for the other.
        List<string> told = [];
        int frame = 0;
        int heard = -1;
        (int Up, long Ticks, int Began) host = (-1, 0, -1);
        (int Up, long Ticks, int Began) joiner = (-1, 0, -1);
        void Watch(Igi1Gameplay game, object before, ref (int Up, long Ticks, int Began) side)
        {
            if (side.Up < 0 && game.World.LevelRoot is { } now && !ReferenceEquals(now, before))
            {
                side = (frame, game.World.PlayerTicks, -1);
            }
            else if (side.Up >= 0 && side.Began < 0 && game.World.PlayerTicks > side.Ticks)
            {
                side.Began = frame;
            }
        }

        Assert.True(
            pair.RunUntil(
                () =>
                {
                    frame++;
                    if (pair.Client.Session?.Map is { } map && (told.Count == 0 || told[^1] != map))
                    {
                        told.Add(map);
                    }

                    if (heard < 0 && pair.Client.Session?.WaitingForServer == true)
                    {
                        heard = frame;
                    }

                    Watch(pair.Host!, hostLevel, ref host);
                    Watch(pair.Joiner!, joinerLevel, ref joiner);
                    return host.Began >= 0 && joiner.Began >= 0 && pair.Host!.World.NetworkPlayers.Count == 1;
                },
                3000) >= 0,
            $"the two never both began mission 2 (host {host}, joiner {joiner}, joiner in {pair.Joiner.World.MissionId})");
        Assert.Equal(2, pair.Joiner.World.MissionId);
        Assert.DoesNotContain("1", told.Skip(1));
        Assert.True(Math.Abs(host.Began - joiner.Began) <= 3, $"the joiner heard on frame {heard}; the host was up on frame {host.Up} and began on {host.Began}, the joiner up on {joiner.Up} and began on {joiner.Began}");
    }

    [RequiresGameDataFact]
    public void What_a_player_takes_is_taken_by_that_player_and_gone_for_the_other()
    {
        using Igi1CoopPair pair = Joined();
        Game.World.GunPickupRegistry hostGuns = pair.Host!.World.GunPickups!;
        Game.World.GunPickupRegistry joinerGuns = pair.Joiner!.World.GunPickups!;
        Assert.Equal(hostGuns.Items.Count, joinerGuns.Items.Count);

        // A guard killed on the host drops his gun, and the joiner's level has it lying in the same place -- once.
        Vector3d hostAt = pair.Host.World.Player!.Body.Position;
        Game.Igi1.Player.SoldierView guard = pair.Host.World.Soldiers!.SoldierViews
            .Where(static v => !v.IsDead && Game.World.ConditionalContainerGate.IsTaskActive(v.Soldier))
            .OrderBy(v => Vector3d.Distance(v.Position, hostAt))
            .First();
        int before = hostGuns.Items.Count;
        pair.Host.World.Soldiers.ApplyExplosion(guard.Position, 2048, 4096, 50f);
        Assert.True(pair.RunUntil(() => guard.IsDead, 60) >= 0, $"the blast never killed {guard.Soldier.Name}");
        Assert.True(pair.RunUntil(() => hostGuns.Items.Count > before, 600) >= 0, $"the killed guard {guard.Soldier.Name} dropped nothing");
        Assert.True(pair.RunUntil(() => joinerGuns.Items.Count == hostGuns.Items.Count, 120) >= 0, $"the joiner has {joinerGuns.Items.Count} guns to the host's {hostGuns.Items.Count}");
        pair.RunUntil(() => false, 200);
        Assert.Equal(hostGuns.Items.Count, joinerGuns.Items.Count);
        Game.World.GunPickup dropped = hostGuns.Items[^1];
        Assert.True(Vector3d.Distance(dropped.Position, joinerGuns.Items[^1].Position) < 64, "the dropped gun lies elsewhere on the joiner");

        // The joiner walks over it: on the host its player has the gun and the host's doesn't; on the joiner, likewise.
        Igi1NetworkPlayer partner = Assert.Single(pair.Host.World.NetworkPlayers);
        bool hostHad = pair.Host.World.Player!.Weapons!.HasWeapon(dropped.Weapon.WeaponId);
        partner.Player.Body.Position = dropped.Position;
        pair.Joiner.World.Player!.Body.Position = dropped.Position;
        Assert.True(pair.RunUntil(() => dropped.IsCollected, 30) >= 0, "the partner never took the gun on the host");
        Assert.True(partner.Player.Weapons!.HasWeapon(dropped.Weapon.WeaponId), "the partner on the host has no gun");
        Assert.Equal(hostHad, pair.Host.World.Player.Weapons.HasWeapon(dropped.Weapon.WeaponId));
        Assert.True(pair.RunUntil(() => joinerGuns.Items[^1].IsCollected, 60) >= 0, "the joiner's level still has the gun lying there");
        Assert.True(pair.Joiner.World.Player.Weapons!.HasWeapon(dropped.Weapon.WeaponId), "the joiner's own player never got the gun");
    }

    [RequiresGameDataFact]
    public void A_joiner_whose_line_drops_comes_back_to_its_own_player_where_it_was()
    {
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Vector3d away = partner.Player.Body.Position + new Vector3d(8192, 0, 0);
        partner.Player.Body.Position = away;
        pair.Joiner!.World.Player!.Body.Position = away;
        pair.RunUntil(() => false, 10);

        // Dropped: out of the host's level, but kept.
        pair.Reconnect(frames: 60);
        Assert.Empty(pair.Host.World.NetworkPlayers);

        // Back under the same name: the same player, where it was.
        Assert.True(pair.RunUntil(() => pair.Host.World.NetworkPlayers.Count == 1 && pair.Client.LocalEntity is not null, 600) >= 0, "the joiner never came back");
        Igi1NetworkPlayer back = Assert.Single(pair.Host.World.NetworkPlayers);
        Assert.Same(partner, back);
        Assert.True(Vector3d.Distance(back.Player.Body.Position, away) < 2048, $"the joiner came back {Vector3d.Distance(back.Player.Body.Position, away)} units from where it was");
    }

    [RequiresGameDataFact]
    public void The_host_closing_the_room_ends_the_joiners_session_with_a_reason()
    {
        using Igi1CoopPair pair = Joined();
        Assert.Null(pair.Client.Ended);
        pair.Session.Dispose();
        Assert.True(pair.RunUntil(() => pair.Client.Ended is not null, 120) >= 0, "the joiner never heard the room close");
    }

    [RequiresGameDataFact]
    public void What_one_player_says_and_marks_reaches_the_other_under_its_name()
    {
        using Igi1CoopPair pair = Joined();
        pair.Client.Say("Go, go, go!");
        pair.Client.Mark(new Game.Multiplayer.Int3(10, 20, 30));
        pair.Session.Say("Hold position.");
        List<Game.Multiplayer.ChatLine> hostHeard = [];
        List<Game.Multiplayer.ChatLine> joinerHeard = [];
        List<Game.Multiplayer.WorldMark> hostMarks = [];
        Assert.True(pair.RunUntil(
            () =>
            {
                hostHeard.AddRange(pair.Session.TakeChat());
                joinerHeard.AddRange(pair.Client.TakeChat());
                hostMarks.AddRange(pair.Session.TakeMarks());
                return hostHeard.Count >= 2 && joinerHeard.Count >= 2 && hostMarks.Count >= 1;
            },
            120) >= 0, $"host heard {hostHeard.Count}, joiner heard {joinerHeard.Count}, host saw {hostMarks.Count} marks");

        int joiner = pair.Client.LocalEntity!.Value;
        Assert.Contains(new Game.Multiplayer.ChatLine(joiner, "Go, go, go!"), hostHeard);
        Assert.Contains(new Game.Multiplayer.ChatLine(Igi1ReplicatedWorld.HostEntity, "Hold position."), joinerHeard);
        Assert.Contains(new Game.Multiplayer.WorldMark(joiner, new Game.Multiplayer.Int3(10, 20, 30)), hostMarks);
    }

    [RequiresGameDataFact]
    public void A_players_round_hurts_a_teammate_only_with_friendly_fire_on()
    {
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Vector3d eye = pair.Host.World.Player!.Body.Eye;
        Vector3d chest = partner.Player.Body.Position + new Vector3d(0, 0, 1.3 * 4096);
        Vector3d beyond = eye + ((chest - eye) * 2);
        bool Shoot() => pair.Host.World.Bodies.Shoot(
            eye,
            beyond,
            pair.Host.World.PlayerTask,
            hit => new Game.Igi1.Ai.Igi1WeaponHit(hit, eye, beyond, 1f, Game.Weapons.WeaponCategory.Gun, Shooter: null, FromPlayer: true).ToShared(),
            out _);

        float before = partner.Health.RemainingFraction;
        try
        {
            Game.Multiplayer.CoopCVars.FriendlyFire.Value = false;
            Assert.False(Shoot(), "a round met the teammate with friendly fire off");
            Assert.Equal(before, partner.Health.RemainingFraction);

            Game.Multiplayer.CoopCVars.FriendlyFire.Value = true;
            Assert.True(Shoot(), "a round passed through the teammate with friendly fire on");
            Assert.True(partner.Health.RemainingFraction < before, "the teammate was met and not hurt");
        }
        finally
        {
            Game.Multiplayer.CoopCVars.FriendlyFire.Value = false;
        }
    }

    [RequiresGameDataFact]
    public void A_player_the_host_removes_is_told_and_is_not_let_back_in()
    {
        using Igi1CoopPair pair = Joined();
        Assert.Equal(["Anya"], pair.Session.Players);
        pair.Session.Kick("Anya");
        Assert.True(pair.RunUntil(() => pair.Client.Ended is not null, 120) >= 0, "the removed player was never told");
        Assert.Empty(pair.Host!.World.NetworkPlayers);

        // Its game shows the room closed over a paused mission; asking to come back in under that name is refused at the door.
        Assert.True(pair.Session.Session!.IsBarred("Anya"));
    }

    [RequiresGameDataFact]
    public void The_enemies_option_toughens_every_soldier_and_never_a_player()
    {
        using Igi1CoopPair pair = Joined();
        try
        {
            Game.Multiplayer.CoopCVars.EnemyHealthPerPlayer.Value = 150;
            pair.RunUntil(() => false, 5);
            Game.Igi1.Ai.SoldierSimulation soldiers = pair.Host!.World.Soldiers!;

            // Two players at +150% each past the first: two and a half times, for every soldier there is now.
            foreach (Game.Igi1.Player.SoldierView soldier in soldiers.SoldierViews.Where(static s => !s.IsDead))
            {
                Game.Player.HumanHealth health = soldiers.HealthOf(soldier);
                Assert.Equal(health.BaseMaximumHealth * 2.5f, health.MaximumHealth, 3);
            }

            // And for one whose health is made afterwards -- a generator's replacement, a soldier a script brings in.
            Game.Igi1.Player.SoldierView later = soldiers.SoldierViews.First(static s => !s.IsDead);
            object context = typeof(Game.Igi1.Ai.SoldierSimulation)
                .GetField("_context", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(soldiers)!;
            var healths = (System.Collections.IDictionary)context.GetType().GetProperty("Health")!.GetValue(context)!;
            healths.Remove(later);
            Game.Player.HumanHealth fresh = soldiers.HealthOf(later);
            Assert.Equal(fresh.BaseMaximumHealth * 2.5f, fresh.MaximumHealth, 3);

            // The players keep their own.
            Assert.Equal(pair.Host.World.PlayerHealth.BaseMaximumHealth, pair.Host.World.PlayerHealth.MaximumHealth);
            Igi1NetworkPlayer partner = Assert.Single(pair.Host.World.NetworkPlayers);
            Assert.Equal(partner.Health.BaseMaximumHealth, partner.Health.MaximumHealth);
        }
        finally
        {
            Game.Multiplayer.CoopCVars.EnemyHealthPerPlayer.Value = 0;
        }
    }

    [RequiresGameDataFact]
    public void A_downed_player_sees_what_its_teammate_sees()
    {
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        pair.Host.Harm.BeginPlayerDeath(pair.Host.World.Player!.Body.Position, zone: 0, bone: -1, Game.Weapons.WeaponCategory.Gun);
        pair.RunUntil(() => false, 30);

        Core.Graphics.Camera3D eye = (Core.Graphics.Camera3D)typeof(Host.GameHost)
            .GetField("_inspectorEye", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pair.HostGame)!;
        Vector3d partnerEye = Game.World.WorldScene.ToMetres(partner.Player.Body.Eye);
        Core.Numerics.Matrix3f look = Core.Graphics.Camera3D.LookRotation(partner.Player.Body.Yaw, partner.Player.AimPitch);
        Assert.True(Vector3d.Distance(eye.Position, partnerEye) < 1.9, $"the view is {Vector3d.Distance(eye.Position, partnerEye):0.0} m from the teammate's eye");
        float along = (eye.Orientation.M31 * look.M31) + (eye.Orientation.M32 * look.M32) + (eye.Orientation.M33 * look.M33);
        Assert.True(along > 0.99f, $"the view looks {MathF.Acos(Math.Clamp(along, -1f, 1f)) * 180f / MathF.PI:0} degrees off the teammate's");
    }

    [RequiresGameDataFact]
    public void The_dead_stay_dead_on_both_games_when_the_host_falls()
    {
        using Igi1CoopPair pair = Joined();
        int[] ids = KillSoldiers(pair);

        // Each corpse lies in its death clip, on both games, and holds its gun's count of shots.
        Game.Igi1.Ai.SoldierSimulation soldiers = pair.Host!.World.Soldiers!;
        Game.Igi1.Ai.SoldierSimulation joiners = pair.Joiner!.World.Soldiers!;
        Dictionary<int, (int Host, int Joiner, int Shots)> lying = ids.ToDictionary(
            static id => id,
            id => (soldiers.SoldierByRuntimeId(id)!.CurrentAnimation, joiners.SoldierByRuntimeId(id)?.CurrentAnimation ?? -1,
                soldiers.AgentOf(soldiers.SoldierByRuntimeId(id)!)?.ShotSequence ?? 0));

        // The joiner fights on away from the host, out of reach to revive it.
        Assert.Single(pair.Host.World.NetworkPlayers).Player.Body.Position += new Vector3d(20000, 0, 0);
        pair.Host.Harm.BeginPlayerDeath(pair.Host.World.Player!.Body.Position, zone: 0, bone: -1, Game.Weapons.WeaponCategory.Gun);
        for (int second = 0; second < 20; second++)
        {
            pair.RunUntil(() => false, 30);
            AssertStillDead(pair, ids, $"{second} s after the host fell");
            foreach (int id in ids)
            {
                (int Host, int Joiner, int Shots) now = (
                    soldiers.SoldierByRuntimeId(id)!.CurrentAnimation,
                    joiners.SoldierByRuntimeId(id)?.CurrentAnimation ?? -1,
                    soldiers.AgentOf(soldiers.SoldierByRuntimeId(id)!)?.ShotSequence ?? 0);
                Assert.True(now == lying[id], $"soldier {id} {second} s after the host fell: clip/joiner clip/shots {lying[id]} -> {now}");
            }
        }

        Assert.True(pair.Host.World.IsPlayerDead, "the host's player is not down");

        // Nor is the mission failed under the host while its partner plays on: its restart would be the host's alone.
        object session = typeof(Host.GameHost)
            .GetField("_session", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pair.HostGame)!;
        Assert.False((bool)session.GetType().GetProperty("FailedMenu")!.GetValue(session)!, "the host's mission failed while its partner played on");
        Assert.False(pair.Host.World.Objectives?.IsFailed ?? false, "the host's level failed while its partner played on");
    }

    [RequiresGameDataFact]
    public void The_dead_stay_dead_on_both_games_when_the_joiner_revives_the_host()
    {
        using Igi1CoopPair pair = Joined();
        int[] ids = KillSoldiers(pair);

        pair.Host!.Harm.BeginPlayerDeath(pair.Host.World.Player!.Body.Position, zone: 0, bone: -1, Game.Weapons.WeaponCategory.Gun);
        pair.RunUntil(() => false, 5);
        Assert.True(pair.Host.World.IsPlayerDead, "the host's player did not fall");
        Game.Igi1.Ai.SoldierSimulation soldiers = pair.Host.World.Soldiers!;
        Dictionary<int, int> shots = ids.ToDictionary(static id => id, id => soldiers.AgentOf(soldiers.SoldierByRuntimeId(id)!)?.ShotSequence ?? 0);
        Assert.True(pair.RunUntil(() => !pair.Host.World.IsPlayerDead, 30 * 30) >= 0, "the joiner beside the host never revived it");
        for (int second = 0; second < 10; second++)
        {
            pair.RunUntil(() => false, 30);
            AssertStillDead(pair, ids, $"{second} s after the host was revived");
            foreach (int id in ids)
            {
                Assert.Equal((id, shots[id]), (id, soldiers.AgentOf(soldiers.SoldierByRuntimeId(id)!)?.ShotSequence ?? 0));
            }
        }
    }

    // Kills soldiers on the host with blasts, and gives the runtime ids of every soldier dead once the joiner has seen it.
    private static int[] KillSoldiers(Igi1CoopPair pair)
    {
        pair.RunUntil(() => false, 60);
        Game.Igi1.Ai.SoldierSimulation soldiers = pair.Host!.World.Soldiers!;
        foreach (Game.Igi1.Player.SoldierView soldier in soldiers.SoldierViews.Where(static v => !v.IsDead).Take(6).ToArray())
        {
            soldiers.ApplyExplosion(soldier.Position, radiusUnits: 40000, falloffUnits: 40000, damageFactor: 1000f);
        }

        pair.RunUntil(() => false, 90);
        int[] ids = [.. soldiers.SoldierViews.Where(static v => v.IsDead).Select(soldiers.RuntimeIdOf)];
        Assert.NotEmpty(ids);
        AssertStillDead(pair, ids, "once killed");
        return ids;
    }

    // Each of the soldiers dead on the host, in what the joiner is sent, and on the joiner's own screen.
    private static void AssertStillDead(Igi1CoopPair pair, int[] ids, string when)
    {
        foreach (int id in ids)
        {
            Game.Igi1.Player.SoldierView? host = pair.Host!.World.Soldiers!.SoldierByRuntimeId(id);
            Assert.True(host is null || host.IsDead, $"soldier {id} stood up on the host {when}");
            Assert.True(
                pair.Client.Session!.Latest is not { } latest
                    || !latest.TryGet(Igi1ReplicatedWorld.SoldierEntityBase + id, out Game.Multiplayer.ReplicatedHuman sent)
                    || (sent.Flags & Game.Multiplayer.ReplicatedHumanCondition.Alive) == 0,
                $"soldier {id} is sent alive to the joiner {when}");
            Game.Igi1.Player.SoldierView? joiner = pair.Joiner!.World.Soldiers!.SoldierByRuntimeId(id);
            Assert.True(joiner is null || joiner.IsDead, $"soldier {id} stands on the joiner's screen {when}");
        }
    }

    [RequiresGameDataFact]
    public void A_joiners_own_round_bleeds_a_soldier_and_leaves_its_life_to_the_host()
    {
        using Igi1CoopPair pair = Joined();
        pair.RunUntil(() => false, 60);
        Game.Igi1.Ai.SoldierSimulation soldiers = pair.Host!.World.Soldiers!;
        Game.Igi1.Ai.SoldierSimulation copies = pair.Joiner!.World.Soldiers!;
        Game.World.GunPickupRegistry joinerGuns = pair.Joiner.World.GunPickups!;
        int gunsBefore = joinerGuns.Items.Count;
        Game.Igi1.Player.SoldierView guard = NearestLivingSoldier(pair.Host, pair.Host.World.Player!.Body.Position);
        int id = soldiers.RuntimeIdOf(guard);
        Game.Igi1.Player.SoldierView copy = copies.SoldierByRuntimeId(id)!;

        // The joiner's player, stood square before its copy of the guard, fires through its chest, many times over.
        Game.Igi1.Player.PlayerCamera own = pair.Joiner.World.Player!;
        own.Body.Position = copy.Position + new Vector3d(0, -2 * 4096, 0);
        for (int round = 0; round < 20; round++)
        {
            Vector3d eye = own.Body.Eye;
            Vector3d chest = copy.Position + new Vector3d(0, 0, 1.3 * 4096);
            Vector3d beyond = eye + ((chest - eye) * 2);
            Assert.True(pair.Joiner.World.Bodies.Shoot(
                eye, beyond, pair.Joiner.World.PlayerTask,
                hit => new Game.Igi1.Ai.Igi1WeaponHit(hit, eye, beyond, 1000f, Game.Weapons.WeaponCategory.Gun, Shooter: null, FromPlayer: true).ToShared(),
                out _), $"round {round} passed through the joiner's copy of {guard.Soldier.Name}");
            pair.RunUntil(() => false, 2);
        }

        // Nothing of that is the joiner's to decide: its copy lives as long as the host's soldier does, whole, and dropped nothing.
        Assert.False(copy.IsDead, "the joiner's copy died of the joiner's own rounds");
        Assert.Equal(0f, copies.HealthOf(copy).Damage);
        Assert.False(guard.IsDead, "the host's soldier died of rounds that never reached it");
        Assert.Equal(gunsBefore, joinerGuns.Items.Count);

        // Killed on the host, it falls on the joiner too and drops one gun there, where the host's lies -- which the joiner takes.
        Game.World.GunPickupRegistry hostGuns = pair.Host.World.GunPickups!;
        int hostGunsBefore = hostGuns.Items.Count;
        soldiers.ApplyExplosion(guard.Position, 2048, 4096, 50f);
        Assert.True(pair.RunUntil(() => guard.IsDead && copy.IsDead, 60) >= 0, "the host's kill never reached the joiner");
        Assert.True(pair.RunUntil(() => hostGuns.Items.Count > hostGunsBefore, 600) >= 0, "the killed guard dropped nothing on the host");
        pair.RunUntil(() => false, 200);
        Assert.Equal(hostGuns.Items.Count - hostGunsBefore, joinerGuns.Items.Count - gunsBefore);
        Game.World.GunPickup dropped = hostGuns.Items[^1];
        Assert.True(Vector3d.Distance(dropped.Position, joinerGuns.Items[^1].Position) < 64, "the dropped gun lies elsewhere on the joiner");

        Igi1NetworkPlayer partner = Assert.Single(pair.Host.World.NetworkPlayers);
        partner.Player.Body.Position = dropped.Position;
        own.Body.Position = dropped.Position;
        Assert.True(pair.RunUntil(() => dropped.IsCollected, 30) >= 0, "the partner never took the gun on the host");
        Assert.True(pair.RunUntil(() => joinerGuns.Items[^1].IsCollected, 60) >= 0, "the joiner's level still has the gun lying there");
        Assert.True(own.Weapons!.HasWeapon(dropped.Weapon.WeaponId), "the joiner's own player never got the gun");
        Assert.True(partner.Player.Weapons!.HasWeapon(dropped.Weapon.WeaponId), "the partner on the host has no gun");
    }

    [RequiresGameDataFact]
    public void A_partners_round_meets_a_soldier_where_its_client_drew_it()
    {
        using Igi1CoopPair pair = Joined();
        pair.RunUntil(() => false, 60);
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Game.Igi1.Ai.SoldierSimulation soldiers = pair.Host.World.Soldiers!;
        Game.Igi1.Player.SoldierView guard = NearestLivingSoldier(pair.Host, partner.Player.Body.Position);

        // The guard stands still, three metres before the partner, long enough for the server's history to have it there.
        Vector3d stood = partner.Player.Body.Position + new Vector3d(0, 3 * 4096, 0);
        Game.Igi1.Ai.SoldierPuppet still = new(stood, 0f, guard.CurrentAnimation, -1, 0f, Alive: true);
        soldiers.Puppet(guard, still);
        pair.RunUntil(() => false, 40);
        long seenAt = pair.Session.Session!.LastSnapshot!.Tick;
        Assert.True(pair.Session.Session.LagCompensationHistory.Sample(seenAt, Igi1ReplicatedWorld.SoldierEntityBase + soldiers.RuntimeIdOf(guard), out Game.Multiplayer.ReplicatedHuman seen));
        Assert.True(Vector3d.Distance(seen.Position.ToVector(), stood) < 64);

        // Then it is ten metres to the side when the partner's command, made while its client drew the guard before it, runs.
        soldiers.Puppet(guard, still with { Position = stood + new Vector3d(10 * 4096, 0, 0) });
        pair.RunUntil(() => false, 5);
        Assert.True(Vector3d.Distance(guard.Position, stood) > 9 * 4096, "the guard never moved aside");

        Vector3d eye = partner.Player.Body.Eye;
        Vector3d chest = stood + new Vector3d(0, 0, 1.3 * 4096);
        Vector3d beyond = eye + ((chest - eye) * 2);
        bool Shoot() => pair.Host.World.Bodies.Shoot(
            eye, beyond, pair.Host.World.PlayerTask,
            hit => new Game.Igi1.Ai.Igi1WeaponHit(hit, eye, beyond, 0.1f, Game.Weapons.WeaponCategory.Gun, Shooter: null, FromPlayer: true).ToShared(),
            out _);

        // Where the server has the guard now, the round passes through empty air; put back where the client drew it, it is met.
        float whole = soldiers.HealthOf(guard).Damage;
        Assert.False(Shoot(), "a round met the guard where its client had never drawn it");
        partner.BeforeCommand!(seenAt + 2, seenAt);
        try
        {
            Assert.True(Vector3d.Distance(guard.Position, stood) < 64, $"the guard was put back {Vector3d.Distance(guard.Position, stood)} units from where the client drew it");
            Assert.True(Shoot(), "a round passed through the guard where its client drew it");
            Assert.True(soldiers.HealthOf(guard).Damage > whole, "the guard was met and not hurt");
        }
        finally
        {
            partner.AfterCommand!();
        }

        // And it stands where this tick has it again once the command has run.
        Assert.True(Vector3d.Distance(guard.Position, stood) > 9 * 4096, "the guard was not put back where the server has it");
        Assert.False(soldiers.Rewound);
    }

    [RequiresGameDataFact]
    public void A_late_joiner_never_sees_the_intro_the_host_has_passed()
    {
        using Igi1CoopPair pair = new(hostArgs: [], joinerArgs: [], joinLater: true);
        Assert.True(pair.RunUntil(() => pair.Host?.World.Cutscenes is not null && pair.Host.World.Player is not null, 3000) >= 0, "the host's level never came up");
        pair.RunUntil(() => false, 300);
        Assert.False(pair.Host!.World.Cutscenes!.IsPlaying, "the host is still in its intro");

        // The joiner comes to the level the host is playing, with no skip of its own: its scenes are the host's. Its level
        // is held unrun until they are, so no tick of it ever has a scene of its own playing -- the authored intro that the
        // loaded level starts with is put out by the first tick, before anything of the level is seen.
        bool sawScene = false;
        bool Watch()
        {
            sawScene |= pair.Joiner is { World: { PlayerTicks: > 0, Cutscenes: { IsPlaying: true } } };
            return false;
        }

        pair.StartJoiner();
        Assert.True(pair.RunUntil(() => { Watch(); return pair.Joined && pair.Host.World.NetworkPlayers.Count == 1; }, 3000) >= 0, "the joiner was never welcomed");
        pair.RunUntil(Watch, 120);
        Assert.False(sawScene, "the late joiner saw the intro cutscene");
        Assert.NotNull(pair.Joiner!.World.Player);
    }

    // The living soldier nearest a place on a game, among those the level has in play.
    private static Game.Igi1.Player.SoldierView NearestLivingSoldier(Igi1Gameplay game, Vector3d near) =>
        game.World.Soldiers!.SoldierViews
            .Where(static v => !v.IsDead && Game.World.ConditionalContainerGate.IsTaskActive(v.Soldier))
            .OrderBy(v => Vector3d.Distance(v.Position, near))
            .First();

    [RequiresGameDataFact]
    public void A_downed_squad_stands_again_where_it_last_stood_together()
    {
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        pair.RunUntil(() => false, 15 * 30 + 30);
        Vector3d hostKept = pair.Host.World.Player!.Body.Position;
        Vector3d partnerKept = partner.Player.Body.Position;

        // Gone on and down, every one of them: the host's world can stand them up where they last stood together.
        pair.Host.World.Player.Body.Position += new Vector3d(20000, 0, 0);
        partner.Player.Body.Position += new Vector3d(20000, 0, 0);
        pair.Host.Harm.BeginPlayerDeath(pair.Host.World.Player.Body.Position, zone: 0, bone: -1, Game.Weapons.WeaponCategory.Gun);
        pair.Host.Harm.BeginPlayerDeath(partner, partner.Player.Body.Position, zone: 0, bone: -1, Game.Weapons.WeaponCategory.Gun);
        pair.RunUntil(() => false, 5);
        Igi1ReplicatedWorld world = (Igi1ReplicatedWorld)pair.Session.Session!.GetType()
            .GetField("_world", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pair.Session.Session)!;
        Assert.True(world.CanRetryFromCheckpoint, "the downed squad cannot stand again");

        world.RetryFromCheckpoint();
        pair.RunUntil(() => false, 5);
        Assert.False(pair.Host.World.IsPlayerDead);
        Assert.False(partner.IsDead);
        Assert.True(Vector3d.Distance(pair.Host.World.Player.Body.Position, hostKept) < 2048, "the host stood up away from the checkpoint");
        Assert.True(Vector3d.Distance(partner.Player.Body.Position, partnerKept) < 2048, "the partner stood up away from the checkpoint");
    }

    [RequiresGameDataFact]
    public void A_players_voice_reaches_the_other_under_its_entity_and_decodes()
    {
        using Igi1CoopPair pair = Joined();
        short[] tone = new short[960];
        for (int i = 0; i < tone.Length; i++)
        {
            tone[i] = (short)(8000 * Math.Sin(i * 2 * Math.PI * 440 / 48000));
        }

        Concentus.IOpusEncoder encoder = Concentus.OpusCodecFactory.CreateEncoder(48000, 1, Concentus.Enums.OpusApplication.OPUS_APPLICATION_VOIP);
        byte[] packet = new byte[400];
        int length = encoder.Encode(tone, 960, packet, packet.Length);
        pair.Client.Speak(packet.AsSpan(0, length));

        List<Game.Multiplayer.VoicePacket> heard = [];
        Assert.True(pair.RunUntil(() => { heard.AddRange(pair.Session.TakeVoices()); return heard.Count > 0; }, 60) >= 0, "the host never heard the joiner");
        Game.Multiplayer.VoicePacket voice = Assert.Single(heard);
        Assert.Equal(pair.Client.LocalEntity, voice.Entity);

        short[] decoded = new short[960];
        Assert.Equal(960, Concentus.OpusCodecFactory.CreateDecoder(48000, 1).Decode(voice.Packet, decoded, 960, false));
        Assert.True(decoded.Max(static s => Math.Abs((int)s)) > 1000, "the voice decoded to silence");

        // Where the speaker stands is known to the host, to place the voice there.
        Assert.NotNull(pair.Session.WhereIs(voice.Entity));
    }

    [RequiresGameDataFact]
    public void A_joiner_riding_a_lift_the_host_calls_rides_it_on_both_games_without_being_put_back()
    {
        using Igi1CoopPair pair = Joined(mission: 3);
        PlacedElevator lift = Assert.IsType<PlacedElevator>(pair.Host!.World.Elevators?.Elevators.FirstOrDefault(static l => l.State.FloorCount > 1));
        PlacedElevator joinerLift = pair.Joiner!.World.Elevators!.Elevators[pair.Host.World.Elevators!.Elevators.ToList().IndexOf(lift)];
        Igi1NetworkPlayer partner = Assert.Single(pair.Host.World.NetworkPlayers);
        Igi1ClientWorld joinerWorld = Assert.IsType<Igi1ClientWorld>(pair.Client.World);
        ClientSession session = pair.Client.Session!;

        // The joiner stands in the car, on both games.
        Vector3d aboard = lift.State.Position + new Vector3d(0, 0, 64);
        partner.Player.Body.Position = aboard;
        pair.Joiner.World.Player!.Body.Position = aboard;
        pair.RunUntil(() => false, 30);
        double startZ = partner.Player.Body.Position.Z;
        int to = lift.State.CurrentFloorIndex == 0 ? 1 : 0;

        // The car is called on the host alone, as the host's own press calls it: the joiner's game hears of it only by the
        // snapshots. Before, its car stood while the host's rose, and the server left its partner on the floor below.
        WorldInteraction button = Assert.Single(
            pair.Host.World.Interactions!.Items,
            item => item.Task.Type.Name == "Switch" && item.Task.IsSelfOrDescendantOf(lift.Task));
        long correctionsBefore = session.Reconciler.Corrections;
        button.Press();

        List<double> hostTrip = [];
        List<(double Z, int Lead)> joinerTrip = [];
        Assert.True(
            pair.RunUntil(
                () =>
                {
                    int lead = session.SentCommands.Count > 0 && session.LastRunCommandTick != long.MinValue
                        ? (int)(session.SentCommands[^1].Tick - session.LastRunCommandTick)
                        : 0;
                    hostTrip.Add(lift.State.Position.Z);
                    joinerTrip.Add((joinerLift.State.Position.Z, lead));
                    return lift.State.CurrentFloorIndex == to && !lift.State.IsMoving;
                },
                3000) >= 0,
            "the host's car never arrived");

        // The rest of the joiner's commands from the ride reach the server.
        pair.RunUntil(() => false, 15);
        long rideCorrections = session.Reconciler.Corrections - correctionsBefore;

        // The server carried the partner with the car.
        double rode = Math.Abs(partner.Player.Body.Position.Z - startZ);
        double floors = Math.Abs(lift.State.Floors[to].Position.Z - lift.State.Floors[1 - to].Position.Z);
        Assert.True(rode > floors * 0.8, $"the partner rode {rode} of the {floors} units between the floors on the host");

        // The joiner's car went with the host's, taken once: each frame it stood where the host's stood then or up to the
        // joiner's lead (and a few ticks of slack) later -- its own commands are that far ahead -- and it stopped there too.
        double worstApart = 0;
        for (int i = 0; i < joinerTrip.Count; i++)
        {
            (double z, int lead) = joinerTrip[i];
            double nearest = double.MaxValue;
            for (int k = i; k <= Math.Min(hostTrip.Count - 1, i + lead + 4); k++)
            {
                nearest = Math.Min(nearest, Math.Abs(hostTrip[k] - z));
            }

            worstApart = Math.Max(worstApart, nearest);
        }

        Assert.True(worstApart < 1024, $"the joiner's car was {worstApart} units from anywhere the host's stood over its lead");
        Assert.True(Math.Abs(joinerLift.State.Position.Z - lift.State.Position.Z) < 64, "the joiner's car stopped elsewhere than the host's");
        Assert.InRange(joinerWorld.LiftsTaken, 1, 2);

        // And its player rode it there, put back once at most rather than tick after tick. (Afterwards the guards at the top
        // may shoot it, whose shove the joiner's game does not feel: only the ride is counted.)
        Assert.True(rideCorrections <= 3, $"the joiner's player was put back {rideCorrections} times on the ride");
        double height = Math.Abs(pair.Joiner.World.Player.Body.Position.Z - partner.Player.Body.Position.Z);
        Assert.True(height < 64, $"the joiner's player stands {height} units above or below where the host has it");
    }

    private static Igi1CoopPair Joined(params string[] hostArgs) => Joined(1, hostArgs);

    private static Igi1CoopPair Joined(int mission, params string[] hostArgs)
    {
        Igi1CoopPair pair = new(mission: mission, hostArgs: hostArgs);
        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");
        return pair;
    }

    private static int Count(Igi1Gameplay game, string variable) =>
        game.World.Soldiers!.ScriptVariables!.TryRead(variable, out QvmValue value) ? value.ToInt32() : -1;

    private static Vector3d? At(DeclaredTask task) => task.TryGetValue("Position", out object? p) && p is Vector3d v ? v : null;

    private static string Text(DeclaredTask task, string name) => task.TryGetValue(name, out object? v) ? v?.ToString() ?? string.Empty : string.Empty;

    private static string Describe(DeclaredTask task) =>
        $"{task.Id}: Position={(task.TryGetValue("Position", out object? p) ? $"{p?.GetType().Name}:{p}" : "missing")} Criteria={(task.TryGetValue("Criteria", out object? c) ? $"{c?.GetType().Name}:{c}" : "missing")}";
}
