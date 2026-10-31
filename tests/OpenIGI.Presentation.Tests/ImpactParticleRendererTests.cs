using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Rendering.Headless;

namespace OpenIGI.Presentation.Tests;

public sealed class ImpactParticleRendererTests
{
    [Fact]
    public void RetailGlassFaceFilterKeepsOnlyTheStrictNativeWinding()
    {
        Vector3d a = new(0d, 0d, 0d);
        Vector3d b = new(1d, 0d, 0d);
        Vector3d c = new(0d, 1d, 0d);

        Assert.True(GlassShatterBuilder.PassesRetailGlassFaceFilter(new Vector3d(0d, 0d, -1d), a, b, c));
        Assert.False(GlassShatterBuilder.PassesRetailGlassFaceFilter(new Vector3d(0d, 0d, 1d), a, b, c));
        Assert.False(GlassShatterBuilder.PassesRetailGlassFaceFilter(Vector3d.Zero, a, b, c));
        Assert.False(GlassShatterBuilder.PassesRetailGlassFaceFilter(new Vector3d(0d, 0d, -1d), a, a, c));
    }

    [Fact]
    public void RetailGlassEdgeDistanceClampsToTheFiniteCollisionEdge()
    {
        Assert.Equal(0d, GlassShatterBuilder.NormalizedGlassEdgeDistance(
            new Vector3d(0.5d, 0d, 0d), Vector3d.Zero, new Vector3d(1d, 0d, 0d)), 12);
        Assert.InRange(
            GlassShatterBuilder.NormalizedGlassEdgeDistance(
                new Vector3d(2d, 0d, 0d), Vector3d.Zero, new Vector3d(1d, 0d, 0d)),
            (1d / 5120d) - 1e-10,
            (1d / 5120d) + 1e-10);
        Assert.InRange(
            GlassShatterBuilder.NormalizedGlassEdgeDistance(
                new Vector3d(0d, 0d, 1d), Vector3d.Zero, Vector3d.Zero),
            (1d / 5120d) - 1e-10,
            (1d / 5120d) + 1e-10);
    }

    [RequiresGameDataFact]
    public void RetailAtlasesDoNotReplaceProceduralImpactTextures()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));

        RecordingGraphicsDevice device = new();
        using ImpactParticleRenderer renderer = new(device, fileSystem, MissionLoader.CommonSpritesArchive);

        Assert.True(renderer.UsesRetailEffectSprites);
        renderer.SpawnBurst(
            new Vector3d(8192d, 8192d, 8192d),
            new Vector3f(0f, 0f, 1f),
            ImpactSurfaceKind.Stone,
            intensity: 1f);
        renderer.Draw(Camera3D.Default, Matrix4f.Identity, alpha: 1f);

        Assert.NotEmpty(device.MeshDraws);
        // RecordingGraphicsDevice allocates the two retail atlas handles first. Procedural impact
        // draws must use the later handles created by GetDustTexture/GetSparkTexture instead.
        Assert.All(device.MeshDraws, draw => Assert.True(draw.Diffuse.Id > 2));
    }

    [RequiresGameDataFact]
    public void RetailExplosionModelsDrawWithTheirOwnFaceGroupBlend()
    {
        (_, ResourceRegistry resources, LoadedMission mission) = LoadRetailMission();
        RecordingGraphicsDevice device = new();
        RetailExplosionEffects explosions = new(device, resources);

        Assert.True(explosions.Load(mission));

        // The FireBall's face group is transparent and additive (0x49EC70 draws it at ONE, ONE).
        Assert.Equal(BlendMode.AddColour, explosions.RetailFireballBlend);
        Assert.Equal(26, explosions.RetailFireballModelFrameCount);

        // A camera lens's blast: the flame adds. Its pieces are drawn with a round's chips, its smoke as particle systems.
        Igi1ExplosionRuntime scene = new(new IgiRandom(), particles: null);
        _ = scene.Spawn(new Igi1ExplosionRequest(new Vector3d(8192d, 8192d, 8192d), 1, 0, 0.25f, 2048f, 0f, 7, 3, string.Empty));
        explosions.Draw(scene, Camera3D.Default, Matrix4f.Identity, alpha: 1f);

        Assert.Contains(device.MeshDraws, draw => draw.Blend == BlendMode.AddColour);
        Assert.DoesNotContain(device.MeshDraws, draw => draw.Blend == BlendMode.AlphaBlend);
        explosions.Release();
    }

    [Fact]
    public void FireBallFacesTheEyeAndGrowsTowardIt()
    {
        Matrix3f young = RetailExplosionEffects.FacingEye(
            Vector3d.Zero, new Vector3d(10d, 0d, 0d), across: 1f, depth: 0.01f);
        Matrix3f old = RetailExplosionEffects.FacingEye(
            Vector3d.Zero, new Vector3d(10d, 0d, 0d), across: 1f, depth: 1f);

        // The dome's own axis (its third column, 0x480790 transposing the rows it builds) points at the eye and carries the
        // depth; the across axis is square to it.
        Assert.Equal(0.01f, young.M13, 5);
        Assert.Equal(1f, old.M13, 5);
        Assert.Equal(0f, (young.M11 * young.M13) + (young.M21 * young.M23) + (young.M31 * young.M33), 5);
    }

    [RequiresGameDataFact]
    public void RetailExplosionSpritesLoadFromTheCommonArchive()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));

        using ImpactParticleRenderer renderer = new(new RecordingGraphicsDevice(), fileSystem, MissionLoader.CommonSpritesArchive);

        Assert.True(renderer.UsesRetailEffectSprites);
        Assert.True(
            renderer.RetailFireballFrameCount > 0,
            $"Retail fireball frames: {renderer.RetailFireballFrameCount}");
        Assert.True(
            renderer.RetailSmokeFrameCount > 0,
            $"Retail smoke frames: {renderer.RetailSmokeFrameCount}");
    }

    private static (VirtualFileSystem FileSystem, ResourceRegistry Resources, LoadedMission Mission) LoadRetailMission()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LevelLoader level = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(missions, fileSystem, resources, level).Load(1);
        return (fileSystem, resources, mission);
    }
}
