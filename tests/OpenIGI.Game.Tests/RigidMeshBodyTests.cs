using System.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>The realistic dropped-object rigid body: a mesh hull against real world triangles.</summary>
public sealed class RigidMeshBodyTests(ITestOutputHelper output)
{
    private const double Metre = RigidMeshBody.UnitsPerMetre;

    // A rifle-sized slab: 80 cm long, 6 cm thick, 20 cm tall, 3.5 kg.
    private static ConvexHull Rifle() => Box(0.8 * Metre, 0.06 * Metre, 0.2 * Metre);

    private static ConvexHull Box(double x, double y, double z)
    {
        List<Vector3d> points = [];
        foreach (double sx in new[] { -0.5, 0.5 })
        {
            foreach (double sy in new[] { -0.5, 0.5 })
            {
                foreach (double sz in new[] { -0.5, 0.5 })
                {
                    points.Add(new Vector3d(sx * x, sy * y, sz * z));
                }
            }
        }

        return ConvexHull.Build(points)!;
    }

    private static CollisionTriangleGather World(params CollisionTriangle[] triangles) =>
        (Vector3d min, Vector3d max, List<CollisionTriangle> into) =>
        {
            foreach (CollisionTriangle triangle in triangles)
            {
                if (triangle.Overlaps(min, max))
                {
                    into.Add(triangle);
                }
            }
        };

    private static CollisionTriangle[] Plane(double z, double half = 20 * Metre) =>
    [
        new(new Vector3d(-half, -half, z), new Vector3d(half, -half, z), new Vector3d(half, half, z)),
        new(new Vector3d(-half, -half, z), new Vector3d(half, half, z), new Vector3d(-half, half, z)),
    ];

    private static Matrix3f Tilted() =>
        Matrix3f.RotationX(0.6f) * Matrix3f.RotationY(0.35f) * Matrix3f.RotationZ(0.2f);

    private static double LowestZ(RigidMeshBody body, ConvexHull hull)
    {
        Matrix3f o = body.Orientation;
        double lowest = double.MaxValue;
        foreach (Vector3d v in hull.Vertices)
        {
            Vector3f w = o.Transposed().Transform(new Vector3f((float)v.X, (float)v.Y, (float)v.Z));
            lowest = Math.Min(lowest, body.Position.Z + w.Z);
        }

        return lowest;
    }

    private static int Run(RigidMeshBody body, CollisionTriangleGather world, int maximumTicks = 600)
    {
        int tick = 0;
        while (!body.IsAtRest && tick < maximumTicks)
        {
            body.Step(world);
            tick++;
        }

        return tick;
    }

    [Fact]
    public void AHullCarriesTheExactMassPropertiesOfItsSolid()
    {
        ConvexHull box = Box(4.0, 2.0, 1.0);

        Assert.Equal(8.0, box.Volume, 9);
        Assert.Equal(0.0, box.Centroid.Length, 9);

        // A solid box of unit mass: I = (b² + c²) / 12 about each axis.
        Assert.Equal((4.0 + 1.0) / 12.0, box.UnitInertia.M11, 9);
        Assert.Equal((16.0 + 1.0) / 12.0, box.UnitInertia.M22, 9);
        Assert.Equal((16.0 + 4.0) / 12.0, box.UnitInertia.M33, 9);
        Assert.Equal(0.0, box.UnitInertia.M12, 9);
    }

    [Fact]
    public void AHullOfACloudKeepsOnlyItsOutsideAndBoundsItsVertices()
    {
        Random random = new(7);
        List<Vector3d> cloud = [];
        for (int i = 0; i < 4000; i++)
        {
            Vector3d p = new(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5);
            cloud.Add(p.Normalized() * (1000.0 * (0.2 + (0.8 * random.NextDouble()))));
        }

        ConvexHull hull = ConvexHull.Build(cloud, maximumVertices: 48)!;

        Assert.InRange(hull.Vertices.Length, 4, 48);
        foreach (Vector3d point in hull.Vertices)
        {
            foreach (HullFace face in hull.Faces)
            {
                Assert.True(Vector3d.Dot(face.Normal, point) - face.Offset < 1e-6);
            }
        }
    }

