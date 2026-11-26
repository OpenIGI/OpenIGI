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

/// <summary>
/// Culling in both titles: every model is culled as the original culls it -- rooms, props, and I.G.I. 2's lightmapped geometry,
/// which stores no normals (<c>Direct3DRender_SetGenericStateLightmap</c>, CULLMODE CCW) -- and what the eye sees is the side kept.
/// </summary>
[Collection(WorldCullingSettingsFixture.Name)]
public sealed class RetailWorldInteriorCullingTests
{
    /// <summary>
    /// Mission 2's cave, which stores no normals, is culled at the reported camera inside it, and turned over to the original's
    /// front every surface the eye sees there faces it: culling erases none of the cave.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_two_cave_is_culled_and_faces_the_eye_inside_it()
    {
        (Igi2DrawableSceneResult built, ResourceRegistry resources) = Igi2LevelScene.Build(
            Igi2Level.Find("location1", "level2"));
        WorldScene scene = built.Scene;
        Vector3d eye = new(241637, 1545436, 894629);
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
            WorldScene.ToMetres(eye),
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
            Assert.All(caveDraws, static draw => Assert.Equal(CullMode.CounterClockwise, draw.CullMode));
        }
        finally
        {
            GeometryCVars.WorldCull.Value = saved;
        }

        AssertSeenFromTheFront(scene, eye, 30.0, "410_10_1");
    }

    /// <summary>
    /// Mission 3's fuel depot stairs (<c>413_02_1</c>) from the hall below, as reported: its stringer and the skirting boards stand
    /// flush against walls that store no normals, and drawn two-sided the walls' backs fought them for every pixel. Turned over to
    /// the original's front, what the eye sees there faces it, so the culled walls' backs are gone.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_three_stairs_are_seen_from_the_front()
    {
        WorldScene scene = Igi2LevelScene.Build(Igi2Level.Find("location1", "level3")).Item1.Scene;
        AssertSeenFromTheFront(scene, new Vector3d(-4705434, 2286177, 40715358), 12.0, "413_02_1");
    }

    // Rays from the eye in every direction: the nearest face each meets among the models that store no normals, wound as the mesh
    // builder winds it, must face the eye (bar a sliver of stray edges), and the named model must be among what is seen.
    private static void AssertSeenFromTheFront(WorldScene scene, Vector3d eye, double reachMetres, string model)
    {
        double reach = reachMetres * WorldScene.UnitsPerMetre;
        List<(Vector3d A, Vector3d B, Vector3d C, string Name)> faces = [];
        Dictionary<Formats.World.MefModel, MeshGeometry?> built = [];
        foreach (WorldPlacement placement in scene.Placements)
        {
            if (placement.Model.Vertices.Length == 0 || !MissingNormals(placement))
            {
                continue;
            }

            if (!built.TryGetValue(placement.Model, out MeshGeometry? geometry))
            {
                built[placement.Model] = geometry = MefMeshBuilder.Build(placement.Model, 1f);
            }

            if (geometry is null)
            {
                continue;
            }

            Matrix3f toWorld = placement.Orientation.Transposed();
            Vector3d at = WorldScene.ToUnits(placement.Position);
            for (int i = 0; i + 2 < geometry.Indices.Length; i += 3)
            {
                Vector3d a = World(geometry.Vertices[geometry.Indices[i]].Position);
                Vector3d b = World(geometry.Vertices[geometry.Indices[i + 1]].Position);
                Vector3d c = World(geometry.Vertices[geometry.Indices[i + 2]].Position);
                if ((a - eye).Length < reach || (b - eye).Length < reach || (c - eye).Length < reach)
                {
                    faces.Add((a, b, c, placement.Instance.Name));
                }
            }

            Vector3d World(Vector3f local)
            {
                Vector3f turned = toWorld.Transform(local);
                return new Vector3d(turned.X + at.X, turned.Y + at.Y, turned.Z + at.Z);
            }
        }

        const int Rays = 2000;
        double golden = Math.PI * (3 - Math.Sqrt(5));
        int seen = 0, front = 0, ofModel = 0;
        for (int i = 0; i < Rays; i++)
        {
            double z = 1 - (2.0 * (i + 0.5) / Rays);
            double r = Math.Sqrt(1 - (z * z));
            Vector3d direction = new(Math.Cos(golden * i) * r, Math.Sin(golden * i) * r, z);
            double nearest = double.MaxValue;
            int hit = -1;
            for (int f = 0; f < faces.Count; f++)
            {
                if (Meets(eye, direction, faces[f], out double distance) && distance < nearest)
                {
                    nearest = distance;
                    hit = f;
                }
            }

            if (hit < 0)
            {
                continue;
            }

            (Vector3d a, Vector3d b, Vector3d c, string name) = faces[hit];
            seen++;
            ofModel += name == model ? 1 : 0;
            front += Dot(Cross(b - a, c - a), eye - a) > 0 ? 1 : 0;
        }

        Assert.True(ofModel > 50, $"{model} is barely in view: {ofModel} of {seen} rays meet it.");
        Assert.True(front >= seen * 0.98, $"Of {seen} faces seen, {seen - front} are backs the original culls.");
    }

    // Moller-Trumbore, either face.
    private static bool Meets(Vector3d origin, Vector3d direction, (Vector3d A, Vector3d B, Vector3d C, string Name) face, out double distance)
    {
        distance = 0;
        Vector3d e1 = face.B - face.A, e2 = face.C - face.A;
        Vector3d p = Cross(direction, e2);
        double det = Dot(e1, p);
        if (Math.Abs(det) < 1e-9)
        {
            return false;
        }

        Vector3d s = origin - face.A;
        double u = Dot(s, p) / det;
        Vector3d q = Cross(s, e1);
        double v = Dot(direction, q) / det;
        distance = Dot(e2, q) / det;
        return u >= 0 && v >= 0 && u + v <= 1 && distance > 1;
    }

    private static Vector3d Cross(Vector3d a, Vector3d b) =>
        new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    private static double Dot(Vector3d a, Vector3d b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    [RequiresGameDataFact]
    public void Final_igi1_mission_culls_sectors_and_props_alike_as_the_original_does()
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
            AssertCull(sector, CullMode.CounterClockwise);
            AssertCull(prop, CullMode.CounterClockwise);
            GeometryCVars.WorldCull.Value = 0;
            AssertCull(sector, CullMode.None);
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
