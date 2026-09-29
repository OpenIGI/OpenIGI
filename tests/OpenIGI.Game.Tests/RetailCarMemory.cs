using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Tests;

/// <summary>Maps a <see cref="RetailCarBody"/> to and from the original car object's bytes.</summary>
internal static class RetailCarMemory
{
    /// <summary>Builds the fixed definition from the captured physics type and its car record.</summary>
    public static RetailCarDefinition Definition(byte[] type, byte[] record)
    {
        int wheels = I(record, 280);
        RetailCarWheelDefinition[] wheelDefinitions = new RetailCarWheelDefinition[wheels];
        for (int i = 0; i < wheels; i++)
        {
            int w = 284 + (20 * i);
            wheelDefinitions[i] = new RetailCarWheelDefinition(
                record[w] != 0,
                record[w + 1] != 0,
                F(record, w + 4),
                I(record, w + 8),
                F(record, w + 12),
                F(record, w + 16));
        }

        int aero = I(record, 40);
        RetailAeroSurface[] surfaces = new RetailAeroSurface[aero];
        for (int j = 0; j < aero; j++)
        {
            int s = 44 + (28 * j);
            surfaces[j] = new RetailAeroSurface(
                F(record, s), F(record, s + 4), F(record, s + 8), F(record, s + 12),
                F(record, s + 16), F(record, s + 20), F(record, s + 24));
        }

        return new RetailCarDefinition
        {
            Mass = F(type, 8),

            // Past the captured 400 bytes; a car with a wheel on the ground is never asleep regardless.
            SleepSpeed = type.Length > 720 ? F(type, 716) : float.NaN,
            SleepSpin = type.Length > 724 ? F(type, 720) : float.NaN,
            SteeringRetention = F(record, 24),
            EngineForce = F(record, 28),
            ThrottleRetention = F(record, 32),
            Aero = surfaces,
            Wheels = wheelDefinitions,
            SkidSteer = record[609] != 0,
        };
    }

    /// <summary>Loads a car's state from an entry dump.</summary>
    public static void Load(RetailCarBody car, byte[] b)
    {
        LoadBody(car, b);
        car.Throttle = F(b, 1824);
        car.Steering = F(b, 1836);
        LoadControls(car, b);
        car.DrivenWheels = I(b, 1880);
        for (int i = 0; i < RetailCarBody.MaxWheels; i++)
        {
            int w = 1884 + (28 * i);
            RetailCarWheel wheel = car.Wheels[i];
            wheel.Enabled = b[w] != 0;
            wheel.Contact = b[w + 1] != 0;
            for (int k = 0; k < 3; k++)
            {
                wheel.Normal[k] = F(b, w + 4 + (4 * k));
                wheel.Point[k] = F(b, w + 16 + (4 * k));
            }
        }
    }

    /// <summary>Loads the shared physics object's state (<c>+0 … +1823</c>) from an entry dump.</summary>
    public static void LoadBody(RetailRigidBody car, byte[] b)
    {
        for (int k = 0; k < 3; k++)
        {
            car.Position[k] = D(b, 32 + (8 * k));
            car.Dimensions[k] = F(b, 152 + (4 * k));
            car.InverseInertia[k] = F(b, 252 + (4 * k));
            car.Velocity[k] = F(b, 264 + (4 * k));
            car.Direction[k] = F(b, 276 + (4 * k));
            car.Momentum[k] = F(b, 288 + (4 * k));
            car.LastForce[k] = F(b, 300 + (4 * k));
            car.AngularVelocity[k] = F(b, 312 + (4 * k));
            car.PreviousPosition[k] = D(b, 1728 + (8 * k));
            car.Displacement[k] = D(b, 1800 + (8 * k));
        }

        car.Radius = F(b, 68);

        // The model's radius is not in the car; with unit scale factors the broad-phase radius is it.
        car.ModelRadius = car.Radius / Math.Max(car.Dimensions[0], Math.Max(car.Dimensions[1], car.Dimensions[2]));
        for (int k = 0; k < 9; k++)
        {
            car.M[k] = F(b, 112 + (4 * k));
            car.PreviousM[k] = F(b, 1684 + (4 * k));
            car.DeltaM[k] = F(b, 1756 + (4 * k));
        }

        car.MatrixCounter = I(b, 148);
        car.PreviousMatrixCounter = I(b, 1720);
        car.DeltaMatrixCounter = I(b, 1792);
        car.Mass = F(b, 240);
        car.InverseMass = F(b, 244);
        car.Speed = F(b, 248);
        car.IntegratedForceCount = I(b, 336);
        car.ContactCount = I(b, 440);
        for (int k = 0; k < RetailCarBody.PointCount; k++)
        {
            int p = 444 + (56 * k);
            RetailCarPoint point = car.Points[k];
            point.Side = I(b, p);
            point.Restitution = F(b, p + 8);
            point.Position[0] = F(b, p + 12);
            point.Position[1] = F(b, p + 16);
            point.Position[2] = F(b, p + 20);
            point.Collides = b[p + 24] != 0;
            point.IsCollisionPoint = b[p + 25] != 0;
            point.Contact = b[p + 26] != 0;
            point.FrictionX = F(b, p + 28);
            point.FrictionY = F(b, p + 32);
            point.Normal[0] = F(b, p + 36);
            point.Normal[1] = F(b, p + 40);
            point.Normal[2] = F(b, p + 44);
            point.Distance = F(b, p + 48);
            point.Compression = F(b, p + 52);
        }

        car.CollideEnabled = I(b, 1676);
        car.Wake = b[1680] != 0;
        car.RunCollide = b[1752] != 0;
        car.Planar = b[1753] != 0;
        car.Forces.Clear();
    }

