using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
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
/// A level wire is crossed hand over hand (<c>0x411AE0</c> into state 42, <c>0x40DDA0</c>), not slid: the player's origin
/// hangs 2150.4 under the cable, and the looped drag clip moves only from a key press to its next step event.
/// </summary>
public sealed class PlayerCameraWireTests
{
    [RequiresGameDataFact]
    public void ALevelWireIsCrossedHandOverHandAStepAtATime()
    {
        (WorldInteractionRegistry interactions, WorldInteraction wire) = FindLevelWire();

        PlayerCamera camera = Mount(interactions, wire, out Vector3d origin);

        Assert.True(camera.IsInWorldInteraction);
        Assert.True(camera.IsOnLevelWire);
        Assert.Equal(42, camera.InteractionLocomotionState);
        Assert.Equal(HumanLocomotionStates.For(42).AnimationId, camera.InteractionAnimation);

        // State 42 plays animation 172 looped (mode 1 in the state table); played once, it ran off its end after
        // two steps and never crossed another step event, so the ride stopped answering the keys.
        Assert.True(camera.InteractionAnimationLoops);

        // The origin hangs 2150.4 under the cable's own axis, along the cable's up.
        Vector3d hung = wire.WireBodyPoint(origin, WorldInteraction.WireDragDrop);
        Assert.Equal(hung.X, camera.Body.Position.X, 1);
        Assert.Equal(hung.Y, camera.Body.Position.Y, 1);
        Assert.Equal(hung.Z, camera.Body.Position.Z, 1);

        // Mounted short of the model's origin, it faces down the cable toward the far end.
        Vector3d axis = wire.WireAxis;
        Assert.Equal(Math.Atan2(axis.X, axis.Y), camera.Body.Yaw, 3);

        // Standing still until a key; Forward starts the drag clip with the first drag sound.
        InputState input = new();
        input.BeginFrame();
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

    [RequiresGameDataFact]
    public void HoldingAKeyKeepsCrossingTheWireStepAfterStep()
    {
        (WorldInteractionRegistry interactions, WorldInteraction wire) = FindLevelWire();
        PlayerCamera camera = Mount(interactions, wire, out _);
        SimulatedLadderBody body = new(LoadBodySet());
        InteractionMotion motion = new();
        int steps = 0;
        void Sound(string name, Vector3d at) => steps++;

        // The mission ticks the clip after the player, the mount's tick included.
        motion.Tick(camera, body, Sound);
        InputState input = new();
        double start = wire.WireCoordinate(camera.Body.Position);

        // Forward, held: every step event stops the clip and the held key starts the next step at once. Animation 172
        // has two steps a cycle, 18 ticks each, so 150 ticks is eight -- or the far end of the ride.
        for (int tick = 0; tick < 150 && camera.IsInWorldInteraction; tick++)
        {
            input.BeginFrame();
            input.Keyboard.SetKey(KeyCode.W, down: true);
            camera.Tick(input, terrain: null);
            if (camera.IsInWorldInteraction)
            {
                motion.Tick(camera, body, Sound);
            }
        }

        Assert.True(steps >= 4, $"only {steps} drag steps were taken");
        double forward = wire.WireCoordinate(camera.Body.Position) - start;
        Assert.True(forward > 3 * 1900.0, $"the ride moved {forward} units along the cable");

        // Back, held, as far again.
        if (camera.IsInWorldInteraction)
        {
            int forwardSteps = steps;
            double turnedAt = wire.WireCoordinate(camera.Body.Position);
            for (int tick = 0; tick < 80 && camera.IsInWorldInteraction; tick++)
            {
                input.BeginFrame();
                input.Keyboard.SetKey(KeyCode.W, down: false);
                input.Keyboard.SetKey(KeyCode.S, down: true);
                camera.Tick(input, terrain: null);
                if (camera.IsInWorldInteraction)
                {
                    motion.Tick(camera, body, Sound);
                }
            }

            Assert.True(steps - forwardSteps >= 3, $"only {steps - forwardSteps} steps back were taken");
            Assert.True(wire.WireCoordinate(camera.Body.Position) < turnedAt - (2 * 1900.0));
        }
    }

    [RequiresGameDataFact]
    public void LettingGoOfAWireFallsFromItsFeetUnderTheHungOrigin()
    {
        (WorldInteractionRegistry interactions, WorldInteraction wire) = FindLevelWire();
        PlayerCamera camera = Mount(interactions, wire, out _);
        Vector3d hung = camera.Body.Position;

        InputState input = new();
        input.BeginFrame();
        camera.Tick(input, terrain: null);
        Assert.True(camera.IsInWorldInteraction);

        // A fresh press of Use lets go (0x411AE0); the press does not take hold again, and state 29 drops the
        // origin's standing height first, so the feet start under where the origin hung.
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.E, down: true);
        camera.Tick(input, terrain: null);
        Assert.False(camera.IsInWorldInteraction);
        Assert.True(camera.Body.Position.Z <= hung.Z - PlayerBody.StandingOriginHeight + 1.0);
    }

    // Puts the eye a metre under the cable, a third of the way along the ride, looking up at it, and presses Use.
    private static PlayerCamera Mount(WorldInteractionRegistry interactions, WorldInteraction wire, out Vector3d origin)
    {
        PlayerCamera camera = new();
        camera.UseInteractions(interactions);
        Vector3d segment = wire.End - wire.Anchor;
        Vector3d nearAnchor = wire.Anchor + (segment * 0.3);
        camera.Body.Position = new Vector3d(nearAnchor.X, nearAnchor.Y, nearAnchor.Z - 4096.0 - camera.Body.EyeHeight);
        camera.Body.Yaw = (float)Math.Atan2(segment.X, segment.Y);
        camera.Body.Pitch = 0.6f;
        origin = camera.Body.Origin;

        InputState input = new();
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.E, down: true);
        camera.Tick(input, terrain: null);
        return camera;
    }

    // The longest level wire in the campaign, so a ride has room for several steps.
    private static (WorldInteractionRegistry Interactions, WorldInteraction Wire) FindLevelWire()
    {
        (WorldInteractionRegistry, WorldInteraction)? best = null;
        double bestLength = 0.0;
        for (int number = 1; number <= 14; number++)
        {
            (LevelTask root, LoadedMission mission, ScriptVarRegistry variables) = LoadMission(number);
            WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
                root, mission, DoorRegistry.Build(root), variables);
            foreach (WorldInteraction wire in interactions.Items)
            {
                double length = (wire.End - wire.Anchor).Length;
                if (wire.Type == InteractionActivateType.Wire
                    && length > bestLength
                    && Math.Abs((wire.End - wire.Anchor).Normalized().Z) <= Math.Sin(0.034906585))
                {
                    best = (interactions, wire);
                    bestLength = length;
                }
            }
        }

        return best ?? throw new InvalidOperationException("No mission has a level wire.");
    }

    private static BoneAnimationSet LoadBodySet() => BoneAnimationSet.Read(File.ReadAllBytes(
        Path.Combine(GameData.RequireRoot(), "common", "anims", "000.iff")));

    private static (LevelTask, LoadedMission, ScriptVarRegistry) LoadMission(int number)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        OpenIGI.Game.Flow.Igi1LevelTaskTypes.Register(loop.Types);
        OpenIGI.Game.Igi1.Igi1TaskTypes.Register(loop.Types);

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
