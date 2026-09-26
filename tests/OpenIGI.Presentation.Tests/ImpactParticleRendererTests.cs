using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Presentation.Tests;

public sealed class ImpactParticleRendererTests
{
    [Fact]
    public void RetailGlassFaceFilterKeepsOnlyTheStrictNativeWinding()
    {
        Vector3d a = new(0d, 0d, 0d);
        Vector3d b = new(1d, 0d, 0d);
        Vector3d c = new(0d, 1d, 0d);

        Assert.True(ImpactParticleRenderer.PassesRetailGlassFaceFilter(new Vector3d(0d, 0d, -1d), a, b, c));
        Assert.False(ImpactParticleRenderer.PassesRetailGlassFaceFilter(new Vector3d(0d, 0d, 1d), a, b, c));
        Assert.False(ImpactParticleRenderer.PassesRetailGlassFaceFilter(Vector3d.Zero, a, b, c));
        Assert.False(ImpactParticleRenderer.PassesRetailGlassFaceFilter(new Vector3d(0d, 0d, -1d), a, a, c));
    }

    [Fact]
    public void RetailGlassEdgeDistanceClampsToTheFiniteCollisionEdge()
    {
        Assert.Equal(0d, ImpactParticleRenderer.NormalizedGlassEdgeDistance(
            new Vector3d(0.5d, 0d, 0d), Vector3d.Zero, new Vector3d(1d, 0d, 0d)), 12);
        Assert.InRange(
            ImpactParticleRenderer.NormalizedGlassEdgeDistance(
                new Vector3d(2d, 0d, 0d), Vector3d.Zero, new Vector3d(1d, 0d, 0d)),
            (1d / 5120d) - 1e-10,
            (1d / 5120d) + 1e-10);
        Assert.InRange(
            ImpactParticleRenderer.NormalizedGlassEdgeDistance(
                new Vector3d(0d, 0d, 1d), Vector3d.Zero, Vector3d.Zero),
            (1d / 5120d) - 1e-10,
            (1d / 5120d) + 1e-10);
    }

    [Fact]
    public void RetailExplosionMotionUsesAuthoredDragAndScaleGravity()
    {
        (Vector3d position, Vector3f velocity) = ImpactParticleRenderer.AdvanceRetailExplosionMotion(
            Vector3d.Zero,
            Vector3f.Zero,
            gravityScale: 0.9f);

        float expectedGravity = 0.9f * 44.600006f * 0.98f;
        Assert.Equal(0f, position.X);
        Assert.Equal(0f, position.Y);
        Assert.Equal(-expectedGravity, position.Z, 4);
        Assert.Equal(velocity.Z, position.Z, 4);
    }

    [Fact]
    public void ExplosionUsesAuthoredFragmentsAndOptionalSmoke()
    {
        RecordingGraphicsDevice device = new();
        using ImpactParticleRenderer renderer = new(device);

        renderer.SpawnExplosion(new Vector3d(8192, 8192, 8192), 1f, fireballScale: 1.5f, fragments: 2);
        int withSmoke = renderer.LiveCount;

        renderer.Clear();
        renderer.SpawnExplosion(
            new Vector3d(8192, 8192, 8192),
            1f,
            fireballScale: 1.5f,
            fragments: 2,
            includeSmoke: false);
        int withoutSmoke = renderer.LiveCount;

        renderer.Clear();
        renderer.SpawnExplosion(new Vector3d(8192, 8192, 8192), 1f, fireballScale: 1.5f, fragments: 12);

        Assert.True(withSmoke > withoutSmoke);
        Assert.True(renderer.LiveCount > withSmoke);
    }

    [RequiresGameDataFact]
    public void RetailAtlasesDoNotReplaceProceduralImpactTextures()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));

        RecordingGraphicsDevice device = new();
        using ImpactParticleRenderer renderer = new(device, fileSystem);

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
    public void RetailFireBallOutlivesTheTickItStarts()
    {
        (VirtualFileSystem fileSystem, ResourceRegistry resources, LoadedMission mission) = LoadRetailMission();
        RecordingGraphicsDevice device = new();
        using ImpactParticleRenderer renderer = new(device, resources, fileSystem);

        Assert.True(renderer.Load(mission));
        Assert.True(renderer.UsesRetailEffectSprites);
        renderer.SpawnExplosion(
            new Vector3d(8192d, 8192d, 8192d),
            intensity: 1f,
            fireballScale: 1f,
            fragments: 2,
            includeSmoke: false);

        renderer.Draw(Camera3D.Default, Matrix4f.Identity, alpha: 1f);
        Assert.Contains(device.MeshDraws, draw => draw.Blend == BlendMode.Additive);

        device.MeshDraws.Clear();
        renderer.Tick();
        renderer.Draw(Camera3D.Default, Matrix4f.Identity, alpha: 1f);

        // Explosion0 runs once and is gone, but its FireBall lives 75 ticks and keeps adding its flame.
        Assert.Contains(device.MeshDraws, draw => draw.Blend == BlendMode.Additive);
    }

    [RequiresGameDataFact]
    public void RetailExplosionModelsDrawWithTheirOwnFaceGroupBlend()
    {
        (VirtualFileSystem fileSystem, ResourceRegistry resources, LoadedMission mission) = LoadRetailMission();
        RecordingGraphicsDevice device = new();
        using ImpactParticleRenderer renderer = new(device, resources, fileSystem);

        Assert.True(renderer.Load(mission));

        // The FireBall's face group is transparent and additive (0x49EC70 draws it at ONE, ONE); the
        // fragment's is neither, so it is an ordinary opaque model (0x49DF60).
        Assert.Equal(BlendMode.Additive, renderer.RetailFireballBlend);
        Assert.Equal(BlendMode.Opaque, renderer.RetailFragmentBlend);
        Assert.Equal(26, renderer.RetailFireballModelFrameCount);

        renderer.SpawnRetailExplosion(new Vector3d(8192d, 8192d, 8192d), RetailExplosionShape.CameraLens);
        renderer.Draw(Camera3D.Default, Matrix4f.Identity, alpha: 1f);

        // The flame adds, the debris is solid, and the only blended draw left is the smoke, whose
        // texture fades to nothing at its edge. Nothing else could paint a box around the blast.
        Assert.Contains(device.MeshDraws, draw => draw.Blend == BlendMode.Additive);
        Assert.Contains(device.MeshDraws, draw => draw.Blend == BlendMode.Opaque);
        Assert.Single(device.MeshDraws, draw => draw.Blend == BlendMode.AlphaBlend);
    }

    [Fact]
    public void FireBallFacesTheEyeAndGrowsTowardIt()
    {
        Matrix3f young = ImpactParticleRenderer.FacingEye(
            Vector3d.Zero, new Vector3d(10d, 0d, 0d), across: 1f, depth: 0.01f);
        Matrix3f old = ImpactParticleRenderer.FacingEye(
            Vector3d.Zero, new Vector3d(10d, 0d, 0d), across: 1f, depth: 1f);

        // The dome's own axis (its third row) points at the eye and carries the depth.
        Assert.Equal(0.01f, young.M31, 5);
        Assert.Equal(1f, old.M31, 5);
        Assert.Equal(0f, young.M11 * young.M31, 5);
    }

    [RequiresGameDataFact]
    public void RetailExplosionSpritesLoadFromTheCommonArchive()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));

        using ImpactParticleRenderer renderer = new(new RecordingGraphicsDevice(), fileSystem);

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
