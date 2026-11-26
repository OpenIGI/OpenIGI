using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering.Blast;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Headless;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>The bomb's fire and smoke: the same from the same seed, gone in time, and drawn as smoke under light.</summary>
public sealed class BombBlastParticlesTests
{
    private static readonly Vector3d Bomb = new(1000.0 * WorldUnits.PerMetre, 2000.0 * WorldUnits.PerMetre, 10.0 * WorldUnits.PerMetre);

    /// <summary>Two machines that set the same blast off show the same blast, tick for tick.</summary>
    [Fact]
    public void TheSameSeedThrowsTheSameBlast()
    {
        using var a = new BombBlastParticles(new RecordingGraphicsDevice());
        using var b = new BombBlastParticles(new RecordingGraphicsDevice());
        a.Start(Bomb, 40f, 0xC0FFEEu, light: false);
        b.Start(Bomb, 40f, 0xC0FFEEu, light: false);
        for (int tick = 0; tick < 120; tick++)
        {
            Assert.Equal(a.LiveCount, b.LiveCount);
            a.Tick();
            b.Tick();
        }
    }

    /// <summary>The light tier throws a good deal less, and nothing outlives the round's close.</summary>
    [Fact]
    public void TheLightTierThrowsLessAndAllOfItBurnsOut()
    {
        using var full = new BombBlastParticles(new RecordingGraphicsDevice());
        using var light = new BombBlastParticles(new RecordingGraphicsDevice());
        full.Start(Bomb, 40f, 7u, light: false);
        light.Start(Bomb, 40f, 7u, light: true);
        int fullMost = 0;
        int lightMost = 0;
        for (int tick = 0; tick < 30 * 20; tick++)
        {
            full.Tick();
            light.Tick();
            fullMost = Math.Max(fullMost, full.LiveCount);
            lightMost = Math.Max(lightMost, light.LiveCount);
        }

        Assert.InRange(fullMost, 200, BombBlastParticles.MaximumSprites);
        Assert.True(lightMost < fullMost * 0.6, $"light {lightMost}, full {fullMost}");
        Assert.False(full.IsActive);
        Assert.False(light.IsActive);
    }

    /// <summary>
    /// Seen from down the street a moment after it goes off: smoke blended over the scene, the fire added brighter than white,
    /// the flash through walls -- and the device left as it was found.
    /// </summary>
    [Fact]
    public void ItDrawsSmokeThenFireThenTheFlashAndPutsTheDeviceBack()
    {
        var device = new RecordingGraphicsDevice();
        using var particles = new BombBlastParticles(device);
        particles.Start(Bomb, 40f, 11u, light: false);
        for (int tick = 0; tick < 6; tick++)
        {
            particles.Tick();
        }

        var eye = new Camera3D((Bomb / WorldUnits.PerMetre) + new Vector3d(0.0, -60.0, 2.0), Camera3D.LookRotation(0f, 0f));
        particles.Draw(eye, Matrix4f.Identity, 0.5f);

        var draws = device.MeshDraws;
        Assert.Contains(draws, d => d.Blend == BlendMode.AlphaBlend && d.IndexCount > 0);
        Assert.Contains(draws, d => d.Blend == BlendMode.Additive && d.LightmapScale > 1f && d.DepthState == DepthState.TestOnly);
        Assert.Contains(draws, d => d.Blend == BlendMode.Additive && d.DepthState == DepthState.Disabled);

        // The blended smoke comes before anything added over it.
        int firstAdded = draws.FindIndex(d => d.Blend == BlendMode.Additive);
        int lastBlended = draws.FindLastIndex(d => d.Blend == BlendMode.AlphaBlend);
        Assert.True(lastBlended < firstAdded);

        Assert.Equal(1f, device.LightmapScale);
    }

    /// <summary>A building's dust is thrown on its own, and burns out too.</summary>
    [Fact]
    public void ABuildingsDustBurnsOut()
    {
        using var particles = new BombBlastParticles(new RecordingGraphicsDevice());
        particles.StartCollapse(Bomb, 8f, 12f, 3u, light: false);
        Assert.True(particles.LiveCount > 10);
        for (int tick = 0; tick < 30 * 15; tick++)
        {
            particles.Tick();
        }

        Assert.False(particles.IsActive);
    }
}
