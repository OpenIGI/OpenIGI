using OpenIGI.Core.Graphics;
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
using OpenIGI.Presentation.Rendering;
using OpenIGI.Formats;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Desktop.Tests;

/// <summary>Checks that a live explosive-object state change reaches the world renderer.</summary>
public sealed class DestructibleModelReplacementTests
{
    [RequiresGameDataFact]
    public void EveryLevelObjectsQvmExplosiveModelPairIsRegisteredForReplacement()
    {
        int total = 0;
        for (int mission = 1; mission <= 14; mission++)
        {
            using Scenario scenario = CreateMission(mission);
            total += AssertDestroyedReplacement(scenario, mission, IsObjectsQvmExplosive, "destructible task");
        }

        Assert.True(total > 0, "The retail campaign did not provide any destructible OBJECTS.QVM tasks.");
    }

    [RequiresGameDataFact]
    public void EveryLevelObjectsQvmExplodeObjectHonorsItsDestroyedModelDeclaration()
    {
        int total = 0;
        for (int mission = 1; mission <= 14; mission++)
        {
            using Scenario scenario = CreateMission(mission);
            WorldPlacement[] explodeObjects = [.. scenario.Scene.Placements.Where(IsObjectsQvmExplodeObject)];
            total += explodeObjects.Length;

            foreach (WorldPlacement placement in explodeObjects)
            {
                DeclaredTask task = Assert.IsType<DeclaredTask>(placement.Owner);
                Assert.True(task.TryGetValue("Destroyed model", out object? rawDestroyed));
                string destroyedModel = Assert.IsType<string>(rawDestroyed);

                if (destroyedModel.Length == 0)
                {
                    // Still an ExplodeObject: it blows up and raises isExploded (0x481F90), but 0x481A60
                    // has no model to swap in, so it keeps the one it has.
                    Assert.Null(placement.DestroyedReplacement);
                    DestructibleObject keeps = Assert.Single(
                        scenario.Destructibles.Objects,
                        candidate => ReferenceEquals(candidate.Owner, placement.Owner));
                    Assert.True(keeps.KeepsModelWhenDestroyed);
                }
            }
        }

        Assert.True(total > 0, "The retail campaign did not provide any ExplodeObject tasks.");
    }

    [RequiresGameDataFact]
    public void EveryLevelObjectsQvmExplodeObjectWithDestroyedModelTransitionsToItsAuthoredDestroyedModel()
    {
        int total = 0;
        for (int mission = 1; mission <= 14; mission++)
        {
            using Scenario scenario = CreateMission(mission);
            total += AssertDestroyedReplacement(scenario, mission, IsObjectsQvmExplodeObjectWithDestroyedModel, "ExplodeObject");
        }

        Assert.True(total > 0, "The retail campaign did not provide any ExplodeObject destroyed-model declarations.");
    }

