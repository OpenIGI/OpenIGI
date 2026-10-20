using OpenIGI.Game.Physics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using Xunit;
using Xunit.Abstractions;
using OpenIGI.TestSupport;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Replays mission 7's Mil helicopter 353 through <see cref="RetailHeliBody"/> and demands the original's bytes back.
/// </summary>
/// <remarks>
/// The capture holds 1200 physics ticks of the heli's AnimTask flight: the object at every <c>Heli_Tick</c> entry,
/// every force pushed onto its body with the caller, and the skid rays with their results. Its rotors push their
/// lift and yaw records from the run op after each tick (callers <c>0x42D548</c>, <c>0x42D5F8</c>), and the next
/// tick integrates them.
/// </remarks>
[Collection(RetailMissionWorld.Name)]
public sealed class RetailHeliBodyReplayTests(ITestOutputHelper output)
{
    private const string Fixture = "carstep-m7-heli353.log.gz";
    private static readonly int[] RotorCallers = [0x42D548, 0x42D5F8];

    [RequiresGameDataFact]
    public void EveryTickFromItsOwnEntryMatchesRetailBitForBit()
    {
        RetailCarStepCapture capture = RetailCarStepCapture.Load(Fixture);
        RetailHeliDefinition definition = Definition(capture);
        (List<(Vector3d Local, MagicHeliWheel Record)> skids, List<RetailHeliRotor> rotors) = Attachments();
        List<string> failures = [];
        int replayed = 0;
        for (int t = 1; t + 1 < capture.Ticks.Count; t++)
        {
            RetailCarStepCapture.Tick previous = capture.Ticks[t - 1], tick = capture.Ticks[t], next = capture.Ticks[t + 1];
            if (tick.Sequence != previous.Sequence + 1 || next.Sequence != tick.Sequence + 1)
            {
                continue;
            }

            RetailHeliBody heli = Heli(definition, skids, rotors, tick.Entry);
            foreach (RetailCarStepCapture.Force force in previous.Forces.Where(IsRotor))
            {
                heli.Forces.Add(ToForce(force));
            }

            List<string> diff = Step(heli, tick, next);
            replayed++;
            if (diff.Count != 0)
            {
                failures.Add($"tick {tick.Sequence}: {string.Join("; ", diff.Take(10))}{(diff.Count > 10 ? $" (+{diff.Count - 10})" : string.Empty)}");
            }
        }

        foreach (string failure in failures.Take(15))
        {
            output.WriteLine(failure);
        }

        Assert.True(replayed > 1000, $"only {replayed} ticks replayed");
        Assert.True(failures.Count == 0, $"{failures.Count} of {replayed} ticks differ; first: {failures.FirstOrDefault()}");
    }

    [RequiresGameDataFact]
    public void TheWholeFlightReplaysFromItsSecondTickAlone()
    {
        RetailCarStepCapture capture = RetailCarStepCapture.Load(Fixture);
        (List<(Vector3d Local, MagicHeliWheel Record)> skids, List<RetailHeliRotor> rotors) = Attachments();
        RetailHeliBody heli = Heli(Definition(capture), skids, rotors, capture.Ticks[1].Entry);
        foreach (RetailCarStepCapture.Force force in capture.Ticks[0].Forces.Where(IsRotor))
        {
            heli.Forces.Add(ToForce(force));
        }

        int t = 1;
        for (; t + 1 < capture.Ticks.Count; t++)
        {
            RetailCarStepCapture.Tick tick = capture.Ticks[t], next = capture.Ticks[t + 1];
            LoadTargets(heli, tick.Entry);
            List<string> diff = Step(heli, tick, next);
            Assert.True(diff.Count == 0, $"tick {tick.Sequence}: {string.Join("; ", diff.Take(10))}");
        }

        output.WriteLine($"{t - 1} ticks flown; final position ({heli.Position[0]:R}, {heli.Position[1]:R}, {heli.Position[2]:R})");
    }

