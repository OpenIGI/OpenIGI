using OpenIGI.Core.Graphics;
using OpenIGI.Formats;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>
/// Exercises the retail Location 0 / Level 4 extraction cutscene at the renderer boundary.  The
/// branch's heavy outro scene must not submit a helicopter's rotor blur while it sits behind the
/// camera, but every retail sample must remain visible once that helicopter enters the cutscene view.
/// </summary>
public sealed class MissionFourOutroRotorCullingTests
{
    [RequiresGameDataFact]
    public void RetailMissionFourOutroCullsOffCameraRotorBlurAndKeepsItsInViewSamples()
    {
        using Scenario scenario = CreateForcedOutro();
        WorldPlacement[] rotors = scenario.Scene.Placements
            .Where(placement => placement.IsRotor && ConditionalContainerGate.IsTaskActive(placement.Owner))
            .ToArray();

        Assert.NotEmpty(rotors);
        RotorDrawState[] states = rotors.Select(placement =>
        {
            Assert.True(scenario.Cutscenes.TryGetRotorDrawState(placement, out RotorDrawState state));
            return state;
        }).ToArray();

        foreach ((WorldPlacement _, int index) in scenario.Scene.Placements
            .Select((placement, index) => (placement, index))
            .Where(entry => rotors.Contains(entry.placement)))
        {
            Assert.True(scenario.Renderer.MakeResident(scenario.Scene, index));
        }

        HashSet<uint> rotorMeshIds = scenario.Device.Meshes.Keys.ToHashSet();
        Assert.NotEmpty(rotorMeshIds);
        int expectedInViewDraws = rotors.Zip(states).Sum(pair =>
        {
            MeshGeometry geometry = Assert.IsType<MeshGeometry>(
                MefMeshBuilder.Build(pair.First.Model, (float)(1d / WorldScene.UnitsPerMetre)));
            return geometry.Subsets.Sum(DrawCalls) * pair.Second.SampleCount;
        });
        Assert.True(expectedInViewDraws > 0);

        Vector3d helicopterCentre = Average(states.Select(state => state.Position));
        Camera3D behindHelicopter = new(
            helicopterCentre - new Vector3d(0d, 60d, 0d),
            Camera3D.LookRotation(MathF.PI, 0f));
        Camera3D facingHelicopter = new(
            helicopterCentre - new Vector3d(0d, 60d, 0d),
            Camera3D.LookRotation(0f, 0f));
        Matrix4f projection = Matrix4f.CreatePerspective(MathF.PI / 3f, 16f / 9f, 0.01f, 200f);

        scenario.Renderer.Draw(scenario.Scene, behindHelicopter, projection, rotorsOnly: true);
        int offCameraDraws = RotorDrawCount();
        Assert.Equal(0, offCameraDraws);

        scenario.Device.MeshDraws.Clear();
        scenario.Renderer.Draw(scenario.Scene, facingHelicopter, projection, rotorsOnly: true);
        int inViewDraws = RotorDrawCount();
        Assert.Equal(expectedInViewDraws, inViewDraws);

        int RotorDrawCount() => scenario.Device.MeshDraws.Count(draw => rotorMeshIds.Contains(draw.Mesh.Id));
    }

    private static Scenario CreateForcedOutro()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
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
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(4);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        VehiclePhysicsRegistry physics = new();
        physics.Load(resources);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime cutscenes = new(root, loop.Variables, vehiclePhysics: physics);
        MissionObjectiveRuntime objectives = new(root, loop.Variables, native: cutscenes.TryInvokeNative);

        // Retail objects.qvm gates ConditionalContainer_3902 ("Outro cutscene") on AreaActivate_156.
        // Put the player at the authored activation volume, then run the normal level/cutscene phases.
        DeclaredTask area156 = Assert.IsType<DeclaredTask>(root.Tree.FromId(156));
        Assert.True(area156.TryGetValue("Position", out object? extractionRaw));
        Vector3d extraction = Assert.IsType<Vector3d>(extractionRaw);
        objectives.TickLevelState(extraction);
        cutscenes.Tick();
        objectives.TickAfterLevelState();
        cutscenes.FinishExpressionTick();

        Assert.Equal(2222, cutscenes.ActiveScene?.Id);
        Assert.True(Assert.IsType<ConditionalContainerTask>(root.Tree.FromId(3902)).IsRun);

        RecordingGraphicsDevice device = new();
        WorldRenderer renderer = new(device, resources, AssetFormatProfile.Igi1) { Cutscenes = cutscenes };
        return new Scenario(scene, cutscenes, device, renderer);
    }

    private static Vector3d Average(IEnumerable<Vector3d> positions)
    {
        Vector3d sum = default;
        int count = 0;
        foreach (Vector3d position in positions)
        {
            sum += position;
            count++;
        }

        Assert.True(count > 0);
        return sum / count;
    }

    private sealed class Scenario(
        WorldScene scene,
        CutsceneRuntime cutscenes,
        RecordingGraphicsDevice device,
        WorldRenderer renderer) : IDisposable
    {
        public WorldScene Scene { get; } = scene;

        public CutsceneRuntime Cutscenes { get; } = cutscenes;

        public RecordingGraphicsDevice Device { get; } = device;

        public WorldRenderer Renderer { get; } = renderer;

        public void Dispose() => Renderer.Dispose();
    }

    // The draws a subset takes with culling off: its two-sided part, and its coincident skins culled after it (CoincidentSkins).
    private static int DrawCalls(MeshSubset subset) =>
        (subset.IndexCount - subset.SkinIndexCount > 0 ? 1 : 0) + (subset.SkinIndexCount > 0 ? 1 : 0);
}
