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
using OpenIGI.Presentation.Lighting;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Rendering.Headless;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The air between the camera and the ground, against the retail data and the retail rules — see
/// <see cref="SceneFog"/> for where in <c>IGI.EXE</c> each of them was read.
/// </summary>
/// <remarks>
/// The fog is the difference between the original's picture and a postcard. Every shipping level asks
/// for enough of it that half a surface is gone by four hundred metres, which is why retail can afford a
/// very large render distance and still look like a mission rather than a flight simulator.
/// </remarks>
public sealed class RetailSkyFogTests
{
    /// <summary>The air every mission asks for, and what it means in metres.</summary>
    /// <remarks>
    /// <para>
    /// Read off the <c>GlobalLight</c> keyframe, which is where the original reads it. The first attempt
    /// took <c>FlatSky</c>'s <c>Fog Amount</c> instead and got one figure for the whole campaign — the
    /// cloud plane's fade, which every level authors at about 0.9. The real bands are nothing like that
    /// uniform: mission 3 loses half a surface in 71 m and mission 11 still has half of it at 2.9 km,
    /// and that difference is the whole reason one mission's range is meant to be seen and another's
    /// horizon is not.
    /// </para>
    /// <para>
    /// The figures below are the levels' own, so a change to the chain of constants moves them all and
    /// this fails; they are not a re-derivation of the formula from itself.
    /// </para>
    /// </remarks>
    [RequiresGameDataTheory]
    [InlineData(1, 1.89f, 179f)]
    [InlineData(2, 0.18f, 1880f)]
    [InlineData(3, 4.79f, 71f)]
    [InlineData(4, 1.2f, 282f)]
    [InlineData(5, 0.12f, 2820f)]
    [InlineData(6, 0.18f, 1880f)]
    [InlineData(7, 0.79f, 428f)]
    [InlineData(8, 0.4f, 846f)]
    [InlineData(9, 3f, 113f)]
    [InlineData(10, 0.18f, 1880f)]
    [InlineData(11, 0.115f, 2943f)]
    [InlineData(12, 0.4f, 846f)]
    [InlineData(13, 0.6f, 564f)]
    [InlineData(14, 9.5f, 36f)]
    public void EveryMissionsAirIsItsGlobalLightKeyframes(int mission, float density, float halfAt)
    {
        using Level level = Level.Load(mission);
        GlobalLightSettings light = level.GlobalLight;

        // The keyframe a mission opens on is its own, not the green one the goggles switch to.
        Assert.Equal(12f, light.Time);

        Assert.Equal(SceneFog.DensityFor(density), light.TerrainFog.DensityPerMetre, 7);
        Assert.Equal(halfAt, HalfVisibleAt(light.TerrainFog), 0);

        // Every shipping level authors the terrain band and the first object band alike. They are still
        // read and published separately, because the original does.
        Assert.Equal(light.TerrainFog, light.ObjectFog);

        // And a flat model light that is a dim tint, never white: models in this game are the texture
        // multiplied by it, and one that reached white would be the port's own default showing through.
        Assert.InRange(light.ObjectAmbient.X, 0.0f, 0.6f);
        Assert.InRange(light.ObjectAmbient.Y, 0.05f, 0.6f);
        Assert.InRange(light.ObjectAmbient.Z, 0.05f, 0.6f);
    }

