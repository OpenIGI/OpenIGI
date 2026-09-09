using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Checks the renderer's culling boundary around retail helicopter rotor blur meshes.</summary>
public sealed class WorldRendererRotorCullingTests
{
    [RequiresGameDataFact]
    public void RetailRotorMeshesCullBehindTheCameraAndKeepEveryBladeSampleInView()
    {
        using Scenario scenario = CreateMissionOneHelicopter();
        WorldPlacement[] rotors = scenario.Scene.Placements
            .Where(placement => placement.Owner.Id == 1502 && placement.IsRotor)
            .ToArray();
        Assert.Equal(4, rotors.Length);

        RotorDrawState[] states = rotors.Select(placement =>
        {
            Assert.True(scenario.Cutscenes.TryGetRotorDrawState(placement, out RotorDrawState state));
            return state;
        }).ToArray();

        // Make these actual retail models resident before the whole scene draws, so their handles mark
        // only rotor submissions in the recording device, even when a nearby object enters the frustum.
        foreach ((WorldPlacement rotor, int index) in scenario.Scene.Placements
            .Select((placement, index) => (placement, index))
            .Where(entry => rotors.Contains(entry.placement)))
        {
            Assert.True(scenario.Renderer.MakeResident(scenario.Scene, index));
        }

        HashSet<uint> rotorMeshes = scenario.Device.Meshes.Keys.ToHashSet();
        Assert.NotEmpty(rotorMeshes);
        int expectedSamples = rotors.Zip(states).Sum(pair =>
        {
            MeshGeometry geometry = Assert.IsType<MeshGeometry>(
                MefMeshBuilder.Build(pair.First.Model, (float)(1.0 / WorldScene.UnitsPerMetre)));
            return geometry.Subsets.Count(subset => subset.IndexCount > 0) * pair.Second.SampleCount;
        });
        Assert.True(expectedSamples > 0);

        Vector3d centre = Average(states.Select(state => state.Position));
        Vector3d eye = centre - new Vector3d(0, 60, 0);
        Matrix4f projection = Matrix4f.CreatePerspective(MathF.PI / 3f, 16f / 9f, 0.01f, 200f);

        // The authoritatively recognised rotor state must not bypass the same frustum rule as rigid
        // geometry: an off-camera helicopter otherwise submits every blur sample each frame.
        scenario.Renderer.Draw(scenario.Scene, new Camera3D(eye, Camera3D.LookRotation(MathF.PI, 0f)), projection, rotorsOnly: true);
        Assert.Equal(0, RotorDraws());

        scenario.Device.MeshDraws.Clear();
        scenario.Renderer.Draw(scenario.Scene, new Camera3D(eye, Camera3D.LookRotation(0f, 0f)), projection, rotorsOnly: true);
        Assert.Equal(expectedSamples, RotorDraws());

        int RotorDraws() => scenario.Device.MeshDraws.Count(draw => rotorMeshes.Contains(draw.Mesh.Id));
    }

