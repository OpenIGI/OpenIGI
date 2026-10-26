using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// A level wire is crossed hand over hand (<c>0x411AE0</c> into state 42, <c>0x40DDA0</c>), not slid: the player hangs
/// 2150.4 under the cable, and the drag clip moves only from a key press to its next step event.
/// </summary>
public sealed class PlayerCameraWireTests
{
    [RequiresGameDataFact]
    public void ALevelWireIsCrossedHandOverHandAStepAtATime()
    {
        (WorldInteractionRegistry interactions, WorldInteraction wire) = FindLevelWire();

        PlayerCamera camera = new();
        camera.UseInteractions(interactions);
        Vector3d segment = wire.End - wire.Anchor;
        Vector3d nearAnchor = wire.Anchor + (segment * 0.3);
        camera.Body.Position = new Vector3d(nearAnchor.X, nearAnchor.Y, nearAnchor.Z - 4096.0 - camera.Body.EyeHeight);
        camera.Body.Yaw = (float)Math.Atan2(segment.X, segment.Y);
        camera.Body.Pitch = 0.6f;

        InputState input = new();
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.E, down: true);
        camera.Tick(input, terrain: null);

        Assert.True(camera.IsInWorldInteraction);
        Assert.True(camera.IsOnLevelWire);

        // Hung 2150.4 under the cable, along the cable's own up.
        Vector3d hung = wire.WireBodyPoint(nearAnchor, WorldInteraction.WireDragDrop);
        Assert.Equal(hung.X, camera.Body.Position.X, 1);
        Assert.Equal(hung.Y, camera.Body.Position.Y, 1);
        Assert.Equal(hung.Z, camera.Body.Position.Z, 1);

        // Mounted on the anchor's half, it faces down the cable toward the far end.
        Vector3d axis = segment.Normalized();
        Assert.Equal(Math.Atan2(axis.X, axis.Y), camera.Body.Yaw, 3);

        // Standing still until a key; Forward starts the drag clip with the first drag sound.
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.E, down: false);
        camera.Tick(input, terrain: null);
        Assert.Equal(0, camera.WireDragDirection);
        Assert.Null(camera.TakeWireDragSound());

        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.W, down: true);
        camera.Tick(input, terrain: null);
        Assert.Equal(1, camera.WireDragDirection);
        Assert.Equal("wire_drag_1", camera.TakeWireDragSound());

        // The clip's root step moves the body; a new step event stops it on the next tick.
        Vector3d before = camera.Body.Position;
        Vector3d step = axis * 300.0;
        camera.ApplyInteractionRootMotion(step, clipEnded: false, stepEventTime: 1600f);
        Assert.Equal(before + step, camera.Body.Position);

        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.W, down: false);
        camera.Tick(input, terrain: null);
        Assert.Equal(0, camera.WireDragDirection);

        // Back plays it the other way, with the other sound.
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.S, down: true);
        camera.Tick(input, terrain: null);
        Assert.Equal(-1, camera.WireDragDirection);
        Assert.Equal("wire_drag_2", camera.TakeWireDragSound());
    }

    private static (WorldInteractionRegistry Interactions, WorldInteraction Wire) FindLevelWire()
    {
        for (int number = 1; number <= 14; number++)
        {
            (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = LoadMission(number);
            WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
                root, mission, DoorRegistry.Build(root), variables);
            foreach (WorldInteraction wire in interactions.Items)
            {
                if (wire.Type == InteractionActivateType.Wire
                    && wire.End != wire.Anchor
                    && Math.Abs((wire.End - wire.Anchor).Normalized().Z) <= Math.Sin(0.034906585))
                {
                    return (interactions, wire);
                }
            }
        }

        throw new InvalidOperationException("No mission has a level wire.");
    }

    private static (LevelTask, LoadedMission, ScriptVarRegistry) LoadMission(int number)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);

        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions,
            fileSystem,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(number);
        return (root, mission, loop.Variables);
    }
}