    /// <summary>The goggles' keyframe is the green one, and it carries no fog.</summary>
    /// <remarks>
    /// Every level declares exactly two keyframes: its own air at time 12, and a green one at time 100
    /// with the fog density at zero. That is the night-vision state, not a time of day — which is why
    /// the keyframe a mission opens on is chosen by taking the lowest time rather than by interpolating
    /// towards a clock.
    /// </remarks>
    [RequiresGameDataFact]
    public void TheSecondKeyframeIsTheNightVisionState()
    {
        using Level level = Level.Load(1);

        List<DeclaredTask> keyframes = [];
        Collect(level.Root);

        Assert.Equal(2, keyframes.Count);
        Assert.Contains(keyframes, k => Time(k) == 12f);

        DeclaredTask goggles = Assert.Single(keyframes, k => Time(k) == 100f);
        GlobalLightSettings green = GlobalLightSettings.From(goggles, 100f);

        Assert.False(green.TerrainFog.IsEnabled);
        Assert.False(green.ObjectFog.IsEnabled);
        Assert.True(green.ObjectAmbient.Y > green.ObjectAmbient.X, "the goggle state is not green");
        Assert.True(green.ObjectAmbient.Y > green.ObjectAmbient.Z, "the goggle state is not green");

        void Collect(GameTask task)
        {
            if (task is DeclaredTask declared
                && string.Equals(declared.Type.Name, GlobalLightSettings.KeyframeType, StringComparison.Ordinal))
            {
                keyframes.Add(declared);
            }

            foreach (GameTask child in task.Children)
            {
                Collect(child);
            }
        }

        static float Time(DeclaredTask keyframe) =>
            keyframe.TryGetValue("Time", out object? value) && value is float time ? time : float.NaN;
    }

    /// <summary>The sky is drawn unfogged and untinted, and gives back what it found.</summary>
    /// <remarks>
    /// The renderer deliberately isolates the dome and cloud layers from world fog. This protects the
    /// authored sky gradient and is regression-tested as state restoration; mapping retail's opaque
    /// Direct3D state calls at <c>0x52D060</c> to individual named states remains future RE work.
    /// </remarks>
    [RequiresGameDataFact]
    public void TheSkyDrawsUnfoggedAndUntintedAndLeavesTheFrameAsItFoundIt()
    {
        using Level level = Level.Load(1);
        SceneFog fog = level.GlobalLight.TerrainFog;

        level.Device.SetFog(fog);
        level.Device.SetModelAmbient(level.GlobalLight.ObjectAmbient);
        level.Device.MeshDraws.Clear();
        level.Sky.Draw(
            new Camera3D(new Vector3d(0, 0, 0), Camera3D.LookRotation(0f, 0f)),
            Matrix4f.CreatePerspective(1f, 16f / 9f, ViewPlanes.NearMetres, ViewPlanes.FarMetres),
            0f);

        Assert.NotEmpty(level.Device.MeshDraws);
        Assert.All(level.Device.MeshDraws, draw => Assert.False(draw.Fog.IsEnabled));
        Assert.Equal(fog, level.Device.Fog);

        // Nor is the dome tinted by the flat model light: its gradient is its own vertices.
        Assert.All(level.Device.MeshDraws, draw => Assert.Equal(Vector3f.One, draw.ModelAmbient));
        Assert.Equal(level.GlobalLight.ObjectAmbient, level.Device.ModelAmbient);
    }

    /// <summary>The dome uses the paired-listing-confirmed 0.8 base-radius multiplier.</summary>
    /// <remarks>
    /// At <c>0x52CC40</c>, both Ghidra and r2 show every dome position multiplied by the literal
    /// <c>0.8</c>. The base-radius global's renderer meaning is not assumed by this test.
    /// </remarks>
    [Fact]
    public void TheDomeUsesTheRetailBaseRadiusMultiplier()
    {
        Assert.Equal(100_000f, ViewPlanes.FarMetres);
        Assert.Equal(409_600_000f, ViewPlanes.FarMetres * 4096f);
        Assert.Equal(409.6f, ViewPlanes.NearMetres * 4096f, 3);
    }