    [RequiresGameDataFact]
    public void RetailRotorAtFrustumEdgeDrawsWhenOnlyALaterBlurPhaseIsVisible()
    {
        using Scenario scenario = CreateMissionOneHelicopter();
        RotorFrustumEdge edge = FindRotorFrustumEdge(scenario);

        // A culler which treats phase zero as the entire blur disc rejects this placement.  At least
        // one later retail RotorPrimitive::Draw sample is nevertheless inside the view, so rejecting
        // it would make the rotor visibly blink at this camera edge.
        Assert.False(FrustumCulling.SphereVisible(
            edge.Projection * edge.Camera.CreateModelView(edge.State.Position, edge.State.GetOrientation(0)),
            edge.Geometry.Bounds));
        Assert.True(FrustumCulling.SphereVisible(
            edge.Projection * edge.Camera.CreateModelView(
                edge.State.Position,
                edge.State.GetOrientation(edge.VisibleSample)),
            edge.Geometry.Bounds));

        // The cull sphere must cover the whole phase orbit, rather than merely phase zero.  This is
        // deliberately checked independently of the draw count below: a relaxed no-cull shortcut
        // would render here but would lose the off-camera protection covered by the sibling test.
        BoundingSphere fullOrbit = new(Vector3f.Zero, edge.Geometry.Bounds.Centre.Length + edge.Geometry.Bounds.Radius);
        Assert.True(FrustumCulling.SphereVisible(
            edge.Projection * edge.Camera.CreateModelView(edge.State.Position),
            fullOrbit));

        int placementIndex = scenario.Scene.Placements
            .Select((placement, index) => (placement, index))
            .Single(entry => entry.placement == edge.Placement)
            .index;
        Assert.True(scenario.Renderer.MakeResident(scenario.Scene, placementIndex));
        uint rotorMesh = Assert.Single(scenario.Device.Meshes.Keys);
        Matrix4f phaseZeroModelView = edge.Camera.CreateModelView(
            edge.State.Position,
            edge.State.GetOrientation(0));
        int expectedPhaseZeroDraws = CountRotorPhaseZeroDraws(
            scenario,
            edge.Placement.Instance.Name,
            edge.Camera,
            phaseZeroModelView);
        Assert.True(expectedPhaseZeroDraws >= edge.Geometry.Subsets.Count(subset => subset.IndexCount > 0));

        scenario.Renderer.Draw(scenario.Scene, edge.Camera, edge.Projection, rotorsOnly: true);

        int actualPhaseZeroDraws = scenario.Device.MeshDraws.Count(draw =>
            draw.Mesh.Id == rotorMesh && draw.ModelView == phaseZeroModelView);
        Assert.Equal(expectedPhaseZeroDraws, actualPhaseZeroDraws);
    }

    [RequiresGameDataTheory]
    [InlineData(1, 1502)]
    [InlineData(3, 3339)]
    public void HelicopterRotorsBlendInForwardPassAndMoveBetweenTicks(int missionId, int ownerId)
    {
        using Scenario scenario = CreateMissionOneHelicopter(missionId);
        WorldPlacement[] rotors = scenario.Scene.Placements.Where(p => p.Owner.Id == ownerId && p.IsRotor).ToArray();
        Assert.NotEmpty(rotors);
        foreach (WorldPlacement rotor in rotors)
        {
            int index = scenario.Scene.Placements.ToList().IndexOf(rotor);
            Assert.True(scenario.Renderer.MakeResident(scenario.Scene, index));
        }
        HashSet<uint> meshes = scenario.Device.Meshes.Keys.ToHashSet();
        Assert.True(scenario.Cutscenes.TryGetRotorDrawState(rotors[0], out RotorDrawState state));
        Camera3D camera = new(state.Position - new Vector3d(0, 30, -8), Camera3D.LookRotation(0, -0.25f));
        Matrix4f projection = Matrix4f.CreatePerspective(MathF.PI / 3, 16f / 9, 0.01f, 200f);
        scenario.Renderer.Draw(scenario.Scene, camera, projection);
        Assert.DoesNotContain(scenario.Device.MeshDraws, d => meshes.Contains(d.Mesh.Id));
        scenario.Device.MeshDraws.Clear();
        scenario.Renderer.Draw(scenario.Scene, camera, projection, rotorsOnly: true);
        var before = scenario.Device.MeshDraws.Where(d => meshes.Contains(d.Mesh.Id)).ToArray();
        Assert.NotEmpty(before);
        Assert.All(before, d => Assert.Equal(BlendMode.AlphaBlend, d.Blend));
        Assert.All(before, d => Assert.Equal(DepthState.TestOnly, d.DepthState));
        scenario.Cutscenes.Tick();

        // The renderer resolves every live transform once a frame and the later passes reuse it, so a
        // tick only reaches the draw across a frame boundary -- which is what BeginFrame marks.
        scenario.Renderer.BeginFrame(1f, smooth: false);
        scenario.Device.MeshDraws.Clear();
        scenario.Renderer.Draw(scenario.Scene, camera, projection, rotorsOnly: true);
        var after = scenario.Device.MeshDraws.Where(d => meshes.Contains(d.Mesh.Id)).ToArray();
        Assert.Equal(before.Length, after.Length);
        Assert.Contains(before.Zip(after), pair => pair.First.ModelView != pair.Second.ModelView);
    }

