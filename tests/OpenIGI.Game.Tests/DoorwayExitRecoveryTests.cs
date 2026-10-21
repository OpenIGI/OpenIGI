using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

public sealed class DoorwayExitRecoveryTests
{
    [RequiresGameDataFact]
    public void MissionFiveDoorwayBackFaceCanBeExitedWithoutOpeningItsEntryFace()
    {
        (LevelTask root, LoadedMission mission, MagicObjectRegistry magic) = Load(5);
        DoorRegistry doors = DoorRegistry.Build(root);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);

        foreach (PlacedDoor door in doors.Doors)
        {
            door.State.CommandOpen();
            for (int tick = 0; tick < 300 && !door.State.IsFullyOpen; tick++)
            {
                door.State.Tick();
            }

            solids.SetPassable(door.Task, true);
            solids.UpdateTransform(door.Task, door.DrawPosition, door.DrawOrientation);
        }

        PlacedDoor selected = Assert.Single(doors.Doors, door => door.Task.Id == 45);
        Vector3d centre = selected.Position;
        Vector3d across = new Vector3d(
            selected.Orientation.Column1.X, selected.Orientation.Column1.Y, 0.0).Normalized();
        Vector3d through = new Vector3d(
            selected.Orientation.Column2.X, selected.Orientation.Column2.Y, 0.0).Normalized();
        Vector3d position = centre + (across * WorldScene.UnitsPerMetre) + (through * WorldScene.UnitsPerMetre);
        Vector3d target = position - (through * (0.25 * WorldScene.UnitsPerMetre));
        WorldRayCast cast = solids.PlayerCaster();

        // The actual level-five collision mesh marks this doorway-edge crossing as a back-face exit.
        // Before the fix the sweep ignored that flag, repeated the same contact eleven times, and
        // restored position on every tick: once a player reached this state no input could get out.
        Vector3d probeStart = position + new Vector3d(0.0, 0.0, HumanWallProbe.MiddleProbeStandingInUnits);
        Vector3d probeTarget = target + new Vector3d(0.0, 0.0, HumanWallProbe.MiddleProbeStandingInUnits);
        Assert.True(cast(probeStart, probeTarget, out WorldRayHit exit));
        Assert.True(exit.IsExit);

        HumanWallSweepResult leaving = HumanWallSweep.Sweep(
            position, target, grounded: true, crouching: false, cast);

        Assert.False(leaving.TouchedWall);
        Assert.Equal(target, leaving.Destination);
        Assert.Equal(1, leaving.Checks);

        // Reversing the same crossing is an entry into the solid edge. It must remain blocked; exit
        // recovery cannot turn this into a general pass-through or weaken the doorway frame.
        Assert.True(cast(probeTarget, probeStart, out WorldRayHit entry));
        Assert.False(entry.IsExit);

        HumanWallSweepResult entering = HumanWallSweep.Sweep(
            target, position, grounded: true, crouching: false, cast);

        Assert.True(entering.TouchedWall);
        Assert.NotEqual(position, entering.Destination);
    }

    private static (LevelTask Root, LoadedMission Mission, MagicObjectRegistry Magic) Load(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        LevelLoader level = new(root, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, level).Load(missionId);
        return (root, mission, magic);
    }
}
