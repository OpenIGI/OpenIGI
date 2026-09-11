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
    /// <summary>Every mission declares one sky, and the fog it asks for is the fog the port builds.</summary>
    [RequiresGameDataFact]
    public void EveryLevelsFlatSkyAsksForTheFogTheOriginalDraws()
    {
        int levels = 0;

        for (int mission = 1; mission <= 14; mission++)
        {
            using Level level = Level.Load(mission);
            if (!level.Sky.HasSky)
            {
                // Level 14 ships no FlatSky, as it ships no usable terrain.
                Assert.Equal(14, mission);
                continue;
            }

            levels++;
            SceneFog fog = SceneFog.FromFlatSky(level.Sky.FogColour, level.Sky.FogAmount);

            // The authored range across the campaign, and what it means in metres. Anything outside this
            // is either a level read wrongly or a chain of constants that has drifted.
            Assert.InRange(level.Sky.FogAmount, 0.7f, 0.975f);
            Assert.InRange(fog.DensityPerMetre, 0.0014f, 0.0021f);
            Assert.InRange(HalfVisibleAt(fog), 330f, 500f);

            // Half a kilometre out a surface still shows through; four kilometres out it does not. The
            // thinnest fog in the campaign is mission 7's 0.70, which still leaves only a hundredth of a
            // surface at 3.2 km.
            Assert.InRange(fog.FactorAt(500f), 0.35f, 0.5f);
            Assert.True(fog.FactorAt(4000f) < 0.01f, $"mission {mission} still shows {fog.FactorAt(4000f):0.###} at 4 km");

            // The same slate blue in all thirteen, which is the colour the horizon settles to.
            Assert.Equal(0.2f, fog.Colour.X, 3);
            Assert.Equal(0.288f, fog.Colour.Y, 3);
            Assert.Equal(0.4f, fog.Colour.Z, 3);
        }

        Assert.Equal(13, levels);
    }

    /// <summary>The sky is drawn unfogged, and gives back the fog it found.</summary>
    /// <remarks>
    /// <c>sub_52D060</c> turns <c>FOGENABLE</c> off with its depth states before the dome and the cloud
    /// layers. It has to: the dome is a sphere of eighty kilometres, which is several times past the
    /// distance at which this fog leaves nothing of a surface, so a fogged dome is a flat wash of the
    /// fog colour and the level has no sky at all.
    /// </remarks>
    [RequiresGameDataFact]
    public void TheSkyDrawsUnfoggedAndLeavesTheFramesFogAsItFoundIt()
    {
        using Level level = Level.Load(1);
        SceneFog fog = SceneFog.FromFlatSky(level.Sky.FogColour, level.Sky.FogAmount);

        level.Device.SetFog(fog);
        level.Device.MeshDraws.Clear();
        level.Sky.Draw(
            new Camera3D(new Vector3d(0, 0, 0), Camera3D.LookRotation(0f, 0f)),
            Matrix4f.CreatePerspective(1f, 16f / 9f, ViewPlanes.NearMetres, ViewPlanes.FarMetres),
            0f);

        Assert.NotEmpty(level.Device.MeshDraws);
        Assert.All(level.Device.MeshDraws, draw => Assert.False(draw.Fog.IsEnabled));
        Assert.Equal(fog, level.Device.Fog);
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
        private Level(RecordingGraphicsDevice device, SkyRenderer sky)
        {
            Device = device;
            Sky = sky;
        }

        public RecordingGraphicsDevice Device { get; }

        public SkyRenderer Sky { get; }

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
            return new Level(device, new SkyRenderer(device, fileSystem, levelRoot, ViewPlanes.FarMetres));
        }

        public void Dispose() => Sky.Dispose();
    }
}
