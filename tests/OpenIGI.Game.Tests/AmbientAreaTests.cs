using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>The <c>AmbientArea</c> box test and its fade, from the update <c>0x44C370</c>.</summary>
public sealed class AmbientAreaTests
{
    [Fact]
    public void TheBoxIsHalfExtentsInMetresAgainstAPointInEngineUnits()
    {
        // The original compares the local offset against size * ±4096, so a "size" of 2 reaches two
        // metres each way, not two units and not four metres across.
        PlacedAmbientArea area = Area(halfExtentsMetres: new Vector3d(2.0, 3.0, 1.0));

        Assert.True(AmbientAreaRegistry.Contains(area, new Vector3d(0, 0, 0)));
        Assert.True(AmbientAreaRegistry.Contains(area, new Vector3d(1.9 * 4096, 0, 0)));
        Assert.False(AmbientAreaRegistry.Contains(area, new Vector3d(2.1 * 4096, 0, 0)));

        // Strict at the boundary, at both ends.
        Assert.False(AmbientAreaRegistry.Contains(area, new Vector3d(2.0 * 4096, 0, 0)));
        Assert.False(AmbientAreaRegistry.Contains(area, new Vector3d(-2.0 * 4096, 0, 0)));

        // Each axis has its own extent.
        Assert.True(AmbientAreaRegistry.Contains(area, new Vector3d(0, 2.9 * 4096, 0)));
        Assert.False(AmbientAreaRegistry.Contains(area, new Vector3d(0, 0, 1.1 * 4096)));
    }

    [Fact]
    public void TheBoxIsCentredOnItsPositionRatherThanCornered()
    {
        PlacedAmbientArea area = Area(
            position: new Vector3d(10_000, 0, 0),
            halfExtentsMetres: new Vector3d(1.0, 1.0, 1.0));

        Assert.True(AmbientAreaRegistry.Contains(area, new Vector3d(10_000, 0, 0)));
        Assert.False(AmbientAreaRegistry.Contains(area, new Vector3d(0, 0, 0)));
    }

    [Fact]
    public void TheFadeRampsInAndOutTwentyTicksEachWay()
    {
        // 0.05 a tick, clamped. The point of the ramp is that a bed does not switch on at a doorway.
        AmbientAreaRegistry registry = new(new IgiRandom());
        PlacedAmbientArea area = Area(halfExtentsMetres: new Vector3d(1.0, 1.0, 1.0));

        Vector3d inside = new(0, 0, 0);
        Vector3d outside = new(100_000, 0, 0);

        // It is not the registry's own list, so drive Contains + the ramp through a built registry.
        AmbientAreaRegistry live = Registry(area);

        for (int tick = 0; tick < 19; tick++)
        {
            live.Tick(inside);
        }

        Assert.InRange(area.Fade, 0.94, 0.96);

        live.Tick(inside);
        Assert.Equal(1.0, area.Fade, 6);

        // And it stays clamped rather than climbing.
        live.Tick(inside);
        Assert.Equal(1.0, area.Fade, 6);

        for (int tick = 0; tick < 20; tick++)
        {
            live.Tick(outside);
        }

        Assert.Equal(0.0, area.Fade, 6);
        Assert.False(area.Inside);
        _ = registry;
    }

    [Fact]
    public void ABedStartsOnlyOnceAndOnlyWhileInside()
    {
        PlacedAmbientArea area = Area(halfExtentsMetres: new Vector3d(1.0, 1.0, 1.0), sound: "hum");
        AmbientAreaRegistry live = Registry(area);

        live.Tick(new Vector3d(0, 0, 0));
        Assert.Equal("hum", area.PendingSound);

        // Whoever plays it says so, and the area then asks for nothing further.
        area.IsSounding = true;
        live.Tick(new Vector3d(0, 0, 0));
        Assert.Equal(string.Empty, area.PendingSound);
        Assert.False(area.WantsStop);
    }

    [Fact]
    public void LeavingTheBoxAsksForTheBedToStopOnlyOnceItHasFadedRightOut()
    {
        PlacedAmbientArea area = Area(halfExtentsMetres: new Vector3d(1.0, 1.0, 1.0), sound: "hum");
        AmbientAreaRegistry live = Registry(area);

        // Fully faded in first: from a single tick inside the fade is only 0.05, and one tick back out
        // would take it straight to zero.
        for (int tick = 0; tick < 20; tick++)
        {
            live.Tick(new Vector3d(0, 0, 0));
        }

        // The bed started at once (no gap authored), so the area holds its instance.
        Assert.True(area.HasVoice);
        Vector3d outside = new(100_000, 0, 0);

        // Fading out, still sounding: the gain falls but the voice is kept.
        live.Tick(outside);
        Assert.False(area.WantsStop);
        Assert.True(area.Fade > 0.0);

        // 0x44C370 lets the instance go on the tick the fade reaches zero, and only then.
        bool stopped = false;
        for (int tick = 0; tick < 30 && !stopped; tick++)
        {
            live.Tick(outside);
            stopped = area.WantsStop;
        }

        Assert.True(stopped);
        Assert.Equal(0.0, area.Fade, 6);
        Assert.False(area.HasVoice);
        live.Tick(outside);
        Assert.False(area.WantsStop);
    }

    [Fact]
    public void AnAreaWithNoVoiceDrawsAVariantEveryTickUntilItsGapRunsOut()
    {
        // 0x44C370: inside and holding no instance, the variant is drawn every tick -- whether or not the gap lets it
        // start -- and once a sound starts the area holds it and draws nothing more. Mission 4's five gapped areas
        // drew five a tick from tick 2 until their gaps ran out.
        PlacedAmbientArea area = new(
            null!, default, Matrix3f.Identity, new Vector3d(1.0, 1.0, 1.0),
            MinDelaySeconds: 0.1, RandomWaitSeconds: 0.0, Sounds: ["dog_01", "dog_02", "dog_03"]);
        IgiRandom stream = new();
        int draws = 0;
        stream.Drew = _ => draws++;
        AmbientAreaRegistry live = new(stream);
        ((List<PlacedAmbientArea>)live.Areas).Add(area);
        area.Countdown = 3;

        for (int tick = 0; tick < 3; tick++)
        {
            live.Tick(default(Vector3d));
            Assert.False(area.HasVoice);
        }

        live.Tick(default(Vector3d));
        Assert.True(area.HasVoice);
        Assert.Equal(4, draws);

        live.Tick(default(Vector3d));
        Assert.Equal(4, draws);
    }

    private static PlacedAmbientArea Area(
        Vector3d position = default,
        Vector3d halfExtentsMetres = default,
        string sound = "") =>
        new(
            null!,
            position,
            Matrix3f.Identity,
            halfExtentsMetres,
            MinDelaySeconds: 0.0,
            RandomWaitSeconds: 0.0,
            Sounds: sound.Length > 0 ? [sound] : []);

    /// <summary>A registry holding one hand-built area, since Build needs a real task tree.</summary>
    private static AmbientAreaRegistry Registry(PlacedAmbientArea area)
    {
        AmbientAreaRegistry registry = new(new IgiRandom());
        ((List<PlacedAmbientArea>)registry.Areas).Add(area);
        return registry;
    }
}
