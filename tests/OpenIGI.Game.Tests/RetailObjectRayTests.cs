using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using Xunit;
using Xunit.Abstractions;
using OpenIGI.TestSupport;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The model half of the ray query against the captured retail wheel rays that struck a model: mission 7's T80 357
/// spawns inside a second T80 (car 361) and later drives along a spline road.
/// </summary>
public sealed class RetailObjectRayTests(ITestOutputHelper output)
{
    [RequiresGameDataTheory]
    [InlineData("carstep-m7-t80-357.log.gz", 571)]
    [InlineData("carstep-m7-t80-357-safe.log.gz", 2284)]
    public void EveryCapturedModelHitIsReproducedBitForBit(string fixture, int rays)
    {
        RetailMissionData data = RetailMissionData.Get(7);
        List<object> objects = [.. Objects(data)];
        RetailCarStepCapture capture = RetailCarStepCapture.Load(fixture);
        int checkedRays = 0;
        List<string> failures = [];
        foreach (RetailCarStepCapture.Tick tick in capture.Ticks)
        {
            byte[] o = tick.RayResults;
            Vector3d[] starts = new Vector3d[tick.RayCount], ends = new Vector3d[tick.RayCount];
            RetailRayResult[] results = new RetailRayResult[tick.RayCount];
            for (int k = 0; k < tick.RayCount; k++)
            {
                starts[k] = Vec(tick.RayStarts, 24 * k);
                ends[k] = Vec(tick.RayEnds, 24 * k);
                results[k] = new RetailRayResult { Point = ends[k] };
            }

            // As the world casts: a model ray by ray, a segment over the whole batch (it stops at the first ray
            // outside its box).
            foreach (object candidate in objects)
            {
                if (candidate is RetailSplineSegment segment)
                {
                    segment.Cast(starts, ends, results);
                    continue;
                }

                for (int k = 0; k < tick.RayCount; k++)
                {
                    if (candidate is RetailRayModel model)
                    {
                        RetailModelRay.Cast(model, starts[k], ends[k], ref results[k]);
                    }
                }
            }

            for (int k = 0; k < tick.RayCount; k++)
            {
                int r = 112 * k;
                if (BitConverter.ToInt32(o, r) == 0)
                {
                    continue;
                }

                RetailRayResult result = results[k];
                checkedRays++;
                List<string> diff = [];
                Same(diff, "hit", result.Hit ? 1 : 0, BitConverter.ToInt32(o, r + 88));
                Same(diff, "x", result.Point.X, BitConverter.ToDouble(o, r + 8));
                Same(diff, "y", result.Point.Y, BitConverter.ToDouble(o, r + 16));
                Same(diff, "z", result.Point.Z, BitConverter.ToDouble(o, r + 24));
                Same(diff, "nx", result.NormalX, BitConverter.ToSingle(o, r + 56));
                Same(diff, "ny", result.NormalY, BitConverter.ToSingle(o, r + 60));
                Same(diff, "nz", result.NormalZ, BitConverter.ToSingle(o, r + 64));
                Same(diff, "front", result.HasFrontMaterial ? 1 : 0, BitConverter.ToInt32(o, r + 76) != 0 ? 1 : 0);
                Same(diff, "back", result.HasSurface ? 1 : 0, BitConverter.ToInt32(o, r + 80) != 0 ? 1 : 0);
                Same(diff, "distance", result.Distance, BitConverter.ToDouble(o, r + 96));
                if (diff.Count != 0)
                {
                    failures.Add($"tick {tick.Sequence} ray {k}: {string.Join("; ", diff)}");
                }
            }
        }

        foreach (string failure in failures.Take(10))
        {
            output.WriteLine(failure);
        }

        Assert.Equal(rays, checkedRays);
        Assert.True(failures.Count == 0, $"{failures.Count} of {checkedRays} differ; first: {failures.FirstOrDefault()}");
    }

    private IEnumerable<object> Objects(RetailMissionData data)
    {
        // Car 361, standing where the level puts it.
        DeclaredTask car = Assert.IsType<DeclaredTask>(data.Root.Tree.FromId(361));
        yield return Model(data, car, Text(car, WorldScene.ModelParameter));

        // Every segment of the level's spline chains: each waypoint names the model laid from the previous one.
        foreach (DeclaredTask spline in Walk(data.Root).OfType<DeclaredTask>().Where(t => t.Type.Name == WorldScene.SplineObjType))
        {
            List<DeclaredTask> waypoints = [.. spline.Children.OfType<DeclaredTask>().Where(t => t.Type.Name == WorldScene.SplineWaypointType)];
            if (waypoints.Count > 0)
            {
                output.WriteLine("waypoint parameters: " + string.Join(", ", Enumerable.Range(0, waypoints[0].Type.Parameters.Count)
                    .Select(i => $"{waypoints[0].Type.Parameters[i].Name}={waypoints[0].GetValue(i)}")));
            }

            for (int i = 1; i < waypoints.Count; i++)
            {
                if (Segment(data, waypoints[i - 1], waypoints[i]) is { } segment)
                {
                    yield return segment;
                }
            }
        }
    }

