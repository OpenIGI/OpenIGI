using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class RetailElevatorTests
{
    [RequiresGameDataFact]
    public void AuthoredElevatorsExposeSplineFloorsAndLiveScriptVariables()
    {
        int liftCount = 0;
        int floorCount = 0;

        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, ScriptVarRegistry variables) = Load(missionId);
            ElevatorRegistry registry = ElevatorRegistry.Build(root, variables);

            liftCount += registry.Elevators.Count;
            foreach (PlacedElevator elevator in registry.Elevators)
            {
                Assert.NotEmpty(elevator.Model);
                Assert.False(elevator.State.IsMoving);
                Assert.InRange(elevator.State.CurrentFloorIndex, 0, elevator.State.FloorCount - 1);
                Assert.Contains($"{elevator.Task.Name}.vFloor", variables.GetBoundNames(elevator.Task));
                Assert.Contains($"{elevator.Task.Name}.isMoving", variables.GetBoundNames(elevator.Task));
                floorCount++;
            }
        }

        Assert.True(liftCount > 0, "no Elevator tasks were found in the retail campaign");
        Assert.Equal(liftCount, floorCount);
    }

    private static (LevelTask Root, ScriptVarRegistry Variables) Load(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader level = new(root, loop.TaskCommands, loop.Natives, resources);
        new MissionLoader(missions, fileSystem, resources, level).Load(missionId);
        return (root, loop.Variables);
    }
}
