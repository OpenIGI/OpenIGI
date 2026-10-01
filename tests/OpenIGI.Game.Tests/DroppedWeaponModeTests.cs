using System.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>The two ways a dropped gun falls: the original's, and the realistic rigid body.</summary>
public sealed class DroppedWeaponModeTests(ITestOutputHelper output)
{
    private const double Metre = RigidMeshBody.UnitsPerMetre;

    /// <summary>
    /// <c>0x46CEA0</c>: straight down under gravity, back up at half speed on each landing, stopping once
    /// a landing is slower than 409.6 units a tick -- and a drop sound on every bounce.
    /// </summary>
    [Fact]
    public void TheOriginalsDropFallsStraightAndBouncesAtHalfSpeed()
    {
        RetailGunDrop drop = new(new Vector3d(100, 200, 2.0 * Metre), restHeight: 0.0);

        List<double> bounceSpeeds = [];
        for (int tick = 0; tick < 300 && !drop.IsResting; tick++)
        {
            double falling = drop.FallSpeed;
            if (drop.Tick())
            {
                bounceSpeeds.Add(falling);

                // Reversed and halved, then one tick of gravity -- or stopped outright when too slow.
                double expected = drop.IsResting ? 0.0 : (-0.5 * falling) + DroppedGunPhysics.GravityPerTick;
                Assert.Equal(expected, drop.FallSpeed, 6);
            }

            Assert.Equal(100.0, drop.Position.X);
            Assert.Equal(200.0, drop.Position.Y);
        }

        Assert.True(drop.IsResting);
        Assert.Equal(0.0, drop.Position.Z, 6);
        Assert.NotEmpty(bounceSpeeds);

        // Each landing is half as fast as the one before it, give or take the gravity of the partial tick.
        for (int i = 1; i < bounceSpeeds.Count; i++)
        {
            Assert.True(bounceSpeeds[i] < bounceSpeeds[i - 1]);
        }

        output.WriteLine($"{bounceSpeeds.Count} bounce(s): {string.Join(", ", bounceSpeeds.Select(s => s.ToString("0", System.Globalization.CultureInfo.InvariantCulture)))}");
    }

    /// <summary><c>0x46C980</c> keeps the hand's heading and lays the gun on its side.</summary>
    [Fact]
    public void TheOriginalLaysTheGunOnItsSideKeepingItsHeading()
    {
        Matrix3f hand = Matrix3f.FromEngineEulerAngles(0.7f, -0.4f, 1.1f);
        Matrix3f lying = RetailGunDrop.LyingOrientation(hand);

        Matrix3f expected = Matrix3f.FromEngineEulerAngles(0f, MathF.PI / 2f, MathF.Atan2(hand.M21, hand.M11));
        Assert.Equal(expected.M11, lying.M11, 5);
        Assert.Equal(expected.M22, lying.M22, 5);
        Assert.Equal(expected.M31, lying.M31, 5);
        Assert.Equal(expected.M33, lying.M33, 5);
    }

    /// <summary>The rest height is ten centimetres above what a fifty-metre trace meets, or the drop point.</summary>
    [Fact]
    public void TheOriginalRestsTenCentimetresAboveTheSurfaceBelow()
    {
        WorldRayCast floor = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            hit = new WorldRayHit(new Vector3d(origin.X, origin.Y, -3000.0), new Vector3f(0f, 0f, 1f), 0.0);
            return end.Z < -3000.0 && origin.Z > -3000.0;
        };