    private static RetailRayModel Model(RetailMissionData data, DeclaredTask task, string modelName)
    {
        Assert.True(data.Mission.TryGetModel(modelName, out MefModel? model));
        Assert.True(MefCollisionMesh.TryRead(model!.Container, meshIndex: 0, out MefCollisionMesh? mesh));
        RetailRayModel placed = new(task, new RetailRayMesh(mesh!), model.NativeMarkerBound);
        Vector3d position = Assert.IsType<Vector3d>(Value(task, "Position"));
        Vector3f angles = Assert.IsType<Vector3f>(Value(task, "Orientation"));
        placed.Position[0] = position.X;
        placed.Position[1] = position.Y;
        placed.Position[2] = position.Z;
        RetailSplineSegment.AnglesToMatrix(angles.X, angles.Y, angles.Z).CopyTo(placed.M, 0);
        return placed;
    }

    private static RetailSplineSegment? Segment(RetailMissionData data, DeclaredTask previous, DeclaredTask waypoint)
    {
        string name = Text(waypoint, WorldScene.SegmentModelParameter);
        if (name.Length == 0 || !data.Mission.TryGetModel(name, out MefModel? model) || model is null
            || !MefCollisionMesh.TryRead(model.Container, meshIndex: 0, out MefCollisionMesh? level0) || level0 is null)
        {
            return null;
        }

        List<RetailRayMesh> levels = [];
        RetailRayMesh mesh = new(level0);
        levels.Add(mesh);
        if (MefCollisionMesh.TryRead(model.Container, meshIndex: 1, out MefCollisionMesh? level1) && level1 is not null)
        {
            levels.Add(new RetailRayMesh(level1));
        }

        // The x extent: every LOD's render vertices, from 0.
        float x0 = 0f, x1 = 0f;
        string stem = name[..^1];
        for (int lod = 1; lod <= 9; lod++)
        {
            if (!data.Mission.TryGetModel(stem + lod, out MefModel? level) || level is null)
            {
                break;
            }

            foreach (MefVertex vertex in level.Vertices)
            {
                x0 = vertex.Position.X < x0 ? vertex.Position.X : x0;
                x1 = vertex.Position.X > x1 ? vertex.Position.X : x1;
            }
        }

        Vector3d p0 = Assert.IsType<Vector3d>(Value(previous, "Position"));
        Vector3d p1 = Assert.IsType<Vector3d>(Value(waypoint, "Position"));
        Vector3f a0 = Assert.IsType<Vector3f>(Value(previous, "Orientation"));
        Vector3f a1 = Assert.IsType<Vector3f>(Value(waypoint, "Orientation"));
        // 0x502250 binds NumAreas at +0x198, Align at +0x1A1 (the unbent flag) and Flip at +0x1A4 (reverse).
        Assert.False(Value(waypoint, "Align") is true);
        int count = Assert.IsType<int>(Value(waypoint, "NumAreas"));
        return RetailSplineSegment.Build(
            waypoint,
            mesh,
            levels,
            p0,
            p1,
            RetailSplineSegment.AnglesToMatrix(a0.X, a0.Y, a0.Z),
            RetailSplineSegment.AnglesToMatrix(a1.X, a1.Y, a1.Z),
            count,
            x0,
            x1,
            reverse: Value(waypoint, "Flip") is true);
    }

    private static object? Value(DeclaredTask task, string name) => task.TryGetValue(name, out object? value) ? value : null;

    private static string Text(DeclaredTask task, string name) => Value(task, name) as string ?? string.Empty;

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;
            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private static Vector3d Vec(byte[] b, int offset) =>
        new(BitConverter.ToDouble(b, offset), BitConverter.ToDouble(b, offset + 8), BitConverter.ToDouble(b, offset + 16));

    private static void Same(List<string> diff, string name, double port, double retail)
    {
        if (BitConverter.DoubleToInt64Bits(port) != BitConverter.DoubleToInt64Bits(retail))
        {
            diff.Add($"{name}: port {port:R} retail {retail:R}");
        }
    }

    private static void Same(List<string> diff, string name, float port, float retail)
    {
        if (BitConverter.SingleToInt32Bits(port) != BitConverter.SingleToInt32Bits(retail))
        {
            diff.Add($"{name}: port {port:R} retail {retail:R}");
        }
    }
}
