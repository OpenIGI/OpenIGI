using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Checks that a live explosive-object state change reaches the world renderer.</summary>
public sealed class DestructibleModelReplacementTests
{
    [RequiresGameDataFact]
    public void EveryLevelObjectsQvmExplosiveModelPairIsRegisteredForReplacement()
    {
        for (int mission = 1; mission <= 14; mission++)
        {
            using Scenario scenario = CreateMission(mission);
            WorldPlacement[] explosivePlacements = [.. scenario.Scene.Placements.Where(IsObjectsQvmExplosive)];

            foreach (WorldPlacement placement in explosivePlacements)
            {
                DeclaredTask task = Assert.IsType<DeclaredTask>(placement.Owner);
                Assert.True(task.TryGetValue("Destroyed model", out object? rawDestroyed));
                string destroyedModel = Assert.IsType<string>(rawDestroyed);
                Assert.True(
                    placement.DestroyedReplacement is not null,
                    $"Mission {mission}, {task.Type.Name} '{task.Name}': {placement.Instance.Name} has no binding for {destroyedModel}.");
                WorldModelBinding replacement = placement.DestroyedReplacement!;
                DestructibleObject destructible = Assert.Single(
                    scenario.Destructibles.Objects,
                    candidate => ReferenceEquals(candidate.Placement, placement));

                Assert.Equal(destroyedModel, replacement.Instance.Name);
                Assert.Equal(destroyedModel, destructible.DestroyedModelName);
            }
        }
    }

    [RequiresGameDataFact]
    public void DestroyedLevelOneTerminalUsesItsAuthoredReplacementModel()
    {
        using Scenario scenario = CreateMission(1);
        WorldPlacement placement = scenario.Scene.Placements.First(
            candidate => candidate.Instance.Name == "252_01_1"
                && candidate.DestroyedReplacement?.Instance.Name == "203_02_1");
        DestructibleObject terminal = Assert.Single(
            scenario.Destructibles.Objects,
            candidate => ReferenceEquals(candidate.Placement, placement));

        Assert.Equal("Terminal", terminal.TypeName);
        Assert.Equal("203_02_1", terminal.DestroyedModelName);
        Assert.Same(terminal, scenario.Destructibles.TryDestroy(terminal.Owner, placement));
        Assert.True(scenario.Destructibles.IsDestroyed(placement));
    }

    [RequiresGameDataFact]
    public void DestroyedLevelOneComputerReplacesItsIntactModelOnTheNextFrame()
    {
        using Scenario scenario = CreateMission(1);
        (WorldPlacement placement, int index) = scenario.Scene.Placements
            .Select((placement, index) => (placement, index))
            .First(entry => entry.placement.Instance.Name == "203_01_1"
                && entry.placement.DestroyedReplacement?.Instance.Name == "203_02_1");
        DestructibleObject target = Assert.Single(scenario.Destructibles.Objects, item =>
            ReferenceEquals(item.Placement, placement));

        Assert.True(scenario.Renderer.MakeResident(scenario.Scene, index));

        Camera3D camera = new(
            placement.Position - new Vector3d(0d, 5d, 0d),
            Camera3D.LookRotation(0f, 0f));
        Matrix4f projection = Matrix4f.CreatePerspective(MathF.PI / 2f, 16f / 9f, 0.01f, 100f);

        // Populate the frame cache with the intact placement before its scripted destruction.
        scenario.Renderer.Draw(scenario.Scene, camera, projection);
        HashSet<uint> intactMeshes = [.. scenario.Device.Meshes.Keys];

        Assert.Same(target, scenario.Destructibles.TryDestroy(target.Owner, placement));
        scenario.Renderer.BeginFrame(tickFraction: 1f, smooth: false);
        Assert.True(scenario.Renderer.MakeResident(scenario.Scene, index));
        uint destroyedMesh = Assert.Single(scenario.Device.Meshes.Keys, id => !intactMeshes.Contains(id));

        scenario.Device.MeshDraws.Clear();
        scenario.Renderer.Draw(scenario.Scene, camera, projection);

        Assert.Contains(scenario.Device.MeshDraws, draw => draw.Mesh.Id == destroyedMesh);
    }

    private static bool IsObjectsQvmExplosive(WorldPlacement placement) =>
        placement.Owner is DeclaredTask task
        && task.TryGetValue("Destroyed model", out object? destroyed)
        && destroyed is string { Length: > 0 }
        && task.TryGetValue("Damage scale", out _);

    private static Scenario CreateMission(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions,
            fileSystem,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(missionId);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        DestructibleObjectRegistry destructibles = DestructibleObjectRegistry.Build(
            root,
            scene.Placements,
            loop.Variables);
        RecordingGraphicsDevice device = new();
        WorldRenderer renderer = new(device, resources) { Destructibles = destructibles };
        return new Scenario(scene, destructibles, device, renderer);
    }

    private sealed class Scenario(
        WorldScene scene,
        DestructibleObjectRegistry destructibles,
        RecordingGraphicsDevice device,
        WorldRenderer renderer) : IDisposable
    {
        public WorldScene Scene { get; } = scene;

        public DestructibleObjectRegistry Destructibles { get; } = destructibles;

        public RecordingGraphicsDevice Device { get; } = device;

        public WorldRenderer Renderer { get; } = renderer;

        public void Dispose() => Renderer.Dispose();
    }
}
