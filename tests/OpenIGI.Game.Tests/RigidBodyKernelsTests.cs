using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The shared rigid-body steps against each game's own transcription, which they replaced: bit for bit, at each game's
/// precision.
/// </summary>
/// <remarks>
/// The oracles below are the two games' code as it stood before the merge (Project I.G.I.'s <c>RetailRigidBody</c>,
/// I.G.I. 2's <c>Igi2RigidBody</c>), copied verbatim but for the containers they read and write.
/// </remarks>
public sealed class RigidBodyKernelsTests
{
    private const int Cases = 20000;

    [Fact]
    public void The_quaternion_steps_are_each_game_s_own()
    {
        Random random = new(1);
        for (int c = 0; c < Cases; c++)
        {
            float[] m = Orientation(random);
            (float X, float Y, float Z, float W) one = RigidBodyKernels.ToQuaternion<X87>(m);
            Assert.Equal(Igi1.ToQuaternion(m), one);
            (Fpu X, Fpu Y, Fpu Z, Fpu W) two = Igi2.ToQuaternion(m);
            (float X, float Y, float Z, float W) shared = RigidBodyKernels.ToQuaternion<Fpu>(m);
            Assert.Equal((two.X.F, two.Y.F, two.Z.F, two.W.F), shared);

            float x = one.X, y = one.Y, z = one.Z;
            X87 w = one.W;
            RigidBodyKernels.NormaliseQuaternion<X87>(ref x, ref y, ref z, ref w);
            (float X, float Y, float Z, X87 W) oracle1 = Igi1.NormaliseQuaternion(one.X, one.Y, one.Z, one.W);
            Assert.Equal((oracle1.X, oracle1.Y, oracle1.Z, oracle1.W.Value), (x, y, z, w.Value));
            float[] into = new float[9];
            float[] expected = new float[9];
            RigidBodyKernels.ToMatrix<X87>(x, y, z, w, into);
            Igi1.ToMatrix(x, y, z, w, expected);
            Assert.Equal(expected, into);

            Fpu qx = two.X, qy = two.Y, qz = two.Z, qw = two.W;
            Igi2.NormaliseQuaternion(ref qx, ref qy, ref qz, ref qw);
            float sx = shared.X, sy = shared.Y, sz = shared.Z;
            Fpu sw = shared.W;
            RigidBodyKernels.NormaliseQuaternion<Fpu>(ref sx, ref sy, ref sz, ref sw);
            Assert.Equal((qx.F, qy.F, qz.F, qw.Value), (sx, sy, sz, sw.Value));
            Igi2.ToMatrix(qx, qy, qz, qw, expected);
            RigidBodyKernels.ToMatrix<Fpu>(sx, sy, sz, sw, into);
            Assert.Equal(expected, into);
        }
    }

    [Fact]
    public void The_force_sums_are_each_game_s_own()
    {
        Random random = new(2);
        for (int c = 0; c < Cases / 4; c++)
        {
            float[] m = Orientation(random);
            float inverseMass = Value(random, 0.0001f, 1f);
            PointForce[] forces = new PointForce[random.Next(0, 7)];
            for (int f = 0; f < forces.Length; f++)
            {
                forces[f] = new PointForce(
                    random.Next(5) == 0 ? default : Vector(random, 50000f),
                    random.Next(4) == 0 ? default : Vector(random, 8000f));
            }

            Vector3f v0 = Vector(random, 400f), l0 = Vector(random, 90000f);

            Vector3f v = v0, l = l0;
            Vector3f sum = RigidBodyKernels.SumForces<X87>(forces, m, inverseMass, ref v, ref l);
            (Vector3f Sum, Vector3f V, Vector3f L) one = Igi1.SumForces(forces, m, inverseMass, v0, l0);
            Assert.Equal(one, (sum, v, l));

            v = v0;
            l = l0;
            sum = RigidBodyKernels.SumForces<Fpu>(forces, m, inverseMass, ref v, ref l);
            (Vector3f Sum, Vector3f V, Vector3f L) two = Igi2.SumForces(forces, m, inverseMass, v0, l0);
            Assert.Equal(two, (sum, v, l));
        }
    }