    private static Scenario CreateMissionOneHelicopter(int missionId = 1)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
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
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(missionId);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        VehiclePhysicsRegistry physics = new();
        physics.Load(resources);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        CutsceneRuntime cutscenes = new(root, loop.Variables, vehiclePhysics: physics);
        for (int tick = 0; tick < 360; tick++)
        {
            cutscenes.Tick();
        }

        RecordingGraphicsDevice device = new();
        WorldRenderer renderer = new(device, resources) { Cutscenes = cutscenes };
        return new Scenario(scene, cutscenes, device, renderer);
    }

    private static Vector3d Average(IEnumerable<Vector3d> positions)
    {
        Vector3d total = default;
        int count = 0;
        foreach (Vector3d position in positions)
        {
            total += position;
            count++;
        }

        Assert.True(count > 0);
        return total / count;
    }

    private static RotorFrustumEdge FindRotorFrustumEdge(Scenario scenario)
    {
        Matrix4f projection = Matrix4f.CreatePerspective(MathF.PI / 3f, 16f / 9f, 0.01f, 200f);
        foreach (WorldPlacement placement in scenario.Scene.Placements.Where(placement =>
            placement.Owner.Id == 1502 && placement.IsRotor))
        {
            Assert.True(scenario.Cutscenes.TryGetRotorDrawState(placement, out RotorDrawState state));
            MeshGeometry geometry = Assert.IsType<MeshGeometry>(
                MefMeshBuilder.Build(placement.Model, (float)(1.0 / WorldScene.UnitsPerMetre)));
            if (state.SampleCount < 2 || geometry.Bounds.Centre == default)
            {
                continue;
            }

            // The camera looks along +Y.  Moving it only vertically sweeps the rotor across the top
            // frustum plane while preserving its phase orbit, yielding a repeatable one-sample-visible
            // edge case from the retail mission data instead of inventing a proxy mesh.
            for (int distance = 16; distance <= 64; distance += 8)
            {
                for (float verticalOffset = -24f; verticalOffset <= 24f; verticalOffset += 0.25f)
                {
                    Camera3D camera = new(
                        state.Position + new Vector3d(0d, -distance, verticalOffset),
                        Camera3D.LookRotation(0f, 0f));
                    if (FrustumCulling.SphereVisible(
                        projection * camera.CreateModelView(state.Position, state.GetOrientation(0)),
                        geometry.Bounds))
                    {
                        continue;
                    }

                    int visibleSample = Enumerable.Range(1, state.SampleCount - 1).FirstOrDefault(sample =>
                        FrustumCulling.SphereVisible(
                            projection * camera.CreateModelView(state.Position, state.GetOrientation(sample)),
                            geometry.Bounds));
                    if (visibleSample == 0)
                    {
                        continue;
                    }

                    BoundingSphere fullOrbit = new(
                        Vector3f.Zero,
                        geometry.Bounds.Centre.Length + geometry.Bounds.Radius);
                    if (FrustumCulling.SphereVisible(
                        projection * camera.CreateModelView(state.Position),
                        fullOrbit))
                    {
                        return new(placement, state, geometry, camera, projection, visibleSample);
                    }
                }
            }
        }

        throw new Xunit.Sdk.XunitException("Mission 1 did not provide a rotor phase that crosses the frustum edge.");
    }

    private static int CountRotorPhaseZeroDraws(
        Scenario scenario,
        string instanceName,
        Camera3D camera,
        Matrix4f phaseZeroModelView)
    {
        int draws = 0;
        foreach (WorldPlacement placement in scenario.Scene.Placements.Where(placement =>
            placement.IsRotor && placement.Instance.Name == instanceName))
        {
            if (!scenario.Cutscenes.TryGetRotorDrawState(placement, out RotorDrawState state)
                || camera.CreateModelView(state.Position, state.GetOrientation(0)) != phaseZeroModelView)
            {
                continue;
            }

            MeshGeometry geometry = Assert.IsType<MeshGeometry>(
                MefMeshBuilder.Build(placement.Model, (float)(1.0 / WorldScene.UnitsPerMetre)));
            draws += geometry.Subsets.Count(subset => subset.IndexCount > 0);
        }

        return draws;
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

    private sealed record RotorFrustumEdge(
        WorldPlacement Placement,
        RotorDrawState State,
        MeshGeometry Geometry,
        Camera3D Camera,
        Matrix4f Projection,
        int VisibleSample);
}