    /// <summary>A slab dropped tilted lands, tips and lies on its broad side, resting on the surface.</summary>
    [Fact]
    public void ADroppedGunSettlesFlatOnTheFloor()
    {
        ConvexHull hull = Rifle();
        RigidMeshBody body = new(hull, 3.5, new Vector3d(0, 0, 1.2 * Metre), Tilted(), default, new Vector3d(0.02, -0.04, 0.03));

        int ticks = Run(body, World(Plane(0)));

        Assert.True(body.IsAtRest, "never came to rest");
        output.WriteLine($"rested after {ticks} ticks ({ticks / 30.0:0.00} s)");
        Assert.InRange(LowestZ(body, hull), -RigidMeshBody.PenetrationSlop - 2.0, 2.0);

        // Its thinnest axis stands vertical: it lies on its broad side, as a real rifle does.
        Matrix3f o = body.Orientation;
        Vector3f thin = o.Transposed().Transform(new Vector3f(0f, 1f, 0f));
        Assert.True(Math.Abs(thin.Z) > 0.97f, $"thin axis leans {thin}");
    }

    /// <summary>Contacts only ever take energy out; they never throw a body harder than it fell.</summary>
    [Fact]
    public void EnergyNeverGrows()
    {
        ConvexHull hull = Rifle();
        const double mass = 3.5;
        RigidMeshBody body = new(hull, mass, new Vector3d(0, 0, 1.5 * Metre), Tilted(), new Vector3d(40, 10, 0), new Vector3d(0.1, 0.05, -0.08));
        CollisionTriangleGather world = World(Plane(0));

        double previous = Total();
        for (int tick = 0; tick < 300 && !body.IsAtRest; tick++)
        {
            body.Step(world);
            double now = Total();

            // A small allowance for the integration of the fall within a substep.
            Assert.True(now <= previous + (mass * RigidMeshBody.GravityPerTick * 30.0), $"tick {tick}: {previous} -> {now}");
            previous = now;
        }

        double Total() => body.KineticEnergy + (mass * RigidMeshBody.GravityPerTick * body.CentreOfMass.Z);
    }

    /// <summary>A gun thrown straight down at 30 m/s does not pass through a floor with no thickness.</summary>
    [Fact]
    public void AFastBodyDoesNotTunnelThroughAThinFloor()
    {
        ConvexHull hull = Rifle();
        Vector3d down = new(0, 0, -30.0 * Metre / RigidMeshBody.TicksPerSecond);
        RigidMeshBody body = new(hull, 3.5, new Vector3d(0, 0, 3 * Metre), Tilted(), down, default);

        for (int tick = 0; tick < 120; tick++)
        {
            body.Step(World(Plane(0)));
            Assert.True(LowestZ(body, hull) > -100.0, $"tick {tick}: passed through at {LowestZ(body, hull)}");
        }
    }

    /// <summary>Friction 0.55 holds a gun on a 20° slope and cannot on a 45° one.</summary>
    [Theory]
    [InlineData(20.0, false)]
    [InlineData(45.0, true)]
    public void ASlopeSteeperThanFrictionHoldsIsSlidDown(double degrees, bool slides)
    {
        double angle = degrees * Math.PI / 180.0;
        double half = 20 * Metre;
        double rise = Math.Tan(angle) * half;

        // A plane rising towards +X.
        CollisionTriangle[] slope =
        [
            new(new Vector3d(-half, -half, -rise), new Vector3d(half, -half, rise), new Vector3d(half, half, rise)),
            new(new Vector3d(-half, -half, -rise), new Vector3d(half, half, rise), new Vector3d(-half, half, -rise)),
        ];

        // Laid flat on its broad side, just above the slope and aligned with it.
        Matrix3f lying = Matrix3f.RotationX((float)(Math.PI / 2)) * Matrix3f.RotationY((float)-angle);
        ConvexHull hull = Rifle();
        RigidMeshBody body = new(hull, 3.5, new Vector3d(0, 0, 0.12 * Metre / Math.Cos(angle)), lying, default, default);

        double startX = body.CentreOfMass.X;
        Run(body, World(slope), maximumTicks: 90);
        double travelled = startX - body.CentreOfMass.X;
        output.WriteLine($"{degrees} degrees: slid {travelled / Metre:0.00} m, resting {body.IsAtRest}");

        if (slides)
        {
            Assert.True(travelled > 0.5 * Metre, $"only slid {travelled / Metre:0.00} m");
        }
        else
        {
            Assert.True(travelled < 0.05 * Metre, $"slid {travelled / Metre:0.00} m");
        }
    }