    [Fact]
    public void Point_velocity_normalising_and_rest_are_each_game_s_own()
    {
        Random random = new(3);
        for (int c = 0; c < Cases; c++)
        {
            Vector3f v = Vector(random, 300f), w = Vector(random, 0.5f), arm = Vector(random, 12000f);
            Assert.Equal(Igi1.PointVelocity(v, w, arm), RigidBodyKernels.PointVelocity<X87>(v, w, arm));
            Assert.Equal(Igi2.PointVelocity(v, w, arm), RigidBodyKernels.PointVelocity<Fpu>(v, w, arm));

            Vector3f n = random.Next(10) == 0 ? default : Vector(random, 3000f);
            Assert.Equal(Igi1.Normalise(n), RigidBodyKernels.Normalise<X87>(n));
            Assert.Equal(Igi2.Normalised(n), RigidBodyKernels.Normalise<Fpu>(n));

            int contacts = random.Next(-1, 3);
            bool woken = random.Next(4) == 0;
            int forces = random.Next(3) == 0 ? 1 : 0;
            float speed = Value(random, 0f, 80f);
            float restSpeed = Value(random, 0f, 80f);
            float restSpin = Value(random, 0f, 0.3f);
            Assert.Equal(
                Igi1.IsAsleep(contacts, woken, forces, speed, restSpeed, w, restSpin),
                RigidBodyKernels.MayRest<X87>(contacts, woken, forces, speed, restSpeed, w, restSpin));
            Assert.Equal(
                Igi2.IsIdle(contacts, woken, forces, speed, restSpeed, w, restSpin),
                RigidBodyKernels.MayRest<Fpu>(contacts, woken, forces, speed, restSpeed, w, restSpin));
        }
    }

    // A rotation, sometimes with the trace at or below zero, sometimes a little off orthonormal as a body's drifts.
    private static float[] Orientation(Random random)
    {
        double a = random.NextDouble() * 2 * Math.PI, b = (random.NextDouble() - 0.5) * Math.PI, g = random.NextDouble() * 2 * Math.PI;
        double ca = Math.Cos(a), sa = Math.Sin(a), cb = Math.Cos(b), sb = Math.Sin(b), cg = Math.Cos(g), sg = Math.Sin(g);
        double[] r =
        [
            (ca * cg) - (sa * sb * sg), -sa * cb, (ca * sg) + (sa * sb * cg),
            (sa * cg) + (ca * sb * sg), ca * cb, (sa * sg) - (ca * sb * cg),
            -cb * sg, sb, cb * cg,
        ];
        float[] m = new float[9];
        double drift = random.Next(3) == 0 ? 1e-4 : 0.0;
        for (int i = 0; i < 9; i++)
        {
            m[i] = (float)(r[i] + ((random.NextDouble() - 0.5) * drift));
        }

        return m;
    }

    private static Vector3f Vector(Random random, float scale) =>
        new(Value(random, -scale, scale), Value(random, -scale, scale), Value(random, -scale, scale));

    private static float Value(Random random, float low, float high) => (float)(low + (random.NextDouble() * (high - low)));

    // Project I.G.I.'s RetailRigidBody (0x4EC6A0, 0x4ECF80, 0x4FC7B0), before the merge.
    private static class Igi1
    {
        private static readonly int[] NextAxis = [1, 2, 0];

        public static (float, float, float, float) ToQuaternion(float[] m)
        {
            float qx, qy, qz, qw;
            X87 tr = (X87)m[4] + m[0] + m[8];
            if (tr.Value > 0.0f)
            {
                X87 s = X87.Sqrt(tr + 1.0f);
                qw = (s * 0.5f).F;
                X87 k = (X87)0.5f / s;
                qx = (-(((X87)m[5] - m[7]) * k)).F;
                qy = (-(((X87)m[6] - m[2]) * k)).F;
                qz = (-(((X87)m[1] - m[3]) * k)).F;
            }
            else
            {
                int i = m[4] > m[0] ? 1 : 0;
                if (m[8] > m[4 * i])
                {
                    i = 2;
                }

                int j = NextAxis[i];
                int k = NextAxis[j];
                float[] q = new float[3];
                X87 s = X87.Sqrt((X87)m[4 * i] - m[4 * j] - m[4 * k] + 1.0f);
                q[i] = (s * 0.5f).F;
                if (s.Value != 0.0)
                {
                    s = (X87)0.5f / s;
                }

                q[j] = (((X87)m[(3 * j) + i] + m[(3 * i) + j]) * s).F;
                q[k] = (((X87)m[(3 * k) + i] + m[(3 * i) + k]) * s).F;
                qw = (-(((X87)m[(3 * j) + k] - m[(3 * k) + j]) * s)).F;
                qx = q[0];
                qy = q[1];
                qz = q[2];
            }

            return (qx, qy, qz, qw);
        }

