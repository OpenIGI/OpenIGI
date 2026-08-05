using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Checks objective/LevelFlow extraction against all fourteen retail mission scripts.</summary>
public sealed class MissionObjectiveRuntimeTests
{
    [RequiresGameDataFact]
    public void EveryRetailMissionPublishesObjectivesAndLevelFlow()
    {
        IReadOnlyList<string> scripts = GameData.EnumerateFiles("objects.qvm");
        Assert.Equal(14, scripts.Count);

        foreach (string script in scripts)
        {
            GameLoop loop = new();
            LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
            ResourceRegistry resources = new(new VirtualFileSystem());
            const string resourceName = "LOCAL:level/objects.qvm";
            resources.Register(resourceName, File.ReadAllBytes(script));
            LevelLoader loader = new(root, loop.TaskCommands, loop.Natives, resources);
            loader.LoadScript(resourceName);

            MissionObjectiveRuntime runtime = new(root, loop.Variables);
            Assert.True(runtime.HasLevelFlow, $"{script} has no LevelFlow task.");
            Assert.True(runtime.DefinitionCount > 0, $"{script} has no DefineComputerObjective task.");
            Assert.True(runtime.StatusMessageCount > 0, $"{script} has no StatusMessage task.");
            Assert.InRange(runtime.Objectives.Count, 0, MissionObjectiveRuntime.ObjectiveSlotCount);

            if (script.EndsWith(@"level1\objects.qvm", StringComparison.OrdinalIgnoreCase))
            {
                DeclaredTask gateArea = Assert.IsType<DeclaredTask>(root.Tree.FromId(200));
                Assert.True(gateArea.TryGetValue("Position", out object? rawPosition));
                runtime.Tick(Assert.IsType<OpenIGI.Core.Numerics.Vector3d>(rawPosition));

                Assert.True(loop.Variables.TryRead("AreaActivate_200.nActive", out var active));
                Assert.Equal(1, active.ToInt32());
                Assert.True(loop.Variables.TryRead("EditVariable_199.nValue", out var edit));
                Assert.Equal(1, edit.ToInt32());
            }
        }
    }
}