    [RequiresGameDataFact]
    public void AHelicopterIsCreatedExactlyAsRetailHoldsItAtItsFirstTick()
    {
        RetailMissionData data = RetailMissionData.Get(7);
        RetailCarStepCapture capture = RetailCarStepCapture.Load(Fixture);
        OpenIGI.Engine.Tasks.DeclaredTask task = Assert.IsType<OpenIGI.Engine.Tasks.DeclaredTask>(data.Root.Tree.FromId(353));
        string name = Assert.IsType<string>(task.TryGetValue(WorldScene.ModelParameter, out object? value) ? value : null);
        Assert.True(data.Vehicles.TryGetHelicopter(name, out HelicopterPhysicsDefinition physics));
        Assert.True(data.Mission.TryGetModel(name, out MefModel? model));
        RetailHeliBody heli = RetailHeliSpawn.Create(
            physics,
            model!,
            RetailHeliSpawn.Placement(task),
            n => data.Magic.TryGet(n, out MagicObjectDefinition d) ? d : null,
            n => data.Vehicles.TryGetRotor(n, out RotorPhysicsDefinition r) ? r : null);

        // The definition against the captured TYPE and record.
        RetailHeliDefinition expected = Definition(capture);
        Assert.Equal(expected.Mass, heli.Definition.Mass);
        Assert.Equal(expected.SleepSpeed, heli.Definition.SleepSpeed);
        Assert.Equal(expected.SleepSpin, heli.Definition.SleepSpin);
        for (int k = 0; k < 10; k++)
        {
            if (k != 7)
            {
                Assert.Equal(expected.Record[k], heli.Definition.Record[k]);
            }
        }

        Assert.Equal(expected.Aero, heli.Definition.Aero);

        // Retail's second reset is followed by the rotors' first pushes, then the first tick.
        heli.RotorTick();
        List<string> diff = RetailCarMemory.DiffBody(heli, capture.Ticks[0].Entry);
        byte[] b = capture.Ticks[0].Entry;
        RetailCarMemory.SameFloat(diff, "collective", heli.Collective, BitConverter.ToSingle(b, 1824), 1824);
        RetailCarMemory.SameFloat(diff, "collectiveTarget", heli.CollectiveTarget, BitConverter.ToSingle(b, 1840), 1840);
        RetailCarMemory.SameFloat(diff, "originalThrust", heli.OriginalThrust, BitConverter.ToSingle(b, 1856), 1856);
        RetailCarMemory.SameFloat(diff, "mass", heli.Mass, BitConverter.ToSingle(b, 240), 240);
        RetailCarMemory.SameFloat(diff, "inverseMass", heli.InverseMass, BitConverter.ToSingle(b, 244), 244);
        for (int k = 0; k < 3; k++)
        {
            RetailCarMemory.SameFloat(diff, $"inverseInertia{k}", heli.InverseInertia[k], BitConverter.ToSingle(b, 252 + (4 * k)), 252 + (4 * k));
        }

        // Skid starts on the first tick are the reset's seating plus 2 m.
        Assert.Equal(capture.Ticks[0].SkidRayCount, heli.Skids.Count);
        Assert.True(diff.Count == 0, string.Join("; ", diff));
    }

    private static bool IsRotor(RetailCarStepCapture.Force force) => RotorCallers.Contains(force.Caller);

    private static PointForce ToForce(RetailCarStepCapture.Force force) =>
        new(new Vector3f(force.Vector[0], force.Vector[1], force.Vector[2]), new Vector3f(force.Point[0], force.Point[1], force.Point[2]));