        public static (float, float, float, X87) NormaliseQuaternion(float qx, float qy, float qz, X87 qwr)
        {
            X87 len = X87.Sqrt((X87)qz * qz + (X87)qy * qy + (X87)qx * qx + qwr * qwr);
            X87 inv = (X87)1.0 / len;
            qx = ((X87)qx * inv).F;
            qy = ((X87)qy * inv).F;
            qz = ((X87)qz * inv).F;
            qwr *= inv;
            return (qx, qy, qz, qwr);
        }

        public static void ToMatrix(float qx, float qy, float qz, X87 qwr, float[] m)
        {
            float aa = Twice((X87)qx * qx).F;
            float b2 = Twice((X87)qy * qx).F;
            float cc = Twice((X87)qz * qx).F;
            float dq = Twice((X87)qx * qwr).F;
            float ee = Twice((X87)qy * qy).F;
            float fq = Twice((X87)qz * qy).F;
            float gg = Twice((X87)qy * qwr).F;
            float hh = Twice((X87)qz * qz).F;
            X87 trr = Twice(qwr * qz);

            m[0] = ((X87)1.0f - ee - hh).F;
            m[1] = ((X87)b2 - trr).F;
            m[2] = ((X87)gg + cc).F;
            m[3] = (trr + b2).F;
            X87 x = (X87)1.0f - aa;
            m[4] = (x - hh).F;
            m[5] = ((X87)fq - dq).F;
            m[6] = ((X87)cc - gg).F;
            m[7] = ((X87)fq + dq).F;
            m[8] = (x - ee).F;
        }

        public static (Vector3f, Vector3f, Vector3f) SumForces(PointForce[] forces, float[] m, float im, Vector3f v, Vector3f l)
        {
            float[] velocity = [v.X, v.Y, v.Z];
            float[] momentum = [l.X, l.Y, l.Z];
            X87 sumX = 0.0, sumY = 0.0;
            float sumZ = 0.0f;
            X87 torqueX = 0.0, torqueY = 0.0, torqueZ = 0.0;
            foreach (PointForce force in forces)
            {
                float fxe = force.Force.X, fye = force.Force.Y, fze = force.Force.Z;
                float pxe = force.Point.X, pye = force.Point.Y, pze = force.Point.Z;
                sumX += fxe;
                sumY += fye;
                sumZ = ((X87)sumZ + fze).F;
                if (pxe == 0.0f && pye == 0.0f && pze == 0.0f)
                {
                    continue;
                }

                if (fxe == 0.0f && fye == 0.0f && fze == 0.0f)
                {
                    continue;
                }

                X87 t1 = (X87)fze * pye - (X87)pze * fye;
                float t2 = (-((X87)fze * pxe - (X87)fxe * pze)).F;
                float t3 = ((X87)fye * pxe - (X87)fxe * pye).F;
                torqueX += t1;
                torqueY += t2;
                torqueZ += t3;
            }

            float fy = sumY.F;
            float fx = sumX.F;
            float tz = torqueZ.F;
            float ty = torqueY.F;
            float ax = ((X87)im * fx).F;
            float ay = ((X87)im * fy).F;
            X87 az = (X87)im * sumZ;
            float dvx = (az * m[2] + (X87)ay * m[1] + (X87)ax * m[0]).F;
            float dvy = (az * m[5] + (X87)ax * m[3] + (X87)ay * m[4]).F;
            X87 dvz = (X87)ay * m[7] + (X87)ax * m[6] + az * m[8];
            velocity[0] = ((X87)dvx + velocity[0]).F;
            velocity[1] = ((X87)dvy + velocity[1]).F;
            velocity[2] = (dvz + velocity[2]).F;
            momentum[0] = (torqueX + momentum[0]).F;
            momentum[1] = ((X87)ty + momentum[1]).F;
            momentum[2] = ((X87)tz + momentum[2]).F;
            return (new Vector3f(fx, fy, sumZ), new Vector3f(velocity[0], velocity[1], velocity[2]), new Vector3f(momentum[0], momentum[1], momentum[2]));
        }

