using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Igi1.World;
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

        // Each soldier the joiner has is the host's puppet, standing where the host's stands.
        foreach (Game.Igi1.Player.SoldierView mine in pair.Joiner!.World.Soldiers!.SoldierViews.Where(static v => !v.IsDead))
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

    private static Igi1CoopPair Joined(params string[] hostArgs)
    {
        Igi1CoopPair pair = new(hostArgs: hostArgs);
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