    private static List<string> Step(RetailHeliBody heli, RetailCarStepCapture.Tick tick, RetailCarStepCapture.Tick next)
    {
        SkidWorld world = new(tick);
        heli.Tick(world);
        List<PointForce> own = [.. heli.PushedForces];
        heli.PushedForces.Clear();
        heli.RotorTick();
        List<PointForce> rotor = [.. heli.PushedForces];
        List<string> diff = [.. world.Errors];
        Compare(diff, "tick", own, [.. tick.Forces.Where(f => !IsRotor(f))]);
        Compare(diff, "rotor", rotor, [.. tick.Forces.Where(IsRotor)]);
        diff.AddRange(RetailCarMemory.DiffBody(heli, next.Entry));
        byte[] b = next.Entry;
        RetailCarMemory.SameFloat(diff, "collective", heli.Collective, BitConverter.ToSingle(b, 1824), 1824);
        for (int k = 0; k < 3; k++)
        {
            RetailCarMemory.SameFloat(diff, $"control{k}", heli.Controls[k], BitConverter.ToSingle(b, 1828 + (4 * k)), 1828 + (4 * k));
        }

        return diff;
    }

    private static void Compare(List<string> diff, string what, List<PointForce> port, List<RetailCarStepCapture.Force> retail)
    {
        if (port.Count != retail.Count)
        {
            diff.Add($"{what} force count: port {port.Count} retail {retail.Count} ({string.Join(",", retail.Select(f => $"0x{f.Caller:X}"))})");
        }

        for (int k = 0; k < Math.Min(port.Count, retail.Count); k++)
        {
            PointForce p = port[k];
            float[] a = [p.Force.X, p.Force.Y, p.Force.Z, p.Point.X, p.Point.Y, p.Point.Z];
            float[] r = [.. retail[k].Vector, .. retail[k].Point];
            for (int c = 0; c < 6; c++)
            {
                if (BitConverter.SingleToInt32Bits(a[c]) != BitConverter.SingleToInt32Bits(r[c]))
                {
                    diff.Add($"{what} force{k} (0x{retail[k].Caller:X}) [{c}]: port {a[c]:R} retail {r[c]:R}");
                }
            }
        }
    }

    private static RetailHeliBody Heli(
        RetailHeliDefinition definition,
        List<(Vector3d Local, MagicHeliWheel Record)> skids,
        List<RetailHeliRotor> rotors,
        byte[] entry)
    {
        RetailHeliBody heli = new(definition);
        RetailCarMemory.LoadBody(heli, entry);
        heli.Collective = BitConverter.ToSingle(entry, 1824);
        for (int k = 0; k < 3; k++)
        {
            heli.Controls[k] = BitConverter.ToSingle(entry, 1828 + (4 * k));
        }

        heli.OriginalThrust = BitConverter.ToSingle(entry, 1856);
        LoadTargets(heli, entry);
        foreach ((Vector3d local, MagicHeliWheel record) in skids)
        {
            heli.Skids.Add(new RetailHeliSkid(local, record.Travel, record.BottomOut));
        }

        heli.Rotors.AddRange(rotors);
        SeatSkids(heli);
        return heli;
    }

    private static void LoadTargets(RetailHeliBody heli, byte[] entry)
    {
        heli.CollectiveTarget = BitConverter.ToSingle(entry, 1840);
        for (int k = 0; k < 3; k++)
        {
            heli.Targets[k] = BitConverter.ToSingle(entry, 1844 + (4 * k));
        }
    }

    /// <summary>The skids sit where the previous step's re-seat left them: <c>M·l + pos</c> of the entry pose.</summary>
    private static void SeatSkids(RetailHeliBody heli)
    {
        float[] m = heli.M;
        foreach (RetailHeliSkid skid in heli.Skids)
        {
            Vector3d l = skid.Local;
            double wx = ((X87)m[2] * l.Z + (X87)m[1] * l.Y + (X87)m[0] * l.X).Value;
            double wy = ((X87)m[5] * l.Z + (X87)m[4] * l.Y + (X87)m[3] * l.X).Value;
            double wz = ((X87)m[8] * l.Z + (X87)m[7] * l.Y + (X87)m[6] * l.X).Value;
            skid.World = new Vector3d(X87.Add(wx, heli.Position[0]), X87.Add(wy, heli.Position[1]), X87.Add(wz, heli.Position[2]));
        }
    }

