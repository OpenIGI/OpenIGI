using OpenIGI.Game.World;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Replays retail car ticks through <see cref="RetailCarBody"/> and demands the original's bytes back.
/// </summary>
/// <remarks>
/// The fixtures are mission 7's APC 352 driving its CarAI route for 600 ticks, and its T80 357 for 900 from
/// the tick it spawns with its tracks below the road (the bottom-out lift, the fall, the settle), captured by
/// the probe's <c>carstep</c> hook set: the car object at every <c>Car_Tick</c> entry, every force pushed
/// onto its body, and the wheel rays with their results. The rays are answered from the capture; everything
/// else is the port's own arithmetic.
/// </remarks>
public sealed class RetailCarBodyReplayTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, RetailCarStepCapture> Captures = [];

    public static TheoryData<string> Fixtures => new()
    {
        "carstep-m7-apc352.log.gz",
        "carstep-m7-t80-357.log.gz",
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryTickFromItsOwnEntryMatchesRetailBitForBit(string fixture)
    {
        RetailCarStepCapture capture = Load(fixture);
        RetailCarDefinition definition = RetailCarMemory.Definition(capture.TypeMemory, capture.RecordMemory);
        List<string> failures = [];
        int replayed = 0;
        for (int t = 0; t + 1 < capture.Ticks.Count; t++)
        {
            RetailCarStepCapture.Tick tick = capture.Ticks[t];
            RetailCarStepCapture.Tick next = capture.Ticks[t + 1];
            if (next.Sequence != tick.Sequence + 1)
            {
                continue;
            }

            RetailCarBody car = new(definition);
            RetailCarMemory.Load(car, tick.Entry);
            List<string> diff = Step(car, tick, next);
            replayed++;
            if (diff.Count != 0)
            {
                failures.Add($"tick {tick.Sequence}: {string.Join("; ", diff.Take(12))}{(diff.Count > 12 ? $" (+{diff.Count - 12})" : string.Empty)}");
            }
        }

        foreach (string failure in failures.Take(20))
        {
            output.WriteLine(failure);
        }

        Assert.True(replayed > 500, $"only {replayed} tick pairs replayed");
        Assert.True(failures.Count == 0, $"{failures.Count} of {replayed} ticks differ; first: {failures.FirstOrDefault()}");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheWholeDriveReplaysFromTheFirstTickAlone(string fixture)
    {
        RetailCarStepCapture capture = Load(fixture);
        RetailCarBody car = new(RetailCarMemory.Definition(capture.TypeMemory, capture.RecordMemory));
        RetailCarMemory.Load(car, capture.Ticks[0].Entry);
        int t = 0;
        for (; t + 1 < capture.Ticks.Count; t++)
        {
            RetailCarStepCapture.Tick tick = capture.Ticks[t];
            RetailCarStepCapture.Tick next = capture.Ticks[t + 1];
            Assert.Equal(tick.Sequence + 1, next.Sequence);

            // Only the controller's packet reaches the car between two ticks.
            RetailCarMemory.LoadControls(car, tick.Entry);
            List<string> diff = Step(car, tick, next);
            Assert.True(diff.Count == 0, $"tick {tick.Sequence}: {string.Join("; ", diff.Take(12))}");
        }

        output.WriteLine($"{t} ticks replayed; final position ({car.Position[0]:R}, {car.Position[1]:R}, {car.Position[2]:R})");
    }

    private static RetailCarStepCapture Load(string fixture)
    {
        lock (Captures)
        {
            return Captures.TryGetValue(fixture, out RetailCarStepCapture? capture)
                ? capture
                : Captures[fixture] = RetailCarStepCapture.Load(fixture);
        }
    }

    private static List<string> Step(RetailCarBody car, RetailCarStepCapture.Tick tick, RetailCarStepCapture.Tick next)
    {
        RetailCarMemory.RecordedWorld world = new(tick);
        car.Tick(world);
        List<string> diff = [.. world.RayErrors];

        // The forces are consumed inside the tick, so compare what the port pushed as it pushed them.
        List<RetailForce> pushed = car.PushedForces;
        if (pushed.Count != tick.Forces.Count)
        {
            diff.Add($"force count: port {pushed.Count} retail {tick.Forces.Count}");
        }

        for (int k = 0; k < Math.Min(pushed.Count, tick.Forces.Count); k++)
        {
            RetailForce p = pushed[k];
            RetailCarStepCapture.Force r = tick.Forces[k];
            float[] port = [p.Fx, p.Fy, p.Fz, p.Px, p.Py, p.Pz];
            float[] retail = [.. r.Vector, .. r.Point];
            for (int c = 0; c < 6; c++)
            {
                if (BitConverter.SingleToInt32Bits(port[c]) != BitConverter.SingleToInt32Bits(retail[c]))
                {
                    diff.Add($"force{k} (0x{r.Caller:X}) [{c}]: port {port[c]:R} retail {retail[c]:R}");
                }
            }
        }

        diff.AddRange(RetailCarMemory.Diff(car, next.Entry));
        return diff;
    }
}
