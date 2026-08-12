using OpenIGI.Core.Numerics;
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
            (LevelTask root, ScriptVarRegistry variables, _) = Load(missionId);
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

    [RequiresGameDataFact]
    public void ElevatorOwnedInteractionAnchorsFollowTheMovingPlatform()
    {
        (LevelTask root, ScriptVarRegistry variables, LoadedMission mission) = Load(14);
        ElevatorRegistry elevators = ElevatorRegistry.Build(root, variables);
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root,
            mission,
            DoorRegistry.Build(root),
            variables,
            elevators);

        foreach (PlacedElevator elevator in elevators.Elevators)
        {
            DeclaredTask? child = Descendants(elevator.Task)
                .OfType<DeclaredTask>()
                .FirstOrDefault(static task => string.Equals(task.Type.Name, "Switch", StringComparison.Ordinal));
            WorldInteraction? interaction = child is null
                ? null
                : interactions.Items.FirstOrDefault(item => ReferenceEquals(item.Task, child));
            if (interaction is null)
            {
                continue;
            }

            DeclaredTask ownedChild = child!;
            Vector3d authoredAnchor = interaction.Anchor;
            int target = elevator.State.CurrentFloorIndex + 1 < elevator.State.FloorCount
                ? elevator.State.CurrentFloorIndex + 1
                : elevator.State.CurrentFloorIndex - 1;
            elevator.State.RequestFloor(target);
            elevator.State.Tick(canStart: true, floorUp: false, floorDown: false);

            Vector3d expectedOffset = elevators.GetCurrentDisplacement(ownedChild);
            Vector3d actualOffset = interaction.Anchor - authoredAnchor;
            Assert.InRange(Math.Abs(actualOffset.X - expectedOffset.X), 0.0, 1e-8);
            Assert.InRange(Math.Abs(actualOffset.Y - expectedOffset.Y), 0.0, 1e-8);
            Assert.InRange(Math.Abs(actualOffset.Z - expectedOffset.Z), 0.0, 1e-8);
            return;
        }

        Assert.Fail("mission 14 contains no active elevator-owned switch interaction");
    }

    private static IEnumerable<GameTask> Descendants(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            yield return child;
            foreach (GameTask descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static (LevelTask Root, ScriptVarRegistry Variables, LoadedMission Mission) Load(int missionId)
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
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, level).Load(missionId);
        return (root, loop.Variables, mission);
    }
}