    private static RetailHeliDefinition Definition(RetailCarStepCapture capture)
    {
        byte[] t = capture.TypeMemory, r = capture.RecordMemory;
        int aero = BitConverter.ToInt32(r, 40);
        Wing[] surfaces = new Wing[aero];
        for (int j = 0; j < aero; j++)
        {
            int s = 44 + (28 * j);
            float F(int o) => BitConverter.ToSingle(r, s + o);
            surfaces[j] = new Wing(F(0), new Vector3f(F(16), F(20), F(24)), new Vector3f(F(4), F(8), F(12)));
        }

        return new RetailHeliDefinition
        {
            Mass = BitConverter.ToSingle(t, 8),
            SleepSpeed = BitConverter.ToSingle(t, 716),
            SleepSpin = BitConverter.ToSingle(t, 720),
            Record = [.. Enumerable.Range(0, 10).Select(k => BitConverter.ToSingle(r, 4 * k))],
            Aero = surfaces,
        };
    }

    /// <summary>The Mil's skids and rotors, from its model's attachments in order.</summary>
    private static (List<(Vector3d, MagicHeliWheel)> Skids, List<RetailHeliRotor> Rotors) Attachments()
    {
        RetailMissionData data = RetailMissionData.Get(7);
        Assert.True(data.Mission.TryGetModel("700_01_1", out MefModel? model));
        List<(Vector3d, MagicHeliWheel)> skids = [];
        List<RetailHeliRotor> rotors = [];
        foreach (MefAttachment attachment in model!.Attachments)
        {
            if (!data.Magic.TryGet(attachment.Name, out MagicObjectDefinition magic))
            {
                continue;
            }

            if (magic.HeliWheel is { } wheel)
            {
                skids.Add((new Vector3d(attachment.Position.X, attachment.Position.Y, attachment.Position.Z), wheel));
            }
            else if (data.Vehicles.TryGetRotor(magic.Model, out RotorPhysicsDefinition rotor))
            {
                rotors.Add(new RetailHeliRotor(rotor.ProducesLift, rotor.IsTailRotor));
            }
        }

        return (skids, rotors);
    }

    /// <summary>Answers the skid rays with the captured results, checking the rays match.</summary>
    private sealed class SkidWorld(RetailCarStepCapture.Tick tick) : IRetailCarWorld
    {
        public List<string> Errors { get; } = [];

        public void CastRays(RetailRigidBody car, ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<RetailRayResult> results)
        {
            if (starts.Length != tick.SkidRayCount)
            {
                Errors.Add($"skid ray count: port {starts.Length} retail {tick.SkidRayCount}");
                return;
            }

            byte[] st = [.. tick.SkidStarts], en = [.. tick.SkidEnds], o = [.. tick.SkidResults];
            for (int k = 0; k < starts.Length; k++)
            {
                Check($"skid{k}.start", starts[k], st, 24 * k);
                Check($"skid{k}.end", ends[k], en, 24 * k);
                int r = 112 * k;
                results[k] = new RetailRayResult
                {
                    Hit = BitConverter.ToInt32(o, r + 88) != 0,
                    Distance = BitConverter.ToDouble(o, r + 96),
                    Point = new Vector3d(BitConverter.ToDouble(o, r + 8), BitConverter.ToDouble(o, r + 16), BitConverter.ToDouble(o, r + 24)),
                };
            }
        }

        public void ProbeUp(RetailRigidBody car, ReadOnlySpan<Vector3d> points, double length, Span<RetailRayResult> results) =>
            Errors.Add("unexpected burial probe");

        public bool VetoContact(RetailRigidBody car, in RetailRayResult result) => false;

        private void Check(string name, Vector3d actual, byte[] buffer, int offset)
        {
            double[] a = [actual.X, actual.Y, actual.Z];
            for (int k = 0; k < 3; k++)
            {
                double expected = BitConverter.ToDouble(buffer, offset + (8 * k));
                if (BitConverter.DoubleToInt64Bits(expected) != BitConverter.DoubleToInt64Bits(a[k]))
                {
                    Errors.Add($"{name}[{k}]: port {a[k]:R} retail {expected:R}");
                }
            }
        }
    }
}
