using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Engine.Vfs;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

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
            modelScale: 0.9f);

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
}