    /// <summary>Every vertex of the cloud strip is in front of the camera, in every level.</summary>
    /// <remarks>
    /// The fade is by view depth (<c>sub_51E1F0</c>), which is zero abreast of the camera and negative
    /// behind it — and the test the original writes treats anything nearer than the fade's start as
    /// fully opaque. So a vertex that is not in front is drawn at full opacity however far out it is.
    /// The screen-space strip cannot produce one; the camera-centred disc that came before it filled a
    /// whole band of the sky with them, and printed a dark line along the skyline of every mission.
    /// </remarks>
    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(13)]
    public void EveryCloudVertexIsInFrontOfTheCamera(int mission)
    {
        using Level level = Level.Load(mission);

        // Level, up, down and across, because the strip is laid out from where the camera looks.
        foreach ((float Yaw, float Pitch) pose in new[] { (0f, 0f), (2.4f, 0.4f), (-1.1f, -0.3f), (0.7f, 1.2f) })
        {
            Camera3D camera = new(default, Camera3D.LookRotation(pose.Yaw, pose.Pitch));
            Vector3f forward = camera.Orientation.Row3;

            foreach (Vertex3D[] strip in DrawAndCollectClouds(level, camera))
            {
                Assert.All(strip, vertex => Assert.True(
                    Vector3f.Dot(forward, vertex.Position) > 0f,
                    $"mission {mission} at yaw {pose.Yaw} pitch {pose.Pitch} put a cloud vertex "
                        + $"{Vector3f.Dot(forward, vertex.Position):0.#} m behind the camera."));
            }
        }
    }

    /// <summary>The strip's outer edge is where the fade has reached zero, so the edge cannot be seen.</summary>
    /// <remarks>
    /// This is the invariant that decides whether a cloud plane has a visible boundary. The original
    /// starts its strip on the line where the plane is the fade distance away, which is exactly where the
    /// fade reaches zero — so the geometry stops at the same place the cloud does. The disc that came
    /// before ended at <c>1.5 × Distance</c> from the camera instead, which is a rim of solid cloud
    /// <c>atan(ZPos / 1.5D)</c> above the horizon: five degrees up in missions 3 and 9, where it read as
    /// a lens hanging in the sky.
    /// </remarks>
    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(13)]
    public void TheCloudStripsOuterEdgeIsInvisible(int mission)
    {
        using Level level = Level.Load(mission);
        Camera3D camera = new(default, Camera3D.LookRotation(0.6f, 0.05f));

        foreach (Vertex3D[] strip in DrawAndCollectClouds(level, camera))
        {
            // The first row is the one on that line; the strip steps away from the horizon after it.
            Assert.All(strip.Take(2), vertex => Assert.True(
                vertex.Colour.A == 0,
                $"the strip's outer row is drawn at alpha {vertex.Colour.A}, {ViewDepth(vertex):0.#} m out."));

            // And it climbs from there, rather than being brightest at the edge.
            Assert.True(strip[^1].Colour.A > strip[0].Colour.A);

            // Every vertex sits on the plane the level authored, none of them pushed off it.
            Assert.All(strip, vertex => Assert.Equal(level.Sky.PlaneHeight, vertex.Position.Z, 2));
        }
    }

    /// <summary>The strip spans the viewport, so the sky it covers has no gap in it.</summary>
    /// <remarks>
    /// A screen-space strip is only correct if it actually reaches the edges of the screen: the rows run
    /// the full width and the last of them is past the far corner, both by the original's thousandth of
    /// overscan. Checked by projecting the vertices back with the same matrix they were laid out with.
    /// </remarks>
    [RequiresGameDataFact]
    public void TheCloudStripReachesEveryEdgeOfTheViewport()
    {
        using Level level = Level.Load(1);
        Camera3D camera = new(default, Camera3D.LookRotation(0f, 0f));
        Matrix4f projection = Projection();

        Vertex3D[] strip = DrawAndCollectClouds(level, camera, projection).First();

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        foreach (Vertex3D vertex in strip)
        {
            float depth = Vector3f.Dot(camera.Orientation.Row3, vertex.Position);
            float ndcX = (projection.M11 * (Vector3f.Dot(camera.Orientation.Row1, vertex.Position) / depth))
                + projection.M13;
            float ndcY = (projection.M22 * (Vector3f.Dot(camera.Orientation.Row2, vertex.Position) / depth))
                + projection.M23;
            minX = MathF.Min(minX, ndcX);
            maxX = MathF.Max(maxX, ndcX);
            maxY = MathF.Max(maxY, MathF.Abs(ndcY));
        }

        Assert.True(minX <= -1f, $"the strip stops at {minX:0.###} short of the left edge.");
        Assert.True(maxX >= 1f, $"the strip stops at {maxX:0.###} short of the right edge.");
        Assert.True(maxY >= 1f, $"the strip stops at {maxY:0.###} short of the top edge.");
    }

    /// <summary>Aimed at the ground there is no cloud plane in front of the camera, and none is drawn.</summary>
    /// <remarks>
    /// The original refuses the layout unless both ends of the strip's first row clear its near plane
    /// (<c>409.6</c> units, a tenth of a metre), which looking down they cannot.
    /// </remarks>
    [RequiresGameDataFact]
    public void LookingAtTheGroundDrawsNoCloud()
    {
        using Level level = Level.Load(1);

        Assert.Empty(DrawAndCollectClouds(
            level,
            new Camera3D(default, Camera3D.LookRotation(0f, -MathF.PI / 2f))));
    }

    private static float ViewDepth(Vertex3D vertex) =>
        Vector3f.Dot(Camera3D.LookRotation(0.6f, 0.05f).Row3, vertex.Position);

    private static Matrix4f Projection() =>
        Matrix4f.CreatePerspective(1f, 16f / 9f, ViewPlanes.NearMetres, ViewPlanes.FarMetres);

    /// <summary>Draws the sky and hands back the vertices of each cloud layer's strip.</summary>
    private static List<Vertex3D[]> DrawAndCollectClouds(
        Level level,
        in Camera3D camera,
        Matrix4f? projection = null)
    {
        level.Device.MeshDraws.Clear();
        level.Sky.Draw(camera, projection ?? Projection(), 0f);

        List<Vertex3D[]> strips = [];
        foreach (RecordingGraphicsDevice.MeshDrawCall draw in level.Device.MeshDraws)
        {
            // The dome is opaque and the cloud layers are the alpha-blended draws over it.
            if (draw.Blend == BlendMode.AlphaBlend && level.Device.Meshes.TryGetValue(draw.Mesh.Id, out var mesh))
            {
                strips.Add(mesh.Vertices);
            }
        }

        return strips;
    }

    private static float HalfVisibleAt(SceneFog fog) => MathF.Log(2f) / fog.DensityPerMetre;

    /// <summary>A retail level loaded far enough to build its sky.</summary>
    private sealed class Level : IDisposable
    {
        private Level(RecordingGraphicsDevice device, SkyRenderer sky, GameTask root)
        {
            Device = device;
            Sky = sky;
            Root = root;
            GlobalLight = GlobalLightSettings.Read(root);
        }

        public RecordingGraphicsDevice Device { get; }

        public SkyRenderer Sky { get; }

        /// <summary>Gets the loaded level's task tree.</summary>
        public GameTask Root { get; }

        /// <summary>Gets the air and the model light the level declares.</summary>
        public GlobalLightSettings GlobalLight { get; }

        public static Level Load(int mission)
        {
            VirtualFileSystem fileSystem = new();
            fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
            ResourceRegistry resources = new(fileSystem);
            GameLoop loop = new(new ManualTimeSource());

            MissionRegistry registry = new();
            registry.Register(loop.Natives);
            registry.Scan(fileSystem, resources, loop.Natives);
            PlayerTaskTypes.RegisterAll(loop.Types);

            LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
            LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
            new MissionLoader(registry, fileSystem, resources, levelLoader, null, new TerrainLoader(resources, loop.Natives))
                .Load(mission);

            RecordingGraphicsDevice device = new();
            return new Level(device, new SkyRenderer(device, fileSystem, levelRoot, ViewPlanes.FarMetres), levelRoot);
        }

        public void Dispose() => Sky.Dispose();
    }
}
