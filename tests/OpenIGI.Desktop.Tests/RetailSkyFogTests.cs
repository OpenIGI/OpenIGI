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
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The air between the camera and the ground, against the retail data and the retail rules — see
/// <see cref="SceneFog"/> for where in <c>IGI.EXE</c> each of them was read.
/// </summary>
/// <remarks>
/// The fog is the difference between the original's picture and a postcard. Every shipping level asks
/// for enough of it that half a surface is gone by four hundred metres, which is why retail can afford a
/// hundred-kilometre far plane and still look like a mission rather than a flight simulator.
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
    /// <c>sub_52D060</c> turns <c>FOGENABLE</c> off with its depth states before the dome and the cloud
    /// layers. It has to: the dome is a sphere of eighty kilometres, which is several times past the
    /// distance at which this fog leaves nothing of a surface, so a fogged dome is a flat wash of the
    /// fog colour and the level has no sky at all.
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

    /// <summary>The dome is sized to the original's four fifths of the far plane.</summary>
    /// <remarks>
    /// <c>sub_52CC40</c> scales every dome vertex by <c>dword_BCADBC * 0.8</c>, and that global holds the
    /// same 409,600,000 units — a hundred kilometres — as the far clip. Eighty kilometres of dome behind
    /// a hundred of frustum is what keeps the sky behind the furthest ground the descent can reach.
    /// </remarks>
    [Fact]
    public void TheDomeIsFourFifthsOfTheRetailFarPlane()
    {
        Assert.Equal(100_000f, ViewPlanes.FarMetres);
        Assert.Equal(409_600_000f, ViewPlanes.FarMetres * 4096f);
        Assert.Equal(409.6f, ViewPlanes.NearMetres * 4096f, 3);
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
