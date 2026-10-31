using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Rendering.Headless;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Serializes renderer settings against the other tests in this assembly.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorldCullingSettingsFixture
{
    public const string Name = "World culling settings";
}

/// <summary>Protects interior geometry in both titles when Ultra enables world backface culling: only coincident skins are culled.</summary>
[Collection(WorldCullingSettingsFixture.Name)]
public sealed class RetailWorldInteriorCullingTests
{
    [RequiresTitleDataFact("igi2")]
    public void Mission_two_cave_remains_two_sided_at_the_reported_camera()
    {
        (Igi2DrawableSceneResult built, ResourceRegistry resources) = Igi2LevelScene.Build(
            Igi2Level.Find("location1", "level2"));
        WorldScene scene = built.Scene;
        RecordingGraphicsDevice device = new();
        using WorldRenderer renderer = new(device, resources, AssetFormatProfile.Igi2);
        for (int i = 0; i < scene.Placements.Count; i++)
        {
            WorldPlacement placement = scene.Placements[i];
            if (placement.TaskName == "Building" && placement.Model.Vertices.Length > 0 &&
                MissingNormals(placement))
            {
                renderer.MakeResident(scene, i);
            }
        }

        HashSet<uint> caveMeshes = device.Meshes.Keys.ToHashSet();
        Assert.NotEmpty(caveMeshes);
        Camera3D camera = new(
            WorldScene.ToMetres(new Vector3d(241637, 1545436, 894629)),
            Camera3D.LookRotation(45.4f * MathF.PI / 180f, -1.2f * MathF.PI / 180f));
        Matrix4f projection = Matrix4f.CreatePerspective(MathF.PI / 2f, 16f / 9f, 0.05f, 1000f);
        int saved = GeometryCVars.WorldCull.Value;
        try
        {
            GeometryCVars.WorldCull.Value = 1;
            renderer.Draw(scene, camera, projection);
            RecordingGraphicsDevice.MeshDrawCall[] caveDraws = device.MeshDraws
                .Where(draw => caveMeshes.Contains(draw.Mesh.Id)).ToArray();
            Assert.NotEmpty(caveDraws);
            AssertTwoSidedButItsSkins(device, caveDraws);

            device.MeshDraws.Clear();
            GeometryCVars.WorldCull.Value = 0;
            renderer.Draw(scene, camera, projection);
            Assert.Equal(caveDraws.Sum(draw => draw.IndexCount), device.MeshDraws
                .Where(draw => caveMeshes.Contains(draw.Mesh.Id)).Sum(draw => draw.IndexCount));
        }
        finally
        {
            GeometryCVars.WorldCull.Value = saved;
        }
    }

    [RequiresGameDataFact]
    public void Final_igi1_mission_keeps_sectors_two_sided_and_still_culls_eligible_props()
    {
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(files);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(files, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        LoadedMission mission = new MissionLoader(missions, files, resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(14);
        WorldScene scene = WorldScene.Build(mission, root, magic);
        WorldPlacement sector = scene.Placements.Where(p => p.Model.IsSector)
            .MaxBy(p => p.Model.Vertices.Length)!;
        Assert.False(MissingNormals(sector));
        WorldPlacement prop = scene.Placements.First(p => !p.Model.IsSector &&
            p.Model.Vertices.Length > 0 && !MissingNormals(p) && MefMeshBuilder.Build(p.Model) is not null);
        int saved = GeometryCVars.WorldCull.Value;
        try
        {
            GeometryCVars.WorldCull.Value = 1;
            AssertCull(sector, CullMode.None);
            AssertCull(prop, CullMode.CounterClockwise);
            GeometryCVars.WorldCull.Value = 0;
            AssertCull(prop, CullMode.None);
        }
        finally
        {
            GeometryCVars.WorldCull.Value = saved;
        }

        void AssertCull(WorldPlacement placement, CullMode expected)
        {
            WorldScene subject = WorldScene.FromPlacements(mission,
                [placement with { Position = Vector3d.Zero, Orientation = Matrix3f.Identity }]);
            MeshGeometry geometry = Assert.IsType<MeshGeometry>(MefMeshBuilder.Build(placement.Model, 1f / 4096f));
            Vector3f centre = geometry.Bounds.Centre;
            Camera3D camera = new(new Vector3d(centre.X, centre.Y - geometry.Bounds.Radius * 2 - 1, centre.Z),
                Camera3D.LookRotation(0, 0));
            RecordingGraphicsDevice device = new();
            using WorldRenderer renderer = new(device, resources, AssetFormatProfile.Igi1);
            renderer.Draw(subject, camera, Matrix4f.CreatePerspective(MathF.PI / 2, 16f / 9f, 0.05f, 5000f));
            Assert.NotEmpty(device.MeshDraws);
            if (expected == CullMode.None)
            {
                AssertTwoSidedButItsSkins(device, device.MeshDraws);
            }
            else
            {
                Assert.All(device.MeshDraws, draw => Assert.Equal(expected, draw.CullMode));
            }
        }
    }

    // Drawn two-sided, a mesh still draws its coincident skins culled -- the two faces of a sheet with no thickness, which
    // would otherwise fight over each pixel (CoincidentSkins). So: some of it is drawn two-sided, and every triangle drawn
    // culled is one face of such a pair, never a wall the viewer stands inside.
    private static void AssertTwoSidedButItsSkins(RecordingGraphicsDevice device, IEnumerable<RecordingGraphicsDevice.MeshDrawCall> draws)
    {
        RecordingGraphicsDevice.MeshDrawCall[] all = [.. draws];
        Assert.Contains(all, draw => draw.CullMode == CullMode.None);
        float tolerance = (float)(0.5 / WorldScene.UnitsPerMetre);
        Dictionary<uint, bool[]> skins = [];
        foreach (RecordingGraphicsDevice.MeshDrawCall draw in all.Where(draw => draw.CullMode != CullMode.None))
        {
            Assert.Equal(CullMode.CounterClockwise, draw.CullMode);
            if (!skins.TryGetValue(draw.Mesh.Id, out bool[]? skin))
            {
                (Vertex3D[] vertices, ushort[] indices) = device.Meshes[draw.Mesh.Id];
                skins[draw.Mesh.Id] = skin = CoincidentSkins.Find(vertices, indices, tolerance);
            }

            for (int t = draw.IndexOffset / 3; t < (draw.IndexOffset + draw.IndexCount) / 3; t++)
            {
                Assert.True(skin[t], $"Mesh {draw.Mesh.Id} culls triangle {t}, which is no coincident skin.");
            }
        }
    }

    private static bool MissingNormals(WorldPlacement placement)
    {
        foreach (Formats.World.MefVertex vertex in placement.Model.Vertices)
        {
            if (vertex.Normal.LengthSquared < 0.000001f)
            {
                return true;
            }
        }

        return false;
    }
}
