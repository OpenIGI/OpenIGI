using OpenIGI.Engine.Flow;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class RetailInteractionInventoryTests
{
    private static readonly HashSet<string> InteractiveTypes = new(StringComparer.Ordinal)
    {
        "Wire", "Fence", "Ladder", "Terminal", "Cabinet", "Switch", "Door",
        "GenericTBA", "PlaceExplosiveTBA", "StationaryGun",
    };

    [RequiresGameDataFact]
    public void EveryRetailDirectInteractionHasARegisteredActivateType()
    {
        Dictionary<string, int> totals = InteractiveTypes.ToDictionary(static name => name, static _ => 0);
        for (int missionId = 1; missionId <= 14; missionId++)
        {
            (LevelTask root, MagicObjectRegistry magic, LoadedMission mission, ScriptVarRegistry variables) = Load(missionId);
            Dictionary<string, List<DeclaredTask>> found = Walk(root)
                .OfType<DeclaredTask>()
                .Where(static task => InteractiveTypes.Contains(task.Type.Name))
                .GroupBy(static task => task.Type.Name, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.Ordinal);

            foreach ((string name, List<DeclaredTask> tasks) in found.OrderBy(static pair => pair.Key))
            {
                totals[name] += tasks.Count;
            }

            DoorRegistry doors = DoorRegistry.Build(root);
            WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
                root, mission, doors, variables);
            WorldScene scene = WorldScene.Build(mission, root, magic);
            Assert.Equal(found.Values.Sum(static tasks => tasks.Count), interactions.Items.Count);
            foreach (WorldInteraction door in interactions.Items.Where(
                static item => item.Type == InteractionActivateType.Door))
            {
                Assert.NotNull(door.Door);
                PlacedDoor placed = Assert.Single(
                    doors.Doors,
                    candidate => ReferenceEquals(candidate.Task, door.Task));
                Assert.Same(placed.State, door.Door);

                Vector3d normal = door.Orientation.Transposed().Transform(new Vector3d(0.0, 2048.0, 0.0));
                Vector3d standing = door.Anchor + normal;
                Assert.True(door.CanOfferFrom(standing));
                Assert.Equal(door.Anchor, door.CandidatePoint(standing));

                Vector3d outside = door.Anchor
                    + door.Orientation.Transposed().Transform(new Vector3d(2048.0, 0.0, 0.0));
                Assert.False(door.CanOfferFrom(outside));
            }
            foreach (PlacedDoor door in doors.Doors)
            {
                WorldPlacement placement = Assert.Single(
                    scene.Placements,
                    candidate => ReferenceEquals(candidate.Owner, door.Task));
                Assert.Equal(placement.Position, door.DrawPosition);
                Assert.Equal(placement.Orientation, door.DrawOrientation);

                door.State.Toggle();
                DeclaredTask doorTask = Assert.IsType<DeclaredTask>(door.Task);
                int motionTicks = Math.Max(1, (int)Math.Ceiling(ReadReal(doorTask, "Open time", 1f) * 30f));
                for (int tick = 0; tick <= motionTicks; tick++)
                {
                    door.State.Tick();
                }

                Assert.True(door.State.IsFullyOpen, $"{door.Task.Name} did not finish opening");
                bool hasMotion = ReadReal(doorTask, "Position stop X", 0f) != 0f
                    || ReadReal(doorTask, "Position stop Y", 0f) != 0f
                    || ReadReal(doorTask, "Position slider", 0f) != 0f
                    || ReadReal(doorTask, "Max angle", 0f) != 0f;
                if (hasMotion)
                {
                    Assert.True(
                        door.DrawPosition != placement.Position || door.DrawOrientation != placement.Orientation,
                        $"{door.Task.Name} became passable without changing its visible transform");
                }

                door.State.Toggle();
                for (int tick = 0; tick <= motionTicks; tick++)
                {
                    door.State.Tick();
                }

                Assert.True(door.State.IsFullyClosed, $"{door.Task.Name} did not close on a second use");
                Assert.Equal(placement.Position, door.DrawPosition);
                Assert.Equal(placement.Orientation, door.DrawOrientation);
            }
            foreach (WorldInteraction wire in interactions.Items.Where(
                static item => item.Type == InteractionActivateType.Wire))
            {
                WorldPlacement placement = Assert.Single(
                    scene.Placements,
                    candidate => ReferenceEquals(candidate.Owner, wire.Task));
                Assert.NotNull(placement.BentGeometry);
                Assert.True(WireTransform.TryCreate(wire.Task, placement.Model, out WireTransform transform));
                Assert.Equal(transform.PositionMetres, placement.Position);
                Assert.Equal(transform.Orientation, placement.Orientation);
                Assert.Equal(transform.CableStart, wire.Anchor);
                Assert.Equal(transform.CableEnd, wire.End);

                Vector3d mount = wire.WireMountPoint(wire.Anchor - ((wire.End - wire.Anchor) * 10.0));
                double expected = Math.Min(3072.0, (wire.End - wire.Anchor).Length * 0.49);
                Assert.Equal(expected, Vector3d.Distance(wire.Anchor, mount), precision: 3);

                Vector3d body = wire.WireBodyPoint(
                    wire.Anchor - ((wire.End - wire.Anchor) * 10.0));
                Vector3d attachment = mount - body;
                Assert.Equal(3686.4, attachment.Length, precision: 3);
                Assert.Equal(
                    0.0,
                    Vector3d.Dot(attachment, (wire.End - wire.Anchor).Normalized()),
                    precision: 3);
            }
        }

        // Ladder is model-attached; every direct family appears in at least one shipped mission.
        foreach (string type in InteractiveTypes.Where(static name => name != "Ladder"))
        {
            Assert.True(totals[type] > 0, $"No shipped {type} task was loaded.");
        }
    }

    private static (LevelTask Root, MagicObjectRegistry Magic, LoadedMission Mission, ScriptVarRegistry Variables) Load(int missionId)
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
        return (root, magic, mission, loop.Variables);
    }

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }

    private static float ReadReal(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) && value is float real ? real : fallback;
}