        public static Vector3f PointVelocity(Vector3f v, Vector3f w, Vector3f r)
        {
            float cx = ((X87)r.Z * w.Y - (X87)w.Z * r.Y).F;
            float cy = (-((X87)r.Z * w.X - (X87)w.Z * r.X)).F;
            float cz = ((X87)w.X * r.Y - (X87)r.X * w.Y).F;
            return new Vector3f(((X87)v.X + cx).F, ((X87)v.Y + cy).F, ((X87)v.Z + cz).F);
        }

        public static Vector3f Normalise(Vector3f n)
        {
            float[] v = [n.X, n.Y, n.Z];
            if (v[0] == 0.0f && v[1] == 0.0f && v[2] == 0.0f)
            {
                return n;
            }

            X87 len = X87.Sqrt((X87)v[0] * v[0] + (X87)v[1] * v[1] + (X87)v[2] * v[2]);
            X87 inv = (X87)1.0 / len;
            return new Vector3f(((X87)v[0] * inv).F, ((X87)v[1] * inv).F, ((X87)v[2] * inv).F);
        }

        public static bool IsAsleep(int contacts, bool wake, int forces, float speed, float sleepSpeed, Vector3f w, float sleepSpin)
        {
            if (contacts <= 0 || wake || forces != 0)
            {
                return false;
            }

            if (!(speed < sleepSpeed))
            {
                return false;
            }

            X87 s = (X87)w.X * w.X + (X87)w.Y * w.Y + (X87)w.Z * w.Z;
            return s.Value < sleepSpin;
        }

        private static X87 Twice(X87 value) => value + value;
    }

    // I.G.I. 2's Igi2RigidBody (Physics_Update 0x5A9580, 0x5AA730) and PhysicsObj_IsIdle, before the merge.
    private static class Igi2
    {
        private static readonly int[] Next = [1, 2, 0];

        public static (Fpu X, Fpu Y, Fpu Z, Fpu W) ToQuaternion(float[] m)
        {
            Fpu trace = ((Fpu)m[4] + m[0]) + m[8];
            if (trace.Value > 0.0)
            {
                float s = Fpu.Sqrt(trace + 1f).F;
                Fpu w = (Fpu)s * 0.5f;
                Fpu r = (Fpu)0.5f / s;
                return (
                    new Fpu((-(((Fpu)m[5] - m[7]) * r)).F),
                    new Fpu((-(((Fpu)m[6] - m[2]) * r)).F),
                    new Fpu((-(((Fpu)m[1] - m[3]) * r)).F),
                    w);
            }

            int i = m[4] > m[0] ? 1 : 0;
            if (m[8] > m[4 * i])
            {
                i = 2;
            }

            int j = Next[i], k = Next[j];
            Fpu root = Fpu.Sqrt((((Fpu)m[4 * i] - m[4 * j]) - m[4 * k]) + 1f);
            float[] q = new float[3];
            q[i] = ((Fpu)0.5f * root).F;
            if (root.Value != 0.0)
            {
                root = (Fpu)0.5f / root;
            }

            q[j] = (((Fpu)m[(3 * j) + i] + m[(3 * i) + j]) * root).F;
            q[k] = (((Fpu)m[(3 * k) + i] + m[(3 * i) + k]) * root).F;
            Fpu qw = -(((Fpu)m[(3 * j) + k] - m[(3 * k) + j]) * root);
            return (new Fpu(q[0]), new Fpu(q[1]), new Fpu(q[2]), qw);
        }

