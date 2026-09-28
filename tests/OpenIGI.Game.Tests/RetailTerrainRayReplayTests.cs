using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Drives the captured retail cars with the port's own wheel rays -- <see cref="RetailTerrainRay"/> over the
/// level's real terrain -- and demands both the original's ray results and its car state back, bit for bit.
/// </summary>
public sealed class RetailTerrainRayReplayTests(ITestOutputHelper output)
{
        public static TheoryData<int, string> Fixtures => new()
    {
        { 7, "carstep-m7-apc352.log.gz" },
        { 7, "carstep-m7-t80-357.log.gz" },
        { 6, "carstep-m6-car801.log.gz" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheCarDrivesOnThePortsOwnTerrainRaysExactlyAsRetail(int mission, string fixture)
    {
        RetailCarStepCapture capture = RetailCarStepCapture.Load(fixture);
        RetailCarBody car = new(RetailCarMemory.Definition(capture.TypeMemory, capture.RecordMemory));
        RetailCarMemory.Load(car, capture.Ticks[0].Entry);
        Drive(car, capture, mission);
    }

    [Theory]
    [InlineData(7, 352, "carstep-m7-apc352.log.gz")]
    [InlineData(7, 357, "carstep-m7-t80-357.log.gz")]
    [InlineData(6, 801, "carstep-m6-car801.log.gz")]
    public void ACarSpawnedFromTheLevelDrivesTheWholeCaptureExactlyAsRetail(int mission, int id, string fixture)
    {
        // Nothing from the capture but the controller's packets: the car is built from the level, its type
        // and its model, and every ray is the port's own.
        RetailMissionData data = RetailMissionData.Get(mission);
        DeclaredTask task = Assert.IsType<DeclaredTask>(data.Root.Tree.FromId(id));
        string modelName = Assert.IsType<string>(task.TryGetValue(WorldScene.ModelParameter, out object? value) ? value : null);
        Assert.True(data.Vehicles.TryGetCar(modelName, out CarPhysicsDefinition physics));
        Assert.True(data.Mission.TryGetModel(modelName, out MefModel? model));
        RetailCarBody car = RetailCarSpawn.Create(physics, model!, RetailCarSpawn.Placement(task, wheelAttachments: 16));
        Drive(car, RetailCarStepCapture.Load(fixture), mission);
    }

    private void Drive(RetailCarBody car, RetailCarStepCapture capture, int mission)
    {
        RetailMissionData data = RetailMissionData.Get(mission);
        RetailTerrainRay terrain = data.TerrainRay;

        // Mission 7 stacks a second T80 (car 361) on the first; retail's rays find it on the first tick only.
        // Mission 6's limousine stands in ConditionalContainer 353 as a static model until its alarm; by the time the
        // capture's car drives, that container is dormant and the model out of the world.
        Func<object, bool> present = owner => mission != 6 || !Under(owner as GameTask, 353);
        List<RetailRayModel> others = [];
        if (mission == 7 && data.Root.Tree.FromId(361) is DeclaredTask stacked)
        {
            others.Add(PlacedCar(data, stacked));
        }
        int t = 0;
        int objectHits = 0;
        for (; t + 1 < capture.Ticks.Count; t++)
        {
            RetailCarStepCapture.Tick tick = capture.Ticks[t];
            RetailCarStepCapture.Tick next = capture.Ticks[t + 1];
            RetailCarMemory.LoadControls(car, tick.Entry);
            TerrainWorld world = new(terrain, data.Objects, tick.Sequence == 1 ? others : null, present, tick);
            car.Tick(world);
            objectHits += world.ObjectHits;
            List<string> diff = [.. world.Errors, .. RetailCarMemory.Diff(car, next.Entry)];
            Assert.True(diff.Count == 0, $"tick {tick.Sequence}: {string.Join("; ", diff.Take(12))}");
        }

        output.WriteLine($"{t} ticks driven on the port's own rays ({objectHits} of them struck a model)");
    }

    private static bool Under(GameTask? task, int containerId)
    {
        for (GameTask? current = task; current is not null; current = current.Parent)
        {
            if (current.Id == containerId && current.Type.Name == "ConditionalContainer")
            {
                return true;
            }
        }

        return false;
    }

    private static RetailRayModel PlacedCar(RetailMissionData data, DeclaredTask task)
    {
        string name = Assert.IsType<string>(task.TryGetValue(WorldScene.ModelParameter, out object? value) ? value : null);
        Assert.True(data.Mission.TryGetModel(name, out MefModel? model));
        Assert.True(MefCollisionMesh.TryRead(model!.Container, meshIndex: 0, out MefCollisionMesh? mesh));
        RetailRayModel placed = new(task, new RetailRayMesh(mesh!), model.NativeMarkerBound);
        Vector3d position = Assert.IsType<Vector3d>(task.TryGetValue("Position", out object? p) ? p : null);
        Vector3f angles = Assert.IsType<Vector3f>(task.TryGetValue("Orientation", out object? a) ? a : null);
        placed.Position[0] = position.X;
        placed.Position[1] = position.Y;
        placed.Position[2] = position.Z;
        RetailSplineSegment.AnglesToMatrix(angles.X, angles.Y, angles.Z).CopyTo(placed.M, 0);
        return placed;
    }

    /// <summary>Answers the wheel rays with the port's terrain query and checks each answer against the capture.</summary>
    private sealed class TerrainWorld(
        RetailTerrainRay terrain,
        RetailWorldObjects objects,
        IReadOnlyList<RetailRayModel>? extra,
        Func<object, bool>? present,
        RetailCarStepCapture.Tick tick) : IRetailCarWorld
    {
        public List<string> Errors { get; } = [];

        public int ObjectHits { get; private set; }

        public void CastRays(RetailRigidBody car, ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<RetailRayResult> results)
        {
            for (int k = 0; k < results.Length; k++)
            {
                results[k] = new RetailRayResult { Point = ends[k] };
            }

            objects.Cast(starts, ends, results, excluded: null, extra, present);
            terrain.Cast(starts, ends, results);
            ObjectHits += results.ToArray().Count(static result => result.Hit && result.Struck is not null);
            if (starts.Length != tick.RayCount)
            {
                Errors.Add($"ray count: port {starts.Length} retail {tick.RayCount}");
                return;
            }

            byte[] o = tick.RayResults;
            for (int k = 0; k < starts.Length; k++)
            {
                int r = 112 * k;
                RetailRayResult port = results[k];
                if (port.Struck is DeclaredTask struck && BitConverter.ToInt32(o, r) == 0)
                {
                    Errors.Add($"ray{k} struck {struck.Type.Name} {struck.Id} '{struck.Name}' model {(struck.TryGetValue(WorldScene.ModelParameter, out object? m) ? m : null)}");
                }

                Same($"ray{k}.hit", port.Hit ? 1 : 0, BitConverter.ToInt32(o, r + 88));
                Same($"ray{k}.point.x", port.Point.X, BitConverter.ToDouble(o, r + 8));
                Same($"ray{k}.point.y", port.Point.Y, BitConverter.ToDouble(o, r + 16));
                Same($"ray{k}.point.z", port.Point.Z, BitConverter.ToDouble(o, r + 24));
                if (BitConverter.ToInt32(o, r + 88) == 0)
                {
                    // A miss: RayCast initialises only +0, +8 and +88; the rest of the record is stale stack.
                    continue;
                }

                Same($"ray{k}.normal.x", port.NormalX, BitConverter.ToSingle(o, r + 56));
                Same($"ray{k}.normal.y", port.NormalY, BitConverter.ToSingle(o, r + 60));
                Same($"ray{k}.normal.z", port.NormalZ, BitConverter.ToSingle(o, r + 64));
                Same($"ray{k}.front", port.HasFrontMaterial ? 1 : 0, BitConverter.ToInt32(o, r + 76) != 0 ? 1 : 0);
                Same($"ray{k}.back", port.HasSurface ? 1 : 0, BitConverter.ToInt32(o, r + 80) != 0 ? 1 : 0);
                Same($"ray{k}.distance", port.Distance, BitConverter.ToDouble(o, r + 96));
            }
        }

        public void ProbeUp(RetailRigidBody car, ReadOnlySpan<Vector3d> points, double length, Span<RetailRayResult> results) =>
            Errors.Add("unexpected burial probe");

        public bool VetoContact(RetailRigidBody car, in RetailRayResult result) => false;

        private void Same(string name, double port, double retail)
        {
            if (BitConverter.DoubleToInt64Bits(port) != BitConverter.DoubleToInt64Bits(retail))
            {
                Errors.Add($"{name}: port {port:R} retail {retail:R}");
            }
        }

        private void Same(string name, float port, float retail)
        {
            if (BitConverter.SingleToInt32Bits(port) != BitConverter.SingleToInt32Bits(retail))
            {
                Errors.Add($"{name}: port {port:R} retail {retail:R}");
            }
        }

        private void Same(string name, int port, int retail)
        {
            if (port != retail)
            {
                Errors.Add($"{name}: port {port} retail {retail}");
            }
        }
    }
}
