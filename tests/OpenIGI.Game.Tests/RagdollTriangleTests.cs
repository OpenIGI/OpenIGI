using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A corpse meets the level's real surfaces: it lies on a crate and across a staircase instead of sinking
/// into them, which the ground probe and the wall rays alone could not describe.
/// </summary>
public sealed class RagdollTriangleTests
{
    private const double StepRise = 737.0;
    private const double StepRun = 1229.0;
    private const int Steps = 6;

    /// <summary>A body dropped onto a crate comes to rest on top of it, not inside it.</summary>
    [Fact]
    public void ABodyLandsOnACrate()
    {
        (Vector3d Low, Vector3d High)[] solids = [(new(-2_000.0, -2_000.0, 0.0), new(2_000.0, 2_000.0, 2_400.0))];
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 7_000.0));

        Settle(body, World(solids));

        AssertOutside(body, solids);

        // And something of it is lying ON the lid: a capsule whose underside sits on the crate's top,
        // over the crate. A body that fell straight through would have none.
        bool onLid = false;
        for (int part = 0; part < body.BodyCount; part++)
        {
            body.CapsuleOf(part, out Vector3d from, out Vector3d to, out double radius);
            Vector3d lowest = from.Z < to.Z ? from : to;
            onLid |= Math.Abs(lowest.X) < 2_000.0 && Math.Abs(lowest.Y) < 2_000.0
                && Math.Abs(lowest.Z - radius - 2_400.0) < RagdollBody.PenetrationSlop * 4.0;
        }

        Assert.True(onLid, "Nothing of the body is resting on the crate's lid.");
    }

    /// <summary>A body falling across a staircase lies on the steps, with nothing sunk into them.</summary>
    [Fact]
    public void ABodyLiesAcrossStairs()
    {
        (Vector3d Low, Vector3d High)[] solids = new (Vector3d, Vector3d)[Steps];
        for (int step = 0; step < Steps; step++)
        {
            solids[step] = (
                new Vector3d(-4_000.0, step * StepRun, 0.0),
                new Vector3d(4_000.0, Steps * StepRun, (step + 1) * StepRise));
        }

        RagdollSkeleton rig = RagdollRig.Rig();

        // Face up across the flight, head uphill, dropped from above its middle.
        Matrix3f onBack = OpenIGI.Formats.Animation.BonePose.ToMatrix(
            Quaternion32.FromAxisAngle(Vector3f.UnitX, -MathF.PI / 2f));
        RagdollBody body = RagdollBody.Seed(
            rig, RagdollRig.Standing(), new Vector3d(0.0, 2_000.0, 6_000.0), onBack);

        Settle(body, World(solids));

        AssertOutside(body, solids);
    }

    /// <summary>A body thrown at a crate's side stops against it rather than passing into it.</summary>
    [Fact]
    public void ABodyThrownAtACrateStopsAtItsSide()
    {
        (Vector3d Low, Vector3d High)[] solids = [(new(-4_000.0, 4_000.0, 0.0), new(4_000.0, 8_000.0, 6_000.0))];
        RagdollSkeleton rig = RagdollRig.Rig();
        RagdollBody body = RagdollRig.Standing(rig, new Vector3d(0.0, 0.0, 2_400.0));
        body.AddVelocity(new Vector3d(0.0, 300.0, 0.0));

        Settle(body, World(solids));

        AssertOutside(body, solids);
    }

    private static void Settle(RagdollBody body, RagdollWorld world)
    {
        for (int tick = 0; tick < RagdollBody.MaximumActiveTicks && !body.IsAtRest; tick++)
        {
            body.Step(world, substeps: 8);
        }

        Assert.True(body.IsAtRest, "The body never came to rest.");
    }

    // No capsule may reach further into any solid than the contact's deadband and a little give.
    private static void AssertOutside(RagdollBody body, (Vector3d Low, Vector3d High)[] solids)
    {
        const double Give = RagdollBody.PenetrationSlop * 3.0;
        for (int part = 0; part < body.BodyCount; part++)
        {
            body.CapsuleOf(part, out Vector3d from, out Vector3d to, out double radius);
            double inset = Math.Max(0.0, radius - Give);
            for (int sample = 0; sample <= 8; sample++)
            {
                Vector3d at = from + ((to - from) * (sample / 8.0));
                foreach ((Vector3d low, Vector3d high) in solids)
                {
                    bool inside = at.X > low.X - inset && at.X < high.X + inset
                        && at.Y > low.Y - inset && at.Y < high.Y + inset
                        && at.Z > low.Z - inset && at.Z < high.Z + inset
                        && Distance(at, low, high) < inset;
                    Assert.False(
                        inside,
                        $"Body {part} reaches {inset - Distance(at, low, high):F0} units into a solid at ({at.X:F0}, {at.Y:F0}, {at.Z:F0}).");
                }
            }
        }
    }

    // How far a point is from a box: zero inside it.
    private static double Distance(Vector3d point, Vector3d low, Vector3d high)
    {
        double dx = Math.Max(Math.Max(low.X - point.X, 0.0), point.X - high.X);
        double dy = Math.Max(Math.Max(low.Y - point.Y, 0.0), point.Y - high.Y);
        double dz = Math.Max(Math.Max(low.Z - point.Z, 0.0), point.Z - high.Z);
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    // A flat floor at zero from the ground probe, and the solids only as triangles -- so anything the
    // body rests on above the floor it found through the triangle contacts.
    private static RagdollWorld World((Vector3d Low, Vector3d High)[] solids)
    {
        List<CollisionTriangle> triangles = [];
        foreach ((Vector3d low, Vector3d high) in solids)
        {
            Box(low, high, triangles);
        }

        return new RagdollWorld(ground: (double x, double y, double z, out double groundZ) =>
        {
            _ = x;
            _ = y;
            _ = z;
            groundZ = 0.0;
            return true;
        })
        {
            Triangles = (min, max, into) =>
            {
                foreach (CollisionTriangle triangle in triangles)
                {
                    if (triangle.Overlaps(min, max))
                    {
                        into.Add(triangle);
                    }
                }
            },
        };
    }

    private static void Box(Vector3d low, Vector3d high, List<CollisionTriangle> into)
    {
        Vector3d Corner(int i) => new(
            (i & 1) == 0 ? low.X : high.X,
            (i & 2) == 0 ? low.Y : high.Y,
            (i & 4) == 0 ? low.Z : high.Z);

        int[][] faces =
        [
            [0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1], [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3],
        ];
        foreach (int[] face in faces)
        {
            into.Add(new CollisionTriangle(Corner(face[0]), Corner(face[1]), Corner(face[2])));
            into.Add(new CollisionTriangle(Corner(face[0]), Corner(face[2]), Corner(face[3])));
        }
    }
}
