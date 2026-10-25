using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Straight spline segments, <c>SplineObjSegment_Update</c> and the linear path of <c>SplineObjSegment_TraceLinesHandler</c>.</summary>
public sealed class RetailIgi2SplineSegmentTests
{
    private const string FenceModel = "303_02_1";

    private static readonly Lazy<Igi2CollisionWorld> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        return Igi2CollisionWorld.Build(level.Scene, level.ModelFor, null, "terrain");
    });

    private static readonly Lazy<Igi2CollisionMesh> Fence = new(static () =>
    {
        MefModel model = Igi2Level.Find("location1", "level1").ModelFor(FenceModel)!;
        Assert.True(MefCollisionMesh.TryRead(model.Container, 0, out MefCollisionMesh? mesh, AssetFormatProfile.Igi2.Collision));
        return new Igi2CollisionMesh(mesh!);
    });

    /// <summary><c>Real32x9_InvertSafe</c> of a turn is the turn itself: the cofactors over the determinant, transposed back.</summary>
    [Fact]
    public void The_inverse_of_a_turn_transposed_is_the_turn()
    {
        float[] turn = Igi2Pose.Rotation(0.3f, -0.2f, 1.1f);

        float[] result = Igi2Pose.InverseTranspose(turn);

        for (int i = 0; i < 9; i++)
        {
            Assert.Equal(turn[i], result[i], 5);
        }

        Assert.Equal([1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], Igi2Pose.InverseTranspose([1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f]));
    }

    /// <summary><c>Real32x3_GetAngles</c> reports no turn for a direction with no X, rather than a quarter turn.</summary>
    [Fact]
    public void A_direction_with_no_x_has_no_turn()
    {
        Assert.Equal(0f, Igi2Pose.DirectionGamma(new Vector3f(0f, 5f, 1f)));
        Assert.Equal(MathF.PI / 4f, Igi2Pose.DirectionGamma(new Vector3f(3f, 3f, 0f)), 6);
    }

    /// <summary>
    /// The segment stands at its waypoints' midpoint as a 24-bit FPU forms it: the sum truncated to a quarter unit
    /// before it is halved.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_midpoint_is_summed_at_24_bits()
    {
        Igi2SplineSegment? segment = Igi2SplineSegment.Straight(
            "owner",
            new Vector3d(1000000.3, 0.0, 0.0),
            new Vector3d(3000000.1, 0.0, 4096.0),
            isFlipped: false,
            [Fence.Value],
            modelRadius: 1f);

        Assert.NotNull(segment);
        Assert.Equal(2000000.125, segment.Position.X);
        Assert.Equal(2048.0, segment.Position.Z);
    }

    /// <summary>
    /// The stretch's first column is the chord over the model's X extent, reversed when flipped, its second the
    /// chord's turn; the inverse undoes it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_stretch_carries_the_model_along_the_chord()
    {
        Vector3d start = new(0.0, 0.0, 0.0), stop = new(8192.0, 0.0, 0.0);
        (float low, float high) = Extent(Fence.Value);

        Igi2SplineSegment segment = Igi2SplineSegment.Straight("owner", start, stop, isFlipped: false, [Fence.Value], 1f)!;
        Igi2SplineSegment flipped = Igi2SplineSegment.Straight("owner", start, stop, isFlipped: true, [Fence.Value], 1f)!;

        float along = 8192f / (high - low);
        Assert.Equal((low + high) * 0.5f, segment.Middle, 1);
        Assert.Equal(along, segment.Orientation[0], 2);
        Assert.Equal(-along, flipped.Orientation[0], 2);
        Assert.Equal([0f, 1f, 0f], [segment.Orientation[1], segment.Orientation[4], segment.Orientation[7]]);
        Assert.Equal(1f / along, segment.ToMesh[0], 5);

        // The radius: the model's, times the longest row, times 1.1. Here the chord is shorter than the model, and the
        // longest row is a cross-section's unit one.
        Assert.Equal(MathF.Max(along, 1f) * 1.1f, segment.Radius, 5);
    }

    /// <summary>Coincident waypoints leave the stretch undefined, so no segment is built.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Coincident_waypoints_build_nothing()
    {
        Vector3d at = new(10.0, 20.0, 30.0);

        Assert.Null(Igi2SplineSegment.Straight("owner", at, at, isFlipped: false, [Fence.Value], 1f));
    }

    /// <summary>A model's levels of detail chain by its last character while the names resolve, five at most.</summary>
    [Fact]
    public void Levels_of_detail_chain_by_the_last_character()
    {
        HashSet<string> known = ["fence_1", "fence_2", "fence_3"];

        Assert.Equal(["fence_1", "fence_2", "fence_3"], Igi2SplineSegment.LevelsOfDetail("fence_1", known.Contains));
        Assert.Equal(5, Igi2SplineSegment.LevelsOfDetail("a1", static _ => true).Count());
    }

    /// <summary>
    /// Mission 2's curved segments are bent colliders (<c>SplineObjSegment_TransformVertex</c>): a short line through the middle
    /// of every bent face, along its normal, is stopped by the segment no later than the face and the tie window behind it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_2_curves_are_bent_colliders()
    {
        Igi2Level level = Igi2Level.Find("location1", "level2");
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, null, "terrain");
        Igi2SplineSegment[] bent = [.. world.Segments.Where(static s => s.IsBent)];
        Assert.Equal(43, bent.Length);

        int faces = 0, struck = 0;
        foreach (Igi2SplineSegment segment in bent)
        {
            Assert.True(segment.Radius >= 1f);
            Igi2CollisionMesh mesh = segment.Mesh(0)!;
            float[] v = mesh.Arrays.Vertices;
            int[] f = mesh.Arrays.Faces;
            for (int face = 0; face < mesh.Arrays.FaceCount; face++)
            {
                int i0 = 3 * f[4 * face], i1 = 3 * f[(4 * face) + 1], i2 = 3 * f[(4 * face) + 2];
                Vector3d a = new(v[i0], v[i0 + 1], v[i0 + 2]), b = new(v[i1], v[i1 + 1], v[i1 + 2]), c = new(v[i2], v[i2 + 1], v[i2 + 2]);
                Vector3d n = Vector3d.Cross(b - a, c - a);
                if (n.Length < 1.0)
                {
                    continue;
                }

                n /= n.Length;
                Vector3d middle = ((a + b + c) / 3.0) + segment.Position;
                WorldLineTrace trace = new(1);
                trace.Starts[0] = middle + (n * 64.0);
                trace.Ends[0] = middle - (n * 64.0);
                Igi2SplineSegmentTrace.Trace(segment, trace);
                faces++;
                if (trace.Intersections[0] is { IsHit: true } hit)
                {
                    Assert.Same(segment, hit.HitObject);
                    Assert.InRange(hit.Distance, 0.0, 64.0 + 40.96 + 1.0);
                    struck++;
                }
            }
        }

        Assert.True(struck >= faces * 0.98, $"only {struck} of {faces} bent faces stopped a line through them.");
    }

    /// <summary>Mission 1's three fences are straight runs; every segment of them is a collider.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_1_fences_become_colliders()
    {
        IReadOnlyList<Igi2SplineSegment> segments = Mission1.Value.Segments;

        Assert.Equal(14, segments.Count);
        Assert.All(segments, static s => Assert.True(s.Radius > 0f));
    }

    /// <summary>A level line run across a fence at its middle, at the height of its mesh's middle, stops at it.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_line_across_a_fence_stops_at_it()
    {
        Igi2CollisionWorld world = Mission1.Value;
        int struck = 0;
        foreach (Igi2SplineSegment segment in world.Segments)
        {
            // The model's Y runs along the stretch's second column, across the fence; its Z straight up.
            Vector3f across = new(segment.Orientation[1], segment.Orientation[4], segment.Orientation[7]);
            double height = MeshMiddleZ(segment);
            Vector3d middle = new(segment.Position.X, segment.Position.Y, segment.Position.Z + height);
            WorldLineTrace trace = new(1);
            trace.Starts[0] = new Vector3d(middle.X - (across.X * 8192.0), middle.Y - (across.Y * 8192.0), middle.Z);
            trace.Ends[0] = new Vector3d(middle.X + (across.X * 8192.0), middle.Y + (across.Y * 8192.0), middle.Z);

            world.TraceLines(trace, Igi2CollisionWorld.SplineGroup);

            WorldIntersection hit = trace.Intersections[0];
            if (hit.IsHit)
            {
                Assert.Same(segment, hit.HitObject);
                Assert.True(hit.IsDoubleFace);
                Assert.InRange(hit.Distance, 0.0, 16384.0 + 1.0);
                struck++;
            }
        }

        Assert.True(struck >= 10, $"only {struck} of {world.Segments.Count} fence segments stopped a line.");
    }

    /// <summary>A ground query down onto a fence's middle finds it below the start, and no farther than the line reaches.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_ground_query_finds_the_fence_top()
    {
        Igi2CollisionWorld world = Mission1.Value;
        int struck = 0;
        foreach (Igi2SplineSegment segment in world.Segments)
        {
            WorldLineTrace trace = new(1);
            trace.Starts[0] = new Vector3d(segment.Position.X, segment.Position.Y, segment.Position.Z + 40960.0);
            world.NextZ(trace, [-81920.0], Igi2CollisionWorld.SplineGroup);

            WorldIntersection hit = trace.Intersections[0];
            if (hit.IsHit && hit.HitObject is Igi2SplineSegment)
            {
                Assert.InRange(hit.Position.Z, segment.Position.Z - 40960.0, segment.Position.Z + 40960.0);
                struck++;
            }
        }

        Assert.True(struck > 0, "no fence was found from above.");
    }

    private static double MeshMiddleZ(Igi2SplineSegment segment)
    {
        float[] v = segment.Mesh(0)!.Arrays.Vertices;
        float low = float.MaxValue, high = float.MinValue;
        for (int i = 2; i < v.Length; i += 3)
        {
            low = MathF.Min(low, v[i]);
            high = MathF.Max(high, v[i]);
        }

        return (low + high) * 0.5;
    }

    /// <summary>The lowest and highest X of a mesh's vertices, both from zero, as the update measures them.</summary>
    private static (float Low, float High) Extent(Igi2CollisionMesh mesh)
    {
        float low = 0f, high = 0f;
        float[] v = mesh.Arrays.Vertices;
        for (int i = 0; i < v.Length; i += 3)
        {
            low = MathF.Min(low, v[i]);
            high = MathF.Max(high, v[i]);
        }

        return (low, high);
    }
}