    [RequiresGameDataFact]
    public void EveryLevelExplodeMagicObjectTransitionsToItsAuthoredDestroyedModelAtItsRetailThreshold()
    {
        int total = 0;
        for (int mission = 1; mission <= 14; mission++)
        {
            using Scenario scenario = CreateMission(mission);
            (WorldPlacement Placement, int Index)[] placements = [.. scenario.Scene.Placements
                .Select((placement, index) => (placement, index))
                .Where(entry => entry.placement.MagicTaskTypeName == "TASKTYPE_EXPLODEMAGICOBJ")];
            total += placements.Length;

            foreach ((WorldPlacement placement, int index) in placements)
            {
                Assert.NotEqual(string.Empty, placement.MagicDestroyedModel);
                WorldModelBinding replacement = Assert.IsType<WorldModelBinding>(placement.DestroyedReplacement);
                DestructibleObject explosive = Assert.Single(
                    scenario.Destructibles.Objects,
                    candidate => ReferenceEquals(candidate.Placement, placement));

                Assert.Equal(placement.MagicDestroyedModel, replacement.Instance.Name);
                Assert.Equal(placement.MagicDestroyedModel, explosive.DestroyedModelName);
                int hits = Math.Max(1, (int)MathF.Ceiling(explosive.ExplosionDamageThreshold));
                for (int hit = 1; hit <= hits; hit++)
                {
                    DestructibleObject? destroyed = scenario.Destructibles.TryDestroy(placement.Owner, placement);
                    if (hit < hits)
                    {
                        Assert.Null(destroyed);
                    }
                    else
                    {
                        Assert.Same(explosive, destroyed);
                    }
                }

                Assert.True(scenario.Destructibles.IsDestroyed(placement));
                Assert.True(scenario.Renderer.MakeResident(scenario.Scene, index));
            }
        }

        Assert.True(total > 0, "The retail campaign did not place any ExplodeMagicObj instances.");
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
        Destroy(scenario, terminal, placement);
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

        Destroy(scenario, target, placement);
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

    private static bool IsObjectsQvmExplodeObject(WorldPlacement placement) =>
        placement.Owner is DeclaredTask { Type.Name: "ExplodeObject" };

    private static bool IsObjectsQvmExplodeObjectWithDestroyedModel(WorldPlacement placement) =>
        IsObjectsQvmExplodeObject(placement)
        && placement.Owner is DeclaredTask task
        && task.TryGetValue("Destroyed model", out object? destroyed)
        && destroyed is string { Length: > 0 };

    private static int AssertDestroyedReplacement(
        Scenario scenario,
        int mission,
        Func<WorldPlacement, bool> predicate,
        string family)
    {
        (WorldPlacement Placement, int Index)[] placements = [.. scenario.Scene.Placements
            .Select((placement, index) => (placement, index))
            .Where(entry => predicate(entry.placement))];
        foreach (IGrouping<GameTask, (WorldPlacement Placement, int Index)> group in placements.GroupBy(entry => entry.Placement.Owner))
        {
            WorldPlacement placement = group.Last().Placement;
            DeclaredTask task = Assert.IsType<DeclaredTask>(placement.Owner);
            Assert.True(task.TryGetValue("Destroyed model", out object? rawDestroyed));
            string destroyedModel = Assert.IsType<string>(rawDestroyed);
            DestructibleObject destructible = Assert.Single(
                scenario.Destructibles.Objects,
                candidate => ReferenceEquals(candidate.Owner, placement.Owner));

            // Hit the last placement deliberately: one declared task may own Model, Model 2, and
            // further instances. All of them must share its state and renderer replacement.
            Destroy(scenario, destructible, placement);
            foreach ((WorldPlacement ownedPlacement, int ownedIndex) in group)
            {
                Assert.True(
                    ownedPlacement.DestroyedReplacement is not null,
                    $"Mission {mission}, {family} '{task.Name}': {ownedPlacement.Instance.Name} has no binding for {destroyedModel}.");
                WorldModelBinding replacement = ownedPlacement.DestroyedReplacement!;

                Assert.Equal(destroyedModel, replacement.Instance.Name);
                Assert.Equal(destroyedModel, destructible.DestroyedModelName);
                Assert.True(scenario.Destructibles.IsDestroyed(ownedPlacement));

                // MakeResident selects DisplayedPlacement, so this proves the hit state is not merely
                // registered: the renderer can bind every task-owned wreck model on the next frame.
                Assert.True(scenario.Renderer.MakeResident(scenario.Scene, ownedIndex));
            }
        }

        return placements.Length;
    }

    // A round breaks most destructibles on contact; an ExplodeObject's only arms its countdown at 0
    // (0x481E60) and the object explodes on its next update (0x481970).
    private static void Destroy(Scenario scenario, DestructibleObject target, WorldPlacement placement)
    {
        DestructibleObject? hit = scenario.Destructibles.TryDestroy(
            target.Owner, placement, damage: DestructionDose(target));
        if (target.ExplodesOnCountdown)
        {
            Assert.Null(hit);
            scenario.Destructibles.Tick();
        }
        else
        {
            Assert.Same(target, hit);
        }

        Assert.True(target.IsDestroyed);
    }

    private static float DestructionDose(DestructibleObject item) =>
        Math.Max(1f, item.ExplosionDamageThreshold);

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
        WorldScene scene = WorldScene.Build(mission, root, magic, destroyedModelArchiveName: new Igi1Quirks().DestroyedModelArchiveName);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        DestructibleObjectRegistry destructibles = DestructibleObjectRegistry.Build(
            root,
            scene.Placements,
            loop.Variables);
        RecordingGraphicsDevice device = new();
        WorldRenderer renderer = new(device, resources, AssetFormatProfile.Igi1) { Destructibles = destructibles };
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