        public static void ToMatrix(Fpu x, Fpu y, Fpu z, Fpu w, float[] m)
        {
            Fpu xx = Twice(x * x), xy = Twice(y * x), xz = Twice(z * x), xw = Twice(x * w);
            Fpu yy = Twice(y * y), yz = Twice(z * y), yw = Twice(y * w);
            Fpu zz = Twice(z * z), zw = Twice(z * w);
            m[0] = (((Fpu)1f - yy) - zz).F;
            m[1] = (xy - zw).F;
            m[2] = (yw + xz).F;
            m[3] = (zw + xy).F;
            Fpu oneLessXx = (Fpu)1f - xx;
            m[4] = (oneLessXx - zz).F;
            m[5] = (yz - xw).F;
            m[6] = (xz - yw).F;
            m[7] = (yz + xw).F;
            m[8] = (oneLessXx - yy).F;

            static Fpu Twice(Fpu v) => v + v;
        }

        public static void NormaliseQuaternion(ref Fpu x, ref Fpu y, ref Fpu z, ref Fpu w)
        {
            Fpu s = (Fpu)1.0 / Fpu.Sqrt((((z * z) + (y * y)) + (x * x)) + (w * w));
            x = new Fpu((x * s).F);
            y = new Fpu((y * s).F);
            z = new Fpu((z * s).F);
            w *= s;
        }

        public static (Vector3f, Vector3f, Vector3f) SumForces(PointForce[] forces, float[] m, float inverseMass, Vector3f v, Vector3f l)
        {
            Fpu fx = 0f, fy = 0f, fz = 0f, tx = 0f, ty = 0f, tz = 0f;
            foreach (PointForce force in forces)
            {
                Vector3f f = force.Force, p = force.Point;
                fx += f.X;
                fy += f.Y;
                fz += f.Z;
                if (!IsZero(p) && !IsZero(f))
                {
                    tx += ((Fpu)p.Y * f.Z) - ((Fpu)p.Z * f.Y);
                    ty += -(((Fpu)f.Z * p.X) - ((Fpu)p.Z * f.X));
                    tz += ((Fpu)f.Y * p.X) - ((Fpu)p.Y * f.X);
                }
            }

            Vector3f total = new(fx.F, fy.F, fz.F);
            Fpu ax = fx * inverseMass, ay = (Fpu)inverseMass * fy.F, az = (Fpu)inverseMass * fz.F;
            float rx = (((az * m[2]) + (ay * m[1])) + (ax * m[0])).F;
            float ry = (((az * m[5]) + (ax * m[3])) + (ay * m[4])).F;
            float rz = (((ay * m[7]) + (ax * m[6])) + (az * m[8])).F;
            Vector3f velocity = new(((Fpu)rx + v.X).F, ((Fpu)ry + v.Y).F, ((Fpu)rz + v.Z).F);
            Vector3f momentum = new((tx + l.X).F, (ty + l.Y).F, (tz + l.Z).F);
            return (total, velocity, momentum);
        }

        public static Vector3f PointVelocity(Vector3f c, Vector3f w, Vector3f arm)
        {
            float x = (((Fpu)arm.Z * w.Y) - ((Fpu)w.Z * arm.Y)).F;
            float y = (-(((Fpu)arm.Z * w.X) - ((Fpu)w.Z * arm.X))).F;
            float z = (((Fpu)w.X * arm.Y) - ((Fpu)arm.X * w.Y)).F;
            return new Vector3f(((Fpu)c.X + x).F, ((Fpu)c.Y + y).F, ((Fpu)c.Z + z).F);
        }

        public static Vector3f Normalised(Vector3f v) =>
            IsZero(v) ? v : Scale(v, (Fpu)1.0 / Fpu.Sqrt((((Fpu)v.X * v.X) + ((Fpu)v.Y * v.Y)) + ((Fpu)v.Z * v.Z)));

        public static bool IsIdle(int contacts, bool moved, int forces, float speed, float restSpeed, Vector3f w, float restSpin)
        {
            if (contacts <= 0 || moved || forces != 0 || !(speed < restSpeed))
            {
                return false;
            }

            Fpu spin = (((Fpu)w.X * w.X) + ((Fpu)w.Y * w.Y)) + ((Fpu)w.Z * w.Z);
            return spin.Value < restSpin;
        }

        private static Vector3f Scale(Vector3f v, Fpu s) => new(((Fpu)v.X * s).F, ((Fpu)v.Y * s).F, ((Fpu)v.Z * s).F);

        private static bool IsZero(Vector3f v) => v.X == 0f && v.Y == 0f && v.Z == 0f;
    }
}
