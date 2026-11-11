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
        GameTask[] areas = [.. pair.Host.World.LevelRoot!.Descendants().Where(static t => t.Type.Name == "AreaActivate")];
        GameTask? area = areas
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

        // No script listens for the break key in it, so neither game offers the hold.
        Assert.False(pair.Host.World.Cutscenes!.CanBreak);
        Assert.False(pair.Joiner.World.Cutscenes!.CanBreak);

        // The joiner can't break it: the scene is the host's to skip.
        pair.Joiner.World.Cutscenes!.RequestBreak();
        pair.RunUntil(() => false, 5);
        Assert.Equal(1204, pair.Joiner.World.Cutscenes.ActiveScene?.Id);

        // And the scene ends on both.
        Assert.True(pair.RunUntil(() => pair.Host.World.Cutscenes?.ActiveScene is null && pair.Joiner.World.Cutscenes?.ActiveScene is null, 3000) >= 0,
            $"the scene runs on: host {pair.Host.World.Cutscenes?.ActiveScene?.Id}, joiner {pair.Joiner.World.Cutscenes?.ActiveScene?.Id}");
    }

    [RequiresGameDataTheory]
    [InlineData(1, "EditVariable_4004.nValue")]
    [InlineData(3, "EditVariable_1001.nValue")]
    public void The_host_may_skip_its_intro_and_the_break_held_ends_it_on_both_games(int mission, string skipped)
    {
        using Igi1CoopPair pair = new([], [], joinLater: false, mission: mission, hostSkipsIntro: false);
        Assert.True(pair.RunUntil(() => pair.Host?.World.Cutscenes?.IsPlaying == true && pair.Joined, 3000) >= 0, "the host's intro never began");
        pair.RunUntil(() => false, 30);

        // The intro's script listens for the key: the hold is offered to the host, and not to the joiner, whose scene is the host's.
        Assert.True(pair.Host!.World.Cutscenes!.CanBreak, $"the host's intro scene {pair.Host.World.Cutscenes.ActiveScene?.Id} is not offered the skip");
        Assert.False(pair.Joiner!.World.Cutscenes!.CanBreak);

        // Held from the hold's completing, as the host holds it, until the intro has ended -- on both games.
        int ended = pair.RunUntil(
            () =>
            {
                pair.Host.World.Cutscenes.RequestBreak();
                return !pair.Host.World.Cutscenes.IsPlaying;
            },
            150);
        Assert.True(ended >= 0, $"the break held 150 frames and the host's intro scene {pair.Host.World.Cutscenes.ActiveScene?.Id} runs on");
        Assert.Equal(1, Count(pair.Host, skipped));
        Assert.True(pair.RunUntil(() => !pair.Joiner.World.Cutscenes.IsPlaying, 150) >= 0, "the host skipped its intro, and the joiner's runs on");

        // Skipped once, the intro offers no second skip.
        Assert.False(pair.Host.World.Cutscenes.CanBreak);
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
    public void A_joiner_whose_line_goes_quiet_joins_again_by_itself_as_its_own_player()
    {
        using Igi1CoopPair pair = Joined();
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);

        // The line goes dead both ways, past the client's timeout: it opens a new one and joins again by itself.
        pair.JoinerLink.Send.Loss = 1;
        pair.JoinerLink.Receive.Loss = 1;
        Assert.True(pair.RunUntil(() => pair.Client.IsRejoining, 200_000) >= 0, "the joiner never noticed its line went quiet");
        Assert.True(
            pair.RunUntil(() => !pair.Client.IsRejoining && pair.Client.LocalEntity is not null, 200_000) >= 0,
            $"the joiner never came back: {pair.Client.Ended}");

        // As its own player, the only one, and its session never ended.
        Assert.Null(pair.Client.Ended);
        Assert.True(pair.RunUntil(() => pair.Host.World.NetworkPlayers.Count == 1, 600) >= 0);
        Assert.Same(partner, Assert.Single(pair.Host.World.NetworkPlayers));
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

        // Each game's drawing hears the fall, and empties the guard's hand of the gun that dropped: the joiner's drew it in
        // the corpse's hand beside the host's drop, a second gun that no pickup took away.
        Dictionary<string, bool> emptied = [];
        foreach ((string side, Game.Igi1.Ai.SoldierSimulation sim, Game.Igi1.Player.SoldierView fallen) in
            new[] { ("host", soldiers, guard), ("joiner", copies, copy) })
        {
            Action<Game.Igi1.Ai.SoldierFall>? drawing = sim.BodyFell;
            sim.BodyFell = fall =>
            {
                drawing?.Invoke(fall);
                if (ReferenceEquals(fall.View, fallen))
                {
                    emptied[side] = fall.DroppedWeapon;
                }
            };
        }

        soldiers.ApplyExplosion(guard.Position, 2048, 4096, 50f);
        Assert.True(pair.RunUntil(() => guard.IsDead && copy.IsDead, 60) >= 0, "the host's kill never reached the joiner");
        Assert.True(emptied.GetValueOrDefault("host"), "the host's guard fell with its gun in hand");
        Assert.True(emptied.GetValueOrDefault("joiner"), "the joiner's guard fell with its gun in hand");
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
        partner.BeforeCommand!(partner, seenAt + 2, seenAt, 0f);
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
    [RequiresGameDataFact]
    public void A_joiner_knocked_by_rounds_on_the_host_is_knocked_as_far_on_its_own_game()
    {
        using Igi1CoopPair pair = Joined();
        pair.RunUntil(() => false, 30);
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Game.Igi1.Player.PlayerCamera own = pair.Joiner!.World.Player!;
        ClientSession session = pair.Client.Session!;

        // A way the knock carries clear: the ground beside the spawn has edges a knock stops against, as the host's game has
        // them too, so the host stands a metre behind the partner along each way in turn until two rounds carry it well on.
        Vector3d home = partner.Player.Body.Position;
        void Stand(Vector3d away)
        {
            partner.Player.Body.Position = home;
            pair.Host.World.Player!.Body.Position = home - (away * 4096);
            pair.RunUntil(() => false, 30);
        }

        void Knock(int rounds, Action between)
        {
            for (int round = 0; round < rounds; round++)
            {
                Vector3d eye = pair.Host.World.Player!.Body.Eye;
                Vector3d chest = partner.Player.Body.Position + new Vector3d(0, 0, 1.3 * 4096);
                Vector3d beyond = eye + ((chest - eye) * 2);
                Assert.True(
                    pair.Host.World.Bodies.Shoot(
                        eye,
                        beyond,
                        pair.Host.World.PlayerTask,
                        hit => new Game.Igi1.Ai.Igi1WeaponHit(hit, eye, beyond, 0.02f, Game.Weapons.WeaponCategory.Gun, Shooter: null, FromPlayer: true).ToShared(),
                        out _),
                    "the round missed the partner");
                between();
            }
        }

        Vector3d? clear = null;
        Game.Multiplayer.CoopCVars.FriendlyFire.Value = true;
        try
        {
            foreach (Vector3d away in new[] { new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, -1, 0) })
            {
                Stand(away);
                Knock(2, () => pair.RunUntil(() => false, 4));
                pair.RunUntil(() => false, 20);
                if (Vector3d.Distance(home, partner.Player.Body.Position) > 1200)
                {
                    clear = away;
                    break;
                }
            }
        }
        finally
        {
            Game.Multiplayer.CoopCVars.FriendlyFire.Value = false;
        }

        Assert.True(clear is not null, "no way from the spawn carries a knock clear");
        Stand(clear!.Value);
        pair.RunUntil(() => false, 60);

        // Rounds a few ticks apart, each knocking the partner along the floor on the host: the host's own, with friendly fire
        // on, through the same handler a guard's goes through.
        long correctionsBefore = session.Reconciler.Corrections;
        Vector3d start = partner.Player.Body.Position;
        List<string> errors = [];
        long seen = correctionsBefore;
        int frame = 0;
        void Watch(int frames)
        {
            for (int i = 0; i < frames; i++, frame++)
            {
                Assert.True(pair.Step());
                if (session.Reconciler.Corrections != seen)
                {
                    seen = session.Reconciler.Corrections;
                    errors.Add($"f{frame}:{session.Reconciler.LastError:0}");
                }
            }
        }

        try
        {
            Game.Multiplayer.CoopCVars.FriendlyFire.Value = true;
            Knock(5, () => Watch(4));
        }
        finally
        {
            Game.Multiplayer.CoopCVars.FriendlyFire.Value = false;
        }

        Watch(60);
        double knocked = Vector3d.Distance(start, partner.Player.Body.Position);
        Assert.True(knocked > 2048, $"the rounds knocked the partner only {knocked} units on the host");
        Assert.False(partner.IsDead);

        // The joiner's player went as far, and was not put back for it tick after tick.
        long corrections = session.Reconciler.Corrections - correctionsBefore;
        double apart = Vector3d.Distance(own.Body.Position, partner.Player.Body.Position);
        Assert.True(apart < 256, $"the joiner's player stands {apart} units from where the host has it after a {knocked:0}-unit knock");
        Assert.True(corrections <= 1, $"the joiner's player was put back {corrections} times for five knocks: {string.Join(" ", errors)}");
    }

    [RequiresGameDataFact]
    public void The_hosts_pause_menu_does_not_stop_the_game_for_everyone_and_holds_only_the_hosts_own_player()
    {
        // Live: Escape over a multiplayer game paused it as it pauses a mission alone -- a listen server stopped ticking, the
        // match stood still for every player, and a joined game fell behind its server.
        using Igi1CoopPair pair = Joined();
        Game.Igi1.Player.PlayerCamera host = pair.Host!.World.Player!;
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        pair.RunUntil(() => false, 30);

        // The host's menu up (as Escape brings it up), the host still holding forward.
        object flow = pair.HostGame.GetType().GetField("_menuFlow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(pair.HostGame)!;
        System.Reflection.PropertyInfo pause = flow.GetType().GetProperty("PauseMenu")!;
        pause.SetValue(flow, true);
        Igi1CoopPair.Hold(pair.HostInput, KeyCode.W, true);
        Vector3d hostFrom = host.Body.Position;
        Vector3d partnerFrom = partner.Player.Body.Position;

        // The joiner walks on, and the server runs it.
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        pair.RunUntil(() => false, 60);
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);
        Igi1CoopPair.Hold(pair.HostInput, KeyCode.W, false);
        pause.SetValue(flow, false);

        double walked = Vector3d.Distance(partnerFrom, partner.Player.Body.Position);
        double hostMoved = Vector3d.Distance(hostFrom, host.Body.Position);
        Assert.True(walked > 2048, $"the joiner walked only {walked:0} units on the server while the host's menu was up");
        Assert.True(hostMoved < 256, $"the host's own player moved {hostMoved:0} units under its menu");
    }

    [RequiresGameDataFact]
    public void The_server_holds_the_weapon_the_joiner_chose_even_when_their_carried_lists_differ()
    {
        // Live S&D: the server's copy of the bomb carrier held the knife while the carrier's own game held the bomb, so it
        // could not plant -- and could, with the knife out. Switches crossed as steps from what was in hand, and the two copies'
        // carried lists had come apart (the bomb is given on the server a moment before the client hears of it).
        using Igi1CoopPair pair = Joined();
        Game.Igi1.Player.PlayerCamera own = pair.Joiner!.World.Player!;
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        pair.RunUntil(() => false, 30);
        Game.Player.PlayerWeapons mine = own.Weapons!;
        Game.Player.PlayerWeapons theirs = partner.Player.Weapons!;

        // The server's copy carries a weapon the joiner's game does not.
        Igi1BombWeapon.Keep(partner.Player, true);
        Assert.False(mine.HasWeapon(Igi1BombWeapon.Id));

        // The joiner changes weapon on its own game by a way the server never hears as a key.
        int target = mine.Carried
            .Select(static c => c.Weapon.WeaponId)
            .First(id => id != mine.InHand?.Weapon.WeaponId && !mine.IsBinoculars(id) && theirs.HasWeapon(id));
        mine.RequestWeapon(target);
        pair.RunUntil(() => false, 60);
        Assert.Equal(target, mine.InHand?.Weapon.WeaponId);
        Assert.Equal(target, theirs.InHand?.Weapon.WeaponId);

        // The server's copy starts a switch of its own the joiner never made: it is dropped, the joiner's weapon kept.
        theirs.RequestWeapon(Igi1BombWeapon.Id);
        pair.RunUntil(() => false, 60);
        Assert.Equal(target, theirs.InHand?.Weapon.WeaponId);
        Assert.False(Igi1BombWeapon.Holds(partner.Player), "the server's copy took the bomb out the joiner never chose");
    }

    [RequiresGameDataFact]
    public void A_player_put_back_at_a_spawn_while_climbing_lets_go_of_the_ladder_and_stays_put()
    {
        // Live S&D: a round ended with a player on a ladder, and the next round's respawn left it on the ladder -- the climb
        // carries the body each tick, so it pulled the player straight back from the spawn.
        using Igi1CoopPair pair = Joined();
        ServerSession server = pair.Session.Session!;

        // The first round's start marks the level, as a match's does; the next round's start goes back to it.
        ((Game.Multiplayer.Modes.IModeHost)server).ResetLevel();
        Game.Igi1.Player.PlayerCamera own = pair.Joiner!.World.Player!;
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        object controller = typeof(Game.Igi1.Player.PlayerCamera).GetField("_ladder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(own)!;
        var ladders = (IReadOnlyList<LadderPlacement>)controller.GetType().GetField("_ladders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(controller)!;
        LadderPlacement ladder = ladders.OrderBy(l => l.Top.Z - l.Bottom.Z).Last();
        foreach (Game.Igi1.Player.PlayerCamera body in new[] { own, partner.Player })
        {
            body.Body.Position = ladder.BottomMount - new Vector3d(0, 0, LadderPlacement.BottomVerticalOffsetUnits);
            body.Body.Yaw = ladder.PlayerYaw;
        }

        pair.RunUntil(() => false, 10);
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.E, true);
        bool mounted = pair.RunUntil(() => own.IsOnLadder && partner.Player.IsOnLadder, 30) >= 0;
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.E, false);
        Assert.True(mounted, $"the joiner never got on: own {own.IsOnLadder}, on the host {partner.Player.IsOnLadder}");
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        pair.RunUntil(() => false, 90);
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);
        Assert.True(partner.Player.IsOnLadder, "the joiner came off the ladder before the respawn");

        // The server puts it back, as a new round does: the level reset, then everyone at a spawn.
        ((Game.Multiplayer.Modes.IModeHost)server).ResetLevel();
        ((Game.Multiplayer.Modes.IModeHost)server).Respawn(Assert.Single(server.Clients));
        Vector3d placed = partner.Player.Body.Position;
        Assert.False(partner.Player.IsOnLadder, "the respawn left the player on the ladder");

        // And it stays there, on the host and on its own game.
        List<string> trace = [];
        for (int frame = 0; frame < 60; frame++)
        {
            Assert.True(pair.Step());
            if (frame % 6 == 0)
            {
                Vector3d at = partner.Player.Body.Position - placed;
                trace.Add($"{frame}: d({at.X:0},{at.Y:0},{at.Z:0}) host-ladder {partner.Player.IsOnLadder} own-ladder {own.IsOnLadder} grounded {partner.Player.Body.IsGrounded}");
            }

            // Its own game lets go once the reset reaches it, not after the ladder patience.
            if (frame == 20)
            {
                Assert.False(own.IsOnLadder, $"the joiner's own game still has it climbing 20 frames on: {string.Join(" | ", trace)}");
            }
        }

        Vector3d now = partner.Player.Body.Position;
        double drift = Math.Sqrt(((now.X - placed.X) * (now.X - placed.X)) + ((now.Y - placed.Y) * (now.Y - placed.Y)));
        Assert.True(drift < 4096, $"the player was carried {drift:0} units from where the respawn put it: {string.Join(" | ", trace)}");
        Assert.False(own.IsOnLadder, "the joiner's own game still has it on the ladder");
        Assert.True(Vector3d.Distance(own.Body.Position, partner.Player.Body.Position) < 512, "the joiner's own game has it elsewhere");
    }

    [RequiresGameDataFact]
    public void A_joiner_climbing_and_sliding_down_a_ladder_lands_where_the_host_has_it()
    {
        using Igi1CoopPair pair = Joined();
        Game.Igi1.Player.PlayerCamera own = pair.Joiner!.World.Player!;
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        ClientSession session = pair.Client.Session!;
        object controller = typeof(Game.Igi1.Player.PlayerCamera).GetField("_ladder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(own)!;
        var ladders = (IReadOnlyList<LadderPlacement>)controller.GetType().GetField("_ladders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(controller)!;
        LadderPlacement ladder = ladders.OrderBy(l => l.Top.Z - l.Bottom.Z).Last();

        // At its foot facing it, on both games, and on with the use key.
        foreach (Game.Igi1.Player.PlayerCamera body in new[] { own, partner.Player })
        {
            body.Body.Position = ladder.BottomMount - new Vector3d(0, 0, LadderPlacement.BottomVerticalOffsetUnits);
            body.Body.Yaw = ladder.PlayerYaw;
        }

        pair.RunUntil(() => false, 10);
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.E, true);
        bool mounted = pair.RunUntil(() => own.IsOnLadder && partner.Player.IsOnLadder, 30) >= 0;
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.E, false);
        Assert.True(mounted, $"the joiner never got on: own {own.IsOnLadder}, on the host {partner.Player.IsOnLadder}");

        // Up a good way: the joiner's climb is the one its commands run on the host, rung for rung, so it is seldom put back.
        long climbStart = session.Reconciler.Corrections;
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.W, true);
        pair.RunUntil(() => false, 240);
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.W, false);
        pair.RunUntil(() => false, 30);
        double high = own.Body.Position.Z - ladder.Bottom.Z;
        Assert.True(high > 8192, $"the joiner climbed only {high} units");
        Assert.True(
            Math.Abs(own.Body.Position.Z - partner.Player.Body.Position.Z) < 256,
            $"the joiner climbed to {high:0} and the host has it at {partner.Player.Body.Position.Z - ladder.Bottom.Z:0}");
        long climbCorrections = session.Reconciler.Corrections - climbStart;
        Assert.True(climbCorrections <= 3, $"the joiner was put back {climbCorrections} times on the climb");

        // Then slide to the ground.
        long correctionsBefore = session.Reconciler.Corrections;
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.E, true);
        List<string> phases = [];
        string last = string.Empty;
        bool off = pair.RunUntil(
            () =>
            {
                string now = $"{own.LadderPhase}/{partner.Player.LadderPhase}";
                if (now != last)
                {
                    phases.Add($"f{phases.Count}:{now} z{own.Body.Position.Z - ladder.Bottom.Z:0}/{partner.Player.Body.Position.Z - ladder.Bottom.Z:0}");
                    last = now;
                }

                return !own.IsOnLadder && !partner.Player.IsOnLadder;
            },
            600) >= 0;
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.E, false);
        Assert.True(off, $"still on the ladder after sliding: own {own.LadderPhase}, on the host {partner.Player.LadderPhase}; climbed {high}; {string.Join(" | ", phases)}");

        // Standing at the foot on both, and staying there: no fall, no putting back.
        List<string> afterwards = [];
        for (int frame = 0; frame < 90; frame++)
        {
            Assert.True(pair.Step());
            afterwards.Add($"{own.Body.Position.Z - partner.Player.Body.Position.Z:0}");
        }

        long corrections = session.Reconciler.Corrections - correctionsBefore;
        double apart = Vector3d.Distance(own.Body.Position, partner.Player.Body.Position);
        Assert.True(
            apart < 256 && corrections <= 3,
            $"after a slide from {high:0} units up: {apart:0} apart, {corrections} corrections, own grounded {own.Body.IsGrounded} z {own.Body.Position.Z:0}, "
            + $"host's grounded {partner.Player.Body.IsGrounded} z {partner.Player.Body.Position.Z:0}, foot {ladder.Bottom.Z:0}; dz {string.Join(" ", afterwards.Where((_, i) => i % 6 == 0))}");
    }

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
    public void A_joiner_getting_on_a_ladder_at_its_top_climbs_down_where_the_host_has_it()
    {
        using Igi1CoopPair pair = Joined();
        Game.Igi1.Player.PlayerCamera own = pair.Joiner!.World.Player!;
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        object controller = typeof(Game.Igi1.Player.PlayerCamera).GetField("_ladder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(own)!;
        var ladders = (IReadOnlyList<LadderPlacement>)controller.GetType().GetField("_ladders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(controller)!;
        LadderPlacement ladder = ladders.OrderBy(l => l.Top.Z - l.Bottom.Z).Last();

        // On the roof behind the ladder's head, facing it, on both games.
        Vector3f normal = ladder.Orientation.Column2;
        Vector3d back = new Vector3d(-normal.X, -normal.Y, 0);
        back /= back.Length;
        foreach (Game.Igi1.Player.PlayerCamera body in new[] { own, partner.Player })
        {
            body.Body.Position = ladder.ActivationTop + (back * 3000.0) + new Vector3d(0, 0, 2048);
            body.Body.Yaw = (float)Math.Atan2(-back.X, -back.Y);
        }

        pair.RunUntil(() => false, 30);
        string near = $"own eye {own.Body.Eye} host eye {partner.Player.Body.Eye} top {ladder.ActivationTop}; own yaw {own.Body.Yaw} host yaw {partner.Player.Body.Yaw}";
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.E, true);
        List<string> phases = [];
        string last = string.Empty;
        pair.RunUntil(
            () =>
            {
                string now = $"{own.LadderPhase}/{partner.Player.LadderPhase}";
                if (now != last)
                {
                    phases.Add($"f{phases.Count}:{now} z{own.Body.Position.Z - ladder.Bottom.Z:0}/{partner.Player.Body.Position.Z - ladder.Bottom.Z:0}");
                    last = now;
                }

                return false;
            },
            20);
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.E, false);
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.S, true);
        pair.RunUntil(
            () =>
            {
                string now = $"{own.LadderPhase}/{partner.Player.LadderPhase}";
                if (now != last)
                {
                    phases.Add($"f{phases.Count}:{now} z{own.Body.Position.Z - ladder.Bottom.Z:0}/{partner.Player.Body.Position.Z - ladder.Bottom.Z:0}");
                    last = now;
                }

                return false;
            },
            150);
        Igi1CoopPair.Hold(pair.JoinerInput, KeyCode.S, false);

        Assert.True(own.IsOnLadder && partner.Player.IsOnLadder, $"{near}; {string.Join(" | ", phases)}");
        Assert.True(
            Math.Abs(own.Body.Position.Z - partner.Player.Body.Position.Z) < 256,
            $"own z{own.Body.Position.Z - ladder.Bottom.Z:0} host z{partner.Player.Body.Position.Z - ladder.Bottom.Z:0}; {string.Join(" | ", phases)}");
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

    private static Igi1CoopPair Joined(params string[] hostArgs) => Joined(1, hostArgs);

    private static Igi1CoopPair Joined(int mission, params string[] hostArgs)
    {
        Igi1CoopPair pair = new(mission: mission, hostArgs: hostArgs);
        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");
        return pair;
    }

    private static int Count(Igi1Gameplay game, string variable) =>
        game.World.Soldiers!.ScriptVariables!.TryRead(variable, out QvmValue value) ? value.ToInt32() : -1;

    private static Vector3d? At(GameTask task) => task.TryGetScriptValue("Position", out object? p) && p is Vector3d v ? v : null;

    private static string Text(GameTask task, string name) => task.TryGetScriptValue(name, out object? v) ? v?.ToString() ?? string.Empty : string.Empty;

    private static string Describe(GameTask task) =>
        $"{task.Id}: Position={(task.TryGetScriptValue("Position", out object? p) ? $"{p?.GetType().Name}:{p}" : "missing")} Criteria={(task.TryGetScriptValue("Criteria", out object? c) ? $"{c?.GetType().Name}:{c}" : "missing")}";
}