        Assert.Equal(-3000.0 + 409.6, RetailGunDrop.RestHeightBelow(new Vector3d(0, 0, 1000), floor), 6);
        Assert.Equal(1000.0, RetailGunDrop.RestHeightBelow(new Vector3d(0, 0, 1000), null), 6);
    }

    /// <summary>
    /// A rifle-sized body dropped onto level 1's real terrain and buildings, through the same triangle
    /// queries the game hands it, lands on the surface, comes to rest, and costs little doing it.
    /// </summary>
    [RequiresGameDataFact]
    public void ARealisticDropSettlesOnLevelOnesGroundAndBuildings()
    {
        (TerrainData terrain, double x, double y, double z) = RetailLevelOneGround.Find();
        WorldScene scene = RetailLevelOneGround.Scene();
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        CollisionTriangleGather world = (min, max, into) =>
        {
            solids.CollectTriangles(min, max, into);
            TerrainRayTrace.CollectTriangles(terrain, min, max, into);
        };
        WorldTraceCast trace = WorldTrace.Nearest(solids.Tracer(), TerrainRayTrace.Caster(terrain));

        ConvexHull hull = ConvexHull.Build(
        [
            new(-0.4 * Metre, -0.03 * Metre, -0.1 * Metre), new(0.4 * Metre, -0.03 * Metre, -0.1 * Metre),
            new(-0.4 * Metre, 0.03 * Metre, -0.1 * Metre), new(0.4 * Metre, 0.03 * Metre, -0.1 * Metre),
            new(-0.4 * Metre, -0.03 * Metre, 0.1 * Metre), new(0.4 * Metre, -0.03 * Metre, 0.1 * Metre),
            new(-0.4 * Metre, 0.03 * Metre, 0.1 * Metre), new(0.4 * Metre, 0.03 * Metre, 0.1 * Metre),
        ])!;

        // The open ground, and then the tops of the first placed objects a downward trace meets.
        List<Vector3d> spots = [new(x, y, z)];
        foreach (WorldPlacement placement in scene.Placements.Where(p => !p.IsAttached).Take(40))
        {
            Vector3d centre = placement.Position * WorldScene.UnitsPerMetre;
            if (trace(new WorldTraceRequest(centre + new Vector3d(0, 0, 40000), centre - new Vector3d(0, 0, 40000)), out WorldTraceHit top)
                && top.Kind != WorldTraceHitKind.Terrain)
            {
                spots.Add(top.Position);
            }

            if (spots.Count >= 6)
            {
                break;
            }
        }

        Assert.True(spots.Count >= 2, "no object tops to drop onto");

        Stopwatch clock = new();
        int totalTicks = 0;
        foreach (Vector3d surface in spots)
        {
            RigidMeshBody body = new(
                hull,
                3.5,
                surface + new Vector3d(0, 0, 1.5 * Metre),
                Matrix3f.FromEngineEulerAngles(0.6f, 0.3f, 0.9f),
                new Vector3d(60, -30, 20),
                new Vector3d(0.05, 0.02, -0.04));

            clock.Start();
            int ticks = 0;
            while (!body.IsAtRest && ticks < 600)
            {
                body.Step(world);
                ticks++;
            }

            clock.Stop();
            totalTicks += ticks;

            Assert.True(body.IsAtRest, $"never settled at {surface}");

            // It rests on something at or near where it fell, not below the surface it fell onto.
            Assert.True(
                body.CentreOfMass.Z > surface.Z - (0.5 * Metre),
                $"fell through at {surface}: centre {body.CentreOfMass.Z - surface.Z:0} units from the surface");
            output.WriteLine(
                $"settled in {ticks} ticks, {(body.CentreOfMass - surface).Length / Metre:0.00} m from the landing point");
        }

        double perTick = clock.Elapsed.TotalMilliseconds / Math.Max(totalTicks, 1);
        output.WriteLine($"{perTick:0.000} ms per simulated tick against level 1");
        // Catches a cost grown by an order of magnitude; allows for a CI machine running two builds at once.
        Assert.True(perTick < 6.0, $"{perTick:0.000} ms per tick");
    }

    private static GunPickupRegistry EmptyRegistry()
    {
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        return GunPickupRegistry.Build(tree.Root, new WeaponTypeRegistry(), new AmmoTypeRegistry());
    }

    private static DroppedGunCollider RifleCollider()
    {
        List<Vector3f> corners = [];
        foreach (float x in new[] { -0.4f, 0.4f })
        {
            foreach (float y in new[] { -0.03f, 0.03f })
            {
                foreach (float z in new[] { -0.1f, 0.1f })
                {
                    corners.Add(new Vector3f(x * (float)Metre, y * (float)Metre, z * (float)Metre));
                }
            }
        }

        return new DroppedGunCollider(corners);
    }

    private static RagdollWorld Floor(double height)
    {
        CollisionTriangle[] floor =
        [
            new(new Vector3d(-1e6, -1e6, height), new Vector3d(1e6, -1e6, height), new Vector3d(1e6, 1e6, height)),
            new(new Vector3d(-1e6, -1e6, height), new Vector3d(1e6, 1e6, height), new Vector3d(-1e6, 1e6, height)),
        ];

        return new RagdollWorld(ground: (double x, double y, double z, out double g) =>
        {
            g = height;
            return z >= height;
        })
        {
            Triangles = (min, max, into) => into.AddRange(floor.Where(t => t.Overlaps(min, max))),
            Trace = (Vector3d from, Vector3d to, out WorldRayHit hit) =>
            {
                hit = new WorldRayHit(new Vector3d(from.X, from.Y, height), new Vector3f(0f, 0f, 1f), from.Z - height);
                return from.Z > height && to.Z < height;
            },
        };
    }

    /// <summary>
    /// Through the registry, the Vanilla preset's drop is the original's: laid on its side, straight
    /// down to ten centimetres above the floor, with a clatter reported for every bounce.
    /// </summary>
    [Fact]
    public void AVanillaDropThroughTheRegistryIsTheOriginals()
    {
        bool previous = GameplayCVars.RealisticDrops.Value;
        GameplayCVars.RealisticDrops.Value = false;
        try
        {
            GunPickupRegistry registry = EmptyRegistry();
            WeaponType rifle = new() { MassInGrams = 3400 };
            GunPickup pickup = registry.Spawn(
                new Vector3d(500, 600, 1.2 * Metre),
                Matrix3f.FromEngineEulerAngles(0.4f, 0.2f, 0.8f),
                rifle,
                new Vector3d(80, 0, 40));
            pickup.UseCollider(RifleCollider());

            int clatters = 0;
            RagdollWorld world = Floor(0.0);
            for (int tick = 0; tick < 200; tick++)
            {
                registry.Tick(world);
                clatters += registry.Impacts.Count;
            }

            Assert.Equal(500.0, pickup.Position.X, 6);
            Assert.Equal(600.0, pickup.Position.Y, 6);
            Assert.Equal(RetailGunDrop.RestAboveSurface, pickup.Position.Z, 6);
            Assert.True(clatters >= 1);

            // At rest it is drawn at rest whatever the frame's share of the tick. The blend used to keep
            // interpolating from the last falling state, a tick below the floor, and the gun flickered
            // up and down in place for ever.
            foreach (float share in new[] { 0f, 0.35f, 0.7f, 1f })
            {
                pickup.Present(share, smooth: true);
                Assert.Equal(RetailGunDrop.RestAboveSurface, pickup.DrawPosition.Z, 6);
            }
        }
        finally
        {
            GameplayCVars.RealisticDrops.Value = previous;
        }
    }

    /// <summary>Through the registry, every other preset drops a real rigid body that tumbles and clatters.</summary>
    [Fact]
    public void ARealisticDropThroughTheRegistryTumblesAndLands()
    {
        bool previous = GameplayCVars.RealisticDrops.Value;
        GameplayCVars.RealisticDrops.Value = true;
        try
        {
            GunPickupRegistry registry = EmptyRegistry();
            WeaponType rifle = new() { MassInGrams = 3400 };
            GunPickup pickup = registry.Spawn(
                new Vector3d(500, 600, 1.2 * Metre),
                Matrix3f.FromEngineEulerAngles(0.4f, 0.2f, 0.8f),
                rifle,
                new Vector3d(80, 0, 40));
            pickup.UseCollider(RifleCollider());

            int clatters = 0;
            RagdollWorld world = Floor(0.0);
            for (int tick = 0; tick < 300; tick++)
            {
                registry.Tick(world);
                clatters += registry.Impacts.Count;
            }

            // It carried the knock sideways and came to rest lying on the floor, not ten centimetres above it.
            Assert.True(pickup.Position.X > 500.0 + (0.1 * Metre), $"moved only to {pickup.Position.X}");
            Assert.InRange(pickup.Position.Z, 0.0, 0.2 * Metre);
            Assert.InRange(clatters, 1, 6);
            Assert.Equal(3.4, pickup.MassKilograms, 6);
        }
        finally
        {
            GameplayCVars.RealisticDrops.Value = previous;
        }
    }
}