    /// <summary>Loads only what the car's controller writes between ticks: the targets and the handbrake.</summary>
    public static void LoadControls(RetailCarBody car, byte[] b)
    {
        car.ThrottleTarget = F(b, 1840);
        car.SteeringTarget = F(b, 1852);
        car.Handbrake = b[1860] != 0;
    }

    /// <summary>Lists every field of the car that differs, bit for bit, from an entry dump.</summary>
    public static List<string> Diff(RetailCarBody car, byte[] b)
    {
        List<string> diff = DiffBody(car, b);
        string[] axis = ["x", "y", "z"];
        SameFloat(diff, "throttle", car.Throttle, F(b, 1824), 1824);
        SameFloat(diff, "steering", car.Steering, F(b, 1836), 1836);
        for (int i = 0; i < RetailCarBody.MaxWheels; i++)
        {
            int w = 1884 + (28 * i);
            RetailCarWheel wheel = car.Wheels[i];
            if (wheel.Contact != (b[w + 1] != 0))
            {
                diff.Add($"wheel{i}.contact (+{w + 1}): port {wheel.Contact} retail {b[w + 1]}");
            }

            for (int k = 0; k < 3; k++)
            {
                SameFloat(diff, $"wheel{i}.normal.{axis[k]}", wheel.Normal[k], F(b, w + 4 + (4 * k)), w + 4 + (4 * k));
                SameFloat(diff, $"wheel{i}.point.{axis[k]}", wheel.Point[k], F(b, w + 16 + (4 * k)), w + 16 + (4 * k));
            }
        }

        return diff;
    }

    /// <summary>Adds a mismatch when two floats differ in any bit.</summary>
    public static void SameFloat(List<string> diff, string name, float actual, float expected, int offset)
    {
        if (BitConverter.SingleToInt32Bits(actual) != BitConverter.SingleToInt32Bits(expected))
        {
            diff.Add($"{name} (+{offset}): port {actual:R} (0x{BitConverter.SingleToInt32Bits(actual):X8}) retail {expected:R} (0x{BitConverter.SingleToInt32Bits(expected):X8})");
        }
    }

    /// <summary>Lists every shared physics-object field that differs, bit for bit, from an entry dump.</summary>
    public static List<string> DiffBody(RetailRigidBody car, byte[] b)
    {
        List<string> diff = [];
        void Float(string name, float actual, int offset)
        {
            float expected = F(b, offset);
            if (BitConverter.SingleToInt32Bits(actual) != BitConverter.SingleToInt32Bits(expected))
            {
                diff.Add($"{name} (+{offset}): port {actual:R} (0x{BitConverter.SingleToInt32Bits(actual):X8}) retail {expected:R} (0x{BitConverter.SingleToInt32Bits(expected):X8})");
            }
        }

        void Double(string name, double actual, int offset)
        {
            double expected = D(b, offset);
            if (BitConverter.DoubleToInt64Bits(actual) != BitConverter.DoubleToInt64Bits(expected))
            {
                diff.Add($"{name} (+{offset}): port {actual:R} retail {expected:R} ({(actual - expected):R})");
            }
        }

        void Int(string name, int actual, int offset)
        {
            if (actual != I(b, offset))
            {
                diff.Add($"{name} (+{offset}): port {actual} retail {I(b, offset)}");
            }
        }

        void Bool(string name, bool actual, int offset)
        {
            if (actual != (b[offset] != 0))
            {
                diff.Add($"{name} (+{offset}): port {actual} retail {b[offset]}");
            }
        }

        string[] axis = ["x", "y", "z"];
        for (int k = 0; k < 3; k++)
        {
            Double($"position.{axis[k]}", car.Position[k], 32 + (8 * k));
            Float($"velocity.{axis[k]}", car.Velocity[k], 264 + (4 * k));
            Float($"direction.{axis[k]}", car.Direction[k], 276 + (4 * k));
            Float($"momentum.{axis[k]}", car.Momentum[k], 288 + (4 * k));
            Float($"lastForce.{axis[k]}", car.LastForce[k], 300 + (4 * k));
            Float($"omega.{axis[k]}", car.AngularVelocity[k], 312 + (4 * k));
            Double($"previousPosition.{axis[k]}", car.PreviousPosition[k], 1728 + (8 * k));
            Double($"displacement.{axis[k]}", car.Displacement[k], 1800 + (8 * k));
        }

        Float("radius", car.Radius, 68);
        for (int k = 0; k < 9; k++)
        {
            Float($"M{k}", car.M[k], 112 + (4 * k));
            Float($"previousM{k}", car.PreviousM[k], 1684 + (4 * k));
            Float($"deltaM{k}", car.DeltaM[k], 1756 + (4 * k));
        }

        Int("matrixCounter", car.MatrixCounter, 148);
        Int("previousMatrixCounter", car.PreviousMatrixCounter, 1720);
        Int("deltaMatrixCounter", car.DeltaMatrixCounter, 1792);
        Float("speed", car.Speed, 248);
        Int("integratedForceCount", car.IntegratedForceCount, 336);
        Int("forceCount", car.Forces.Count, 332);
        Int("contactCount", car.ContactCount, 440);
        for (int k = 0; k < RetailCarBody.PointCount; k++)
        {
            int p = 444 + (56 * k);
            RetailCarPoint point = car.Points[k];
            Bool($"point{k}.collides", point.Collides, p + 24);
            Bool($"point{k}.contact", point.Contact, p + 26);
            Float($"point{k}.normal.x", point.Normal[0], p + 36);
            Float($"point{k}.normal.y", point.Normal[1], p + 40);
            Float($"point{k}.normal.z", point.Normal[2], p + 44);
            Float($"point{k}.distance", point.Distance, p + 48);
            Float($"point{k}.compression", point.Compression, p + 52);
        }

        Bool("wake", car.Wake, 1680);
        return diff;
    }

