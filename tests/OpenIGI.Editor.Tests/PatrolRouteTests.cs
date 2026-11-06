using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>Patrol routes and soldier scripts, against what the shipped levels hold.</summary>
public sealed class PatrolRouteTests
{
    /// <summary>The guard and patroller scripts compile to level 1's own, byte for byte.</summary>
    [RequiresGameDataFact]
    public void Soldier_scripts_are_level_ones_own()
    {
        string ai = Path.Combine(GameData.RequireRoot(), "missions", "location0", "level1", "ai");
        Assert.Equal(File.ReadAllBytes(Path.Combine(ai, "2226.qvm")), SoldierScripts.Compile(SoldierScripts.Guard));
        Assert.Equal(File.ReadAllBytes(Path.Combine(ai, "2203.qvm")), SoldierScripts.Compile(SoldierScripts.Patroller(2403)));
    }

    /// <summary>Every patrol of level 1 reads as a route and writes back as the same commands.</summary>
    [RequiresGameDataFact]
    public void Level_one_patrols_round_trip()
    {
        MissionDocument level = MissionDocument.Read(File.ReadAllBytes(Path.Combine(GameData.RequireRoot(), "missions", "location0", "level1", "objects.qvm")));
        int patrols = 0;
        foreach (MissionTask path in level.EnumerateTasks().Where(static t => t.TypeName == PatrolRoute.PathType))
        {
            PatrolRoute route = PatrolRoute.Read(path);
            if (route.Waypoints.Count == 0)
            {
                continue; // a patrol of animations only, which stands where it is
            }

            Assert.Equal(Codes(path.Children), Codes(route.ToCommands(level)));
            patrols++;
        }

        Assert.True(patrols > 5, $"only {patrols} patrols");
    }

    /// <summary>A new route is a plain list; a stop ends it; a loop mark goes where it is put; a wait is in ticks.</summary>
    [Fact]
    public void A_route_writes_as_the_original_lays_one_out()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript(
            """Task_DeclareParameters("PatrolPathCommand", "Command", "Int32", "Command Parameter", "Int32");""",
            QvmProgram.IgiVersion));
        PatrolRoute route = new([
            new PatrolWaypoint(4, Run: false, [new PatrolAction(PatrolPause.Wait, 10), new PatrolAction(PatrolPause.LookAt, 9)]),
            new PatrolWaypoint(7, Run: true, []),
        ]);

        Assert.Equal([(2, 4), (1, 300), (5, 9), (3, 7)], Codes(route.ToCommands(document)));
        Assert.Equal([(2, 4), (1, 300), (5, 9), (3, 7), (7, 0)], Codes(route.WithStop(true).ToCommands(document)));
        Assert.Equal([(1, 60), (6, -1), (2, 4), (1, 300), (5, 9), (3, 7)], Codes((route with { Start = [new PatrolAction(PatrolPause.Wait, 2), new PatrolAction(PatrolPause.LoopMark, -1)] }).ToCommands(document)));
        Assert.Equal("Delays the script execution for 300 ticks", route.ToCommands(document)[1].Label);
        Assert.True(route.WithStop(true).Stops);
        Assert.False(route.WithStop(true).WithStop(false).Stops);
    }

    private static (int, int)[] Codes(IEnumerable<MissionTask> commands) => [.. commands
        .Where(static c => c.TypeName == PatrolRoute.CommandType)
        .Select(static c => (
            c.FindParameter("Command")!.TryGetNumbers(out double[] k) ? (int)k[0] : -9,
            c.FindParameter("Command Parameter")!.TryGetNumbers(out double[] v) ? (int)v[0] : -9))];
}
