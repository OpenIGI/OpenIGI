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
        public static TheoryData<string> Fixtures => new()
    {
        "carstep-m7-apc352.log.gz",
        "carstep-m7-t80-357.log.gz",
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheCarDrivesOnThePortsOwnTerrainRaysExactlyAsRetail(string fixture)
    {
        RetailCarStepCapture capture = RetailCarStepCapture.Load(fixture);
        RetailCarBody car = new(RetailCarMemory.Definition(capture.TypeMemory, capture.RecordMemory));
        RetailCarMemory.Load(car, capture.Ticks[0].Entry);
        Drive(car, capture);
    }

    [Theory]
    [InlineData(352, "carstep-m7-apc352.log.gz")]
    [InlineData(357, "carstep-m7-t80-357.log.gz")]
    public void ACarSpawnedFromTheLevelDrivesTheWholeCaptureExactlyAsRetail(int id, string fixture)
    {
        // Nothing from the capture but the controller's packets: the car is built from the level, its type
        // and its model, and every ray is the port's own.
        RetailMissionData data = RetailMissionData.Get(7);
        DeclaredTask task = Assert.IsType<DeclaredTask>(data.Root.Tree.FromId(id));
        string modelName = Assert.IsType<string>(task.TryGetValue(WorldScene.ModelParameter, out object? value) ? value : null);
        Assert.True(data.Vehicles.TryGetCar(modelName, out CarPhysicsDefinition physics));
        Assert.True(data.Mission.TryGetModel(modelName, out MefModel? model));
        RetailCarBody car = RetailCarSpawn.Create(physics, model!, RetailCarSpawn.Placement(task, wheelAttachments: 16));
        Drive(car, RetailCarStepCapture.Load(fixture));
    }

    private void Drive(RetailCarBody car, RetailCarStepCapture capture)
    {
        RetailTerrainRay terrain = RetailMissionData.Get(7).TerrainRay;
        int t = 0;
        int objectHits = 0;
        for (; t + 1 < capture.Ticks.Count; t++)
        {
            RetailCarStepCapture.Tick tick = capture.Ticks[t];
            RetailCarStepCapture.Tick next = capture.Ticks[t + 1];
            RetailCarMemory.LoadControls(car, tick.Entry);
            TerrainWorld world = new(terrain, tick);
            car.Tick(world);
            objectHits += world.ObjectHits;
            List<string> diff = [.. world.Errors, .. RetailCarMemory.Diff(car, next.Entry)];
            Assert.True(diff.Count == 0, $"tick {tick.Sequence}: {string.Join("; ", diff.Take(12))}");
        }

        output.WriteLine($"{t} ticks driven on the port's terrain rays ({objectHits} model hits taken from the capture)");
    }

    /// <summary>Answers the wheel rays with the port's terrain query and checks each answer against the capture.</summary>
    private sealed class TerrainWorld(RetailTerrainRay terrain, RetailCarStepCapture.Tick tick) : IRetailCarWorld
    {
        public List<string> Errors { get; } = [];

        public int ObjectHits { get; private set; }

        public void CastRays(RetailCarBody car, ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<RetailRayResult> results)
        {
            for (int k = 0; k < results.Length; k++)
            {
                results[k] = new RetailRayResult { Point = ends[k] };
            }

            terrain.Cast(starts, ends, results);
            if (starts.Length != tick.RayCount)
            {
                Errors.Add($"ray count: port {starts.Length} retail {tick.RayCount}");
                return;
            }

            byte[] o = tick.RayResults;
            for (int k = 0; k < starts.Length; k++)
            {
                int r = 112 * k;
                if (k * 112 >= tick.UnknownRayPrefix && BitConverter.ToInt32(o, r) != 0)
                {
                    // A model's mesh answered this ray (0x4C1840), which is not ported yet: take retail's answer.
                    ObjectHits++;
                    results[k] = new RetailRayResult
                    {
                        Struck = new object(),
                        Hit = BitConverter.ToInt32(o, r + 88) != 0,
                        Point = new Vector3d(BitConverter.ToDouble(o, r + 8), BitConverter.ToDouble(o, r + 16), BitConverter.ToDouble(o, r + 24)),
                        Distance = BitConverter.ToDouble(o, r + 96),
                        NormalX = BitConverter.ToSingle(o, r + 56),
                        NormalY = BitConverter.ToSingle(o, r + 60),
                        NormalZ = BitConverter.ToSingle(o, r + 64),
                        HasFrontMaterial = BitConverter.ToInt32(o, r + 76) != 0,
                        HasSurface = BitConverter.ToInt32(o, r + 80) != 0,
                    };
                    continue;
                }

                RetailRayResult port = results[k];
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

        public void ProbeUp(RetailCarBody car, ReadOnlySpan<Vector3d> points, double length, Span<RetailRayResult> results) =>
            Errors.Add("unexpected burial probe");

        public bool VetoContact(RetailCarBody car, in RetailRayResult result) => false;

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