    /// <summary>A world that answers the wheel rays with a captured tick's results, checking the rays match.</summary>
    internal sealed class RecordedWorld(RetailCarStepCapture.Tick tick) : IRetailCarWorld
    {
        /// <summary>Gets the mismatches between the port's rays and the captured ones.</summary>
        public List<string> RayErrors { get; } = [];

        public void CastRays(RetailRigidBody car, ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<RetailRayResult> results)
        {
            if (starts.Length != tick.RayCount)
            {
                RayErrors.Add($"ray count: port {starts.Length} retail {tick.RayCount}");
                return;
            }

            for (int k = 0; k < starts.Length; k++)
            {
                Check($"ray{k}.start", starts[k], tick.RayStarts, 24 * k);
                Check($"ray{k}.end", ends[k], tick.RayEnds, 24 * k);
                int r = 112 * k;
                byte[] o = tick.RayResults;
                results[k] = new RetailRayResult
                {
                    Struck = BitConverter.ToInt32(o, r) != 0 ? new object() : null,
                    Hit = BitConverter.ToInt32(o, r + 88) != 0,
                    Distance = BitConverter.ToDouble(o, r + 96),
                    NormalX = BitConverter.ToSingle(o, r + 56),
                    NormalY = BitConverter.ToSingle(o, r + 60),
                    NormalZ = BitConverter.ToSingle(o, r + 64),
                    HasSurface = BitConverter.ToInt32(o, r + 80) != 0,
                };
            }
        }

        public void ProbeUp(RetailRigidBody car, ReadOnlySpan<Vector3d> points, double length, Span<RetailRayResult> results) =>
            RayErrors.Add("unexpected burial probe");

        public bool VetoContact(RetailRigidBody car, in RetailRayResult result)
        {
            RayErrors.Add("unexpected struck-object contact");
            return false;
        }

        private void Check(string name, Vector3d actual, byte[] buffer, int offset)
        {
            double[] a = [actual.X, actual.Y, actual.Z];
            for (int k = 0; k < 3; k++)
            {
                int at = offset + (8 * k);
                if (at < tick.UnknownRayPrefix)
                {
                    // Only the high half of this double was captured.
                    if (BitConverter.ToInt32(buffer, at + 4) != (int)(BitConverter.DoubleToInt64Bits(a[k]) >> 32))
                    {
                        RayErrors.Add($"{name}[{k}] high half: port {a[k]:R}");
                    }

                    continue;
                }

                double expected = BitConverter.ToDouble(buffer, at);
                if (BitConverter.DoubleToInt64Bits(expected) != BitConverter.DoubleToInt64Bits(a[k]))
                {
                    RayErrors.Add($"{name}[{k}]: port {a[k]:R} retail {expected:R}");
                }
            }
        }
    }

    private static float F(byte[] b, int offset) => BitConverter.ToSingle(b, offset);

    private static double D(byte[] b, int offset) => BitConverter.ToDouble(b, offset);

    private static int I(byte[] b, int offset) => BitConverter.ToInt32(b, offset);
}
