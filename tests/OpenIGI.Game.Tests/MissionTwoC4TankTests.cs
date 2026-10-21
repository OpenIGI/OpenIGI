using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>Regression coverage for the three C4 actions on Mission 2's tanks.</summary>
public sealed class MissionTwoC4TankTests
{
    [RequiresGameDataFact]
    public void EveryMissionTwoTankOffersC4FromItsAuthoredUsePose()
    {
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = Load(2);
        new ConditionalContainerGate(variables).Prepare(root);
        _ = new MissionObjectiveRuntime(root, variables);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, DoorRegistry.Build(root), variables);

        Assert.True(variables.TryWrite("EditVariable_270.nValue", QvmValue.FromInt32(1)));
        interactions.Tick();

        WorldInteraction[] tanks = interactions.Items
            .Where(static item => item.Type == InteractionActivateType.Generic && item.Task.Id is 282 or 284 or 286)
            .OrderBy(static item => item.Task.Id)
            .ToArray();
        Assert.Equal([282, 284, 286], tanks.Select(static item => item.Task.Id));
        Assert.All(tanks, static item => Assert.True(item.IsAvailable));

        foreach (WorldInteraction tank in tanks)
        {
            Vector3d eye = tank.BodyPose(tank.Anchor).Position;
            Vector3d toTank = tank.Anchor - eye;
            float yaw = (float)Math.Atan2(toTank.X, toTank.Y);

            Assert.Same(tank, interactions.Resolve(eye, yaw));
        }
    }

    private static (LevelTask Root, LoadedMission Mission, ScriptVarRegistry Variables) Load(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader loader = new(root, loop.TaskCommands, loop.Natives, resources, null);
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, loader, null, null).Load(missionId);
        return (root, mission, loop.Variables);
    }
}
