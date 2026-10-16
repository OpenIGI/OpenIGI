using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Mission 1's water as <c>h/2/water.c</c> runs it: its depth, its surface, its ripples and its splashes.</summary>
public sealed class RetailIgi2WaterModuleTests
{
    /// <summary>
    /// A point a metre under a body's level, at its centre, is under water by about a metre, the layers' waves either way; ten
    /// metres up it is not asked (<c>Water_IsUnderWater</c>).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_point_under_the_surface_is_under_water()
    {
        (_, Igi2Water water, Igi2WaterBody body) = Build();
        float swell = body.Task.Children.OfType<WaterLayerTask>().Take(Igi2WaterBody.MaxLayers).Sum(static l => Math.Abs(l.Amplitude)) * 4096f;
        Vector3d centre = body.Position;

        Assert.True(water.IsUnderWater(new Vector3d(centre.X, centre.Y, centre.Z - 4096.0), out float depth, out int index));
        Assert.Same(body, water.Bodies[index]);
        Assert.InRange(depth, 4096f - swell - 1f, 4096f + swell + 1f);

        Assert.False(water.IsUnderWater(new Vector3d(centre.X, centre.Y, centre.Z + 40960.0), out _, out int none));
        Assert.Equal(-1, none);
    }

    /// <summary>A line down through the surface strikes it at the body's level, on material two, facing up (<c>Water_TraceLinesHandler</c>).</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_line_down_through_the_surface_strikes_it()
    {
        (Igi2CollisionWorld world, _, Igi2WaterBody body) = Build();
        Vector3d centre = body.Position;
        Igi2LineTrace trace = new(1);
        trace.Starts[0] = new Vector3d(centre.X + 100.0, centre.Y + 50.0, centre.Z + 2048.0);
        trace.Ends[0] = new Vector3d(centre.X + 300.0, centre.Y + 50.0, centre.Z - 2048.0);
        trace.Count = 1;

        world.TraceLines(trace, Igi2Water.Group);

        Igi2Intersection hit = trace.Intersections[0];
        Assert.True(hit.IsHit);
        Assert.Same(body, hit.HitObject);
        Assert.Equal(centre.Z, hit.Position.Z, 3);
        Assert.Equal(centre.X + 200.0, hit.Position.X, 1);
        Assert.Equal(Igi2WaterBody.SurfaceMaterial, hit.EnterMaterial);
        Assert.Equal(new Vector3f(0f, 0f, 1f), hit.Normal);
    }

    /// <summary>
    /// A wading human's ring is not made again in the same place until the last has half spread, and a ring is gone once it has
    /// spread its 150 ticks (<c>Water_PlayerMovementEffect</c>, <c>Water_UpdateWave</c>).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Wading_rings_the_water_until_they_have_spread()
    {
        (_, Igi2Water water, Igi2WaterBody body) = Build();
        int index = water.Bodies.ToList().IndexOf(body);
        Vector3d at = body.Position;

        water.PlayerMovementEffect(at, index, isRunning: false);
        water.PlayerMovementEffect(at, index, isRunning: true);
        Assert.Single(body.Waves);
        Assert.Equal(16384f, body.Waves[0].Reach);

        for (int tick = 0; tick < 76; tick++)
        {
            body.Run();
        }

        water.PlayerMovementEffect(at, index, isRunning: true);
        Assert.Equal(2, body.Waves.Count);
        Assert.Equal(32768f, body.Waves[1].Reach);

        for (int tick = 0; tick <= Igi2WaterBody.WaveDuration; tick++)
        {
            body.Run();
        }

        Assert.Empty(body.Waves);
    }

    /// <summary>
    /// A blast under water throws seven columns of spray, two draws of the water's own stream each, and rings the body
    /// (<c>Water_GunShotEffect</c>).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_blast_under_water_throws_spray()
    {
        (_, Igi2Water water, Igi2WaterBody body) = Build();
        IgiRandom expected = new(unchecked((uint)-925902492), unchecked((uint)-1684277418), 376075981u);
        for (int i = 0; i < 14; i++)
        {
            expected.NextRaw();
        }

        water.ExplosionEffect(water.Bodies.ToList().IndexOf(body), body.Position);

        Assert.Equal(7, body.Splashes.Count);
        Assert.All(body.Splashes, static drop => Assert.True(drop.IsSpray));
        Assert.Equal(expected.NextRaw(), water.Random.NextRaw());
        Assert.Single(body.Waves);
    }

    private static (Igi2CollisionWorld World, Igi2Water Water, Igi2WaterBody Body) Build()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2CollisionWorld world = m.Collision!;
        Igi2Water water = new();
        foreach (WaterTask task in m.Tree.OfType<WaterTask>())
        {
            Igi2WaterBody added = water.Add(task);
            task.Body = added;
            world.AddWater(added);
        }

        Assert.NotEmpty(water.Bodies);
        return (world, water, water.Bodies.OrderByDescending(static b => b.Size).First());
    }
}