    /// <summary>A rifle landing across the edge of a platform stays on it when its weight is over it.</summary>
    [Fact]
    public void ALedgeHoldsAGunWhoseWeightIsOverIt()
    {
        double half = 10 * Metre;
        CollisionTriangle[] platform =
        [
            new(new Vector3d(-half, -half, 0), new Vector3d(0, -half, 0), new Vector3d(0, half, 0)),
            new(new Vector3d(-half, -half, 0), new Vector3d(0, half, 0), new Vector3d(-half, half, 0)),
            .. Plane(-2 * Metre),
        ];

        ConvexHull hull = Rifle();
        Matrix3f lying = Matrix3f.RotationX((float)(Math.PI / 2));
        RigidMeshBody body = new(hull, 3.5, new Vector3d(-0.15 * Metre, 0, 0.3 * Metre), lying, default, default);

        Run(body, World(platform));

        Assert.True(body.IsAtRest);
        Assert.True(body.CentreOfMass.Z > -0.2 * Metre, $"fell off the ledge to {body.CentreOfMass.Z / Metre:0.00} m");
    }

    /// <summary>
    /// A slab dropped flat from a metre bounces, but only to a small fraction of that: steel on a hard
    /// floor gives back about 0.3 of its speed, so under a tenth of its height.
    /// </summary>
    [Fact]
    public void AHardLandingBouncesALittle()
    {
        ConvexHull hull = Rifle();
        Matrix3f lying = Matrix3f.RotationX((float)(Math.PI / 2));
        RigidMeshBody body = new(hull, 3.5, new Vector3d(0, 0, 1.0 * Metre), lying, default, default);
        CollisionTriangleGather world = World(Plane(0));

        // The landing happens mid-tick, so it is found by the fall turning into a rise.
        bool landed = false;
        double apex = 0.0;
        for (int tick = 0; tick < 120; tick++)
        {
            double before = body.Velocity.Z;
            body.Step(world);
            landed |= before < 0.0 && body.Velocity.Z > 0.0;
            if (landed)
            {
                apex = Math.Max(apex, LowestZ(body, hull));
            }
        }

        output.WriteLine($"bounce apex {apex / Metre * 100:0.0} cm");
        Assert.InRange(apex, 0.01 * Metre, 0.15 * Metre);
    }

    /// <summary>Settling a tumbling drop over a detailed floor costs a fraction of a millisecond a tick.</summary>
    [Fact]
    public void ADropIsCheap()
    {
        // A 400-triangle floor under the drop, as a detailed building floor would be.
        List<CollisionTriangle> floor = [];
        double cell = 0.25 * Metre;
        for (int x = -5; x < 5; x++)
        {
            for (int y = -10; y < 10; y++)
            {
                Vector3d a = new(x * cell, y * cell, 0), b = new((x + 1) * cell, y * cell, 0);
                Vector3d c = new((x + 1) * cell, (y + 1) * cell, 0), d = new(x * cell, (y + 1) * cell, 0);
                floor.Add(new CollisionTriangle(a, b, c));
                floor.Add(new CollisionTriangle(a, c, d));
            }
        }

        ConvexHull hull = Rifle();
        CollisionTriangleGather world = World([.. floor]);
        RigidMeshBody warm = new(hull, 3.5, new Vector3d(0, 0, 1.2 * Metre), Tilted(), default, new Vector3d(0.05, 0, 0));
        Run(warm, world);

        // The best of five, so a busy test runner measures the work rather than the scheduler.
        double perTick = double.MaxValue;
        int ticks = 0;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            RigidMeshBody body = new(hull, 3.5, new Vector3d(0, 0, 1.2 * Metre), Tilted(), default, new Vector3d(0.05, 0, 0));
            Stopwatch clock = Stopwatch.StartNew();
            ticks = Run(body, world);
            clock.Stop();
            perTick = Math.Min(perTick, clock.Elapsed.TotalMilliseconds / Math.Max(ticks, 1));
        }

        output.WriteLine($"{ticks} ticks, {perTick:0.000} ms per tick");
        // About a third of a millisecond on an idle machine. The budget is there to catch a cost that has grown
        // by an order of magnitude, and allows for a CI machine running two builds on the cores this shares.
        Assert.True(perTick < 6.0, $"{perTick:0.000} ms per tick");
    }
}
