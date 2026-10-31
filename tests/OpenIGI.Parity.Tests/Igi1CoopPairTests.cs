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

    private static Igi1CoopPair Joined()
    {
        Igi1CoopPair pair = new();
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
