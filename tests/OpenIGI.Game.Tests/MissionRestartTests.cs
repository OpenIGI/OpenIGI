using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A restart builds the level again on the same loop. Mission 7's real T80 sits under
/// <c>ConditionalContainer_700</c>, <c>!Car_361.isExploded &amp;&amp; AreaActivate_175.nActive || this.isRun</c>: once it
/// holds, it holds for the rest of the run. It must start dormant on a restart exactly as on a first load, whatever
/// the run being left had counted in area 175.
/// </summary>
public sealed class MissionRestartTests
{
    private const int Mission = 7;

    [RequiresGameDataFact]
    public void RestartFromInsideTheT80AreaStartsTheRealT80Dormant()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);

        // The run being left: the player stands in "T80 run", so the area counts it and the real T80 comes up.
        LevelTask first = Load(loop, missions, fs, resources);
        MissionObjectiveRuntime objectives = new(first, loop.Variables);
        Vector3d inside = Assert.IsType<Vector3d>(
            Assert.IsType<DeclaredTask>(first.Tree.FromId(175)).TryGetValue("Position", out object? centre)
                ? centre
                : null);
        objectives.TickLevelState(inside, playerTeam: 0);
        Assert.True(loop.Variables.TryRead("AreaActivate_175.nActive", out var counted));
        Assert.Equal(1, counted.ToInt32());

        // Restart: the level goes, the same loop loads it again, and the containers are decided before anything
        // has rebound the area.
        loop.Variables.UnexportTree(first);
        loop.Tasks.Delete(first);
        LevelTask second = Load(loop, missions, fs, resources);

        Assert.False(Assert.IsType<ConditionalContainerTask>(second.Tree.FromId(700)).IsRun);
        Assert.True(Assert.IsType<ConditionalContainerTask>(second.Tree.FromId(701)).IsRun);
    }

    private static LevelTask Load(GameLoop loop, MissionRegistry missions, VirtualFileSystem fs, ResourceRegistry resources)
    {
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        _ = new MissionLoader(
            missions,
            fs,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(Mission);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        return root;
    }
}
