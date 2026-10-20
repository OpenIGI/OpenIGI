using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Tests.Oracles;
using OpenIGI.Game.World;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The engine's shared model-ray core against each game's own, which it replaced: I.G.I. 2's mesh search verbatim
/// (<see cref="OracleIgi2MeshSearch"/>) and Project I.G.I.'s face, sphere and box tests (<see cref="Igi1"/>), bit for bit.
/// </summary>
public sealed class MeshRaySearchTests
{
    private const int Cases = 40000;

    [Fact]
    public void Faces_are_crossed_as_each_game_crossed_them()
    {
        Random random = new(5);
        int crossed2 = 0, crossed1 = 0;
        for (int c = 0; c < Cases; c++)
        {
            (float[] v, Vector3f s, Vector3f e) = Scene(random);
            Vector3f d = new(((Fpu)e.X - s.X).F, ((Fpu)e.Y - s.Y).F, ((Fpu)e.Z - s.Z).F);
            bool flip = random.Next(4) == 0;

            foreach (OracleIgi2PlaneSum sum in new[] { OracleIgi2PlaneSum.YXZ, OracleIgi2PlaneSum.ZYX, OracleIgi2PlaneSum.XZY })
            {
                OracleIgi2MeshSearch old = new();
                old.Box(s, e);
                MeshRaySearch<Fpu> shared = new(MeshRayDialect.Igi2);
                shared.Box(s, e);
                Assert.Equal((old.Min, old.Max), (shared.Min, shared.Max));
                bool was = old.Cross(v, 0, 3, 6, s, d, flip, sum, out OracleIgi2FaceCrossing a);
                bool now = shared.Cross(v, 0, 3, 6, s, d, flip, (MeshPlaneSum)(int)sum, out MeshFaceCrossing<Fpu> b);
                Assert.Equal(was, now);
                if (was)
                {
                    crossed2++;
                    Assert.Equal((a.Dot.Value, a.T, a.Point, a.U.Value, a.V.Value), (b.Dot.Value, b.T, b.Point, b.U.Value, b.V.Value));
                }
            }

            Vector3d sd = new(s.X, s.Y, s.Z), ed = new(e.X, e.Y, e.Z);
            float[] ls = [s.X, s.Y, s.Z];
            float[] lo = new float[3], hi = new float[3];
            Igi1.Box(ls, [e.X, e.Y, e.Z], lo, hi);
            MeshRaySearch<X87> one = new(MeshRayDialect.Igi1);
            one.Box(s, e);
            Assert.Equal((new Vector3f(lo[0], lo[1], lo[2]), new Vector3f(hi[0], hi[1], hi[2])), (one.Min, one.Max));
            float[] d1 = [((X87)e.X - s.X).F, ((X87)e.Y - s.Y).F, ((X87)e.Z - s.Z).F];
            bool oldHit = Igi1.Cross(v, ls, d1, lo, hi, flip, out float oldT, out float[] oldPoint);
            bool newHit = one.Cross(v, 0, 3, 6, s, new Vector3f(d1[0], d1[1], d1[2]), flip, MeshPlaneSum.ZYX, out MeshFaceCrossing<X87> crossing);
            Assert.Equal(oldHit, newHit);
            if (oldHit)
            {
                crossed1++;
                Assert.Equal((oldT, new Vector3f(oldPoint[0], oldPoint[1], oldPoint[2])), (crossing.T, crossing.Point));
            }

            _ = sd;
            _ = ed;
        }

        Assert.True(crossed2 > Cases / 20 && crossed1 > Cases / 60, $"too few crossings to compare: {crossed2} and {crossed1}");
    }

    [Fact]
    public void Spheres_are_met_as_each_game_met_them()
    {
        Random random = new(6);
        int met = 0;
        for (int c = 0; c < Cases; c++)
        {
            (_, Vector3f s, Vector3f e) = Scene(random);
            Vector3f d = new(((Fpu)e.X - s.X).F, ((Fpu)e.Y - s.Y).F, ((Fpu)e.Z - s.Z).F);
            float[] sp = [Value(random, 3000f), Value(random, 3000f), Value(random, 3000f), MathF.Abs(Value(random, 1500f))];
            if (random.Next(50) == 0)
            {
                sp[random.Next(4)] = float.NaN;
            }

            Fpu inverse2 = (Fpu)1.0 / (((Fpu)d.Z * d.Z) + ((Fpu)d.X * d.X) + ((Fpu)d.Y * d.Y));
            foreach (bool sibling in new[] { false, true })
            {
                OracleIgi2MeshSearch old = new();
                old.Box(s, e);
                MeshRaySearch<Fpu> shared = new(MeshRayDialect.Igi2);
                shared.Box(s, e);
                Assert.Equal(old.SphereOk(sp, 0, s, d, inverse2, sibling), shared.Meets(sp, 0, s, d, inverse2, sibling));
            }

            float[] ls = [s.X, s.Y, s.Z];
            float[] lo = new float[3], hi = new float[3];
            Igi1.Box(ls, [e.X, e.Y, e.Z], lo, hi);
            float[] d1 = [((X87)e.X - s.X).F, ((X87)e.Y - s.Y).F, ((X87)e.Z - s.Z).F];
            X87 len2 = (X87)d1[0] * d1[0] + (X87)d1[1] * d1[1] + (X87)d1[2] * d1[2];
            double inverse1 = ((X87)1.0 / len2).Value;
            MeshRaySearch<X87> one = new(MeshRayDialect.Igi1);
            one.Box(s, e);
            bool meets = Igi1.SphereOk(sp, 0, ls, d1, inverse1, lo, hi);
            met += meets ? 1 : 0;
            Assert.Equal(meets, one.Meets(sp, 0, s, new Vector3f(d1[0], d1[1], d1[2]), inverse1, xyz: false));
        }

        Assert.True(met > Cases / 40, $"too few spheres met to compare: {met}");
    }

    // A triangle near a short line, sometimes degenerate, flat on an axis, or holding a value that is not a number.
    private static (float[] V, Vector3f S, Vector3f E) Scene(Random random)
    {
        Vector3f s = new(Value(random, 2000f), Value(random, 2000f), Value(random, 2000f));
        Vector3f e = new(Value(random, 2000f), Value(random, 2000f), Value(random, 2000f));
        float[] v = new float[9];
        for (int i = 0; i < 9; i++)
        {
            v[i] = Value(random, 2500f);
        }

        switch (random.Next(12))
        {
            case 0:
                v[6] = v[0];
                v[7] = v[1];
                v[8] = v[2];
                break;
            case 1:
                v[2] = v[5] = v[8] = Value(random, 2500f);
                break;
            case 2:
                v[random.Next(9)] = float.NaN;
                break;
            case 3:
                e = s;
                break;
        }

        return (v, s, e);
    }

    private static float Value(Random random, float scale) => (float)((random.NextDouble() - 0.5) * 2.0 * scale);

    // Project I.G.I.'s RetailModelRay (0x4C1840) as it stood before the merge: its box, its sphere test, and its face test up to
    // the tie rules, which the shared Accept carries unchanged.
    private static class Igi1
    {
        private const float BoxPad = 0.1f;

        public static void Box(float[] ls, float[] p, float[] lo, float[] hi)
        {
            for (int k = 0; k < 3; k++)
            {
                lo[k] = ((X87)(ls[k] < p[k] ? ls[k] : p[k]) - BoxPad).F;
                hi[k] = ((X87)(ls[k] <= p[k] ? p[k] : ls[k]) + BoxPad).F;
            }
        }

        public static bool SphereOk(float[] sp, int n, float[] ls, float[] d, double inverse, float[] lo, float[] hi)
        {
            float cx = sp[4 * n], cy = sp[(4 * n) + 1], cz = sp[(4 * n) + 2], rr = sp[(4 * n) + 3];
            X87 ex = (X87)cx - ls[0], ey = (X87)cy - ls[1], ez = (X87)cz - ls[2];
            X87 proj = ez * d[2] + ey * d[1] + ex * d[0];
            X87 c2 = ez * ez + ex * ex + ey * ey;
            X87 d2 = c2 - (proj * proj * inverse);
            if (!(((X87)rr * rr).Value >= d2.Value))
            {
                return false;
            }

            if (((X87)rr + cx).Value < lo[0] || ((X87)cy + rr).Value < lo[1] || ((X87)cz + rr).Value < lo[2])
            {
                return false;
            }

            return ((X87)cx - rr).Value <= hi[0] && ((X87)cy - rr).Value <= hi[1] && ((X87)cz - rr).Value <= hi[2];
        }

        public static bool Cross(float[] v, float[] ls, float[] d, float[] lo, float[] hi, bool mirror, out float tfOut, out float[] point)
        {
            tfOut = 0f;
            point = [];
            const int i0 = 0, i1 = 1, i2 = 2;
            for (int k = 0; k < 3; k++)
            {
                if (!(lo[k] <= v[(3 * i0) + k] || lo[k] <= v[(3 * i1) + k] || lo[k] <= v[(3 * i2) + k]))
                {
                    return false;
                }
            }

            for (int k = 0; k < 3; k++)
            {
                if (!(hi[k] >= v[(3 * i0) + k] || hi[k] >= v[(3 * i1) + k] || hi[k] >= v[(3 * i2) + k]))
                {
                    return false;
                }
            }

            float v0x = v[3 * i0], v0y = v[(3 * i0) + 1], v0z = v[(3 * i0) + 2];
            float ax = ((X87)v[3 * i2] - v0x).F, ay = ((X87)v[(3 * i2) + 1] - v0y).F, az = ((X87)v[(3 * i2) + 2] - v0z).F;
            float bx = ((X87)v[3 * i1] - v0x).F, by = ((X87)v[(3 * i1) + 1] - v0y).F;
            X87 bzr = (X87)v[(3 * i1) + 2] - v0z;
            float bz = bzr.F;
            float nx = (bzr * ay - (X87)by * az).F;
            float nyp = ((X87)bz * ax - (X87)bx * az).F;
            float nz = ((X87)by * ax - (X87)bx * ay).F;
            float tnx = nx, tny = -nyp, tnz = nz;
            if (mirror)
            {
                tnx = -tnx;
                tny = -tny;
                tnz = -tnz;
            }

            X87 den = (X87)tnz * d[2] + (X87)tny * d[1] + (X87)tnx * d[0];
            if (den.Value == 0d || double.IsNaN(den.Value))
            {
                return false;
            }

            X87 num = ((X87)v0z - ls[2]) * tnz + ((X87)v0y - ls[1]) * tny + ((X87)v0x - ls[0]) * tnx;
            X87 t = num / den;
            if (t.Value < 0d)
            {
                return false;
            }

            float tf = t.F;
            if (!(tf <= 1.0f))
            {
                return false;
            }

            float pcx = ((X87)tf * d[0] + ls[0]).F, pcy = ((X87)tf * d[1] + ls[1]).F, pcz = ((X87)tf * d[2] + ls[2]).F;
            float fax = MathF.Abs(tnx), fay = MathF.Abs(tny), faz = MathF.Abs(tnz);
            X87 s1, s2;
            if (fax > fay && fax > faz)
            {
                if (nx == 0f)
                {
                    return false;
                }

                float qzv = ((X87)pcz - v0z).F, qyv = ((X87)pcy - v0y).F;
                s2 = ((X87)qzv * ay - (X87)qyv * az) / nx;
                if (s2.Value < 0d || !(s2.Value <= 1d))
                {
                    return false;
                }

                if (MathF.Abs(ay) > MathF.Abs(az))
                {
                    s1 = ((X87)qyv - (X87)by * s2) / ay;
                }
                else
                {
                    if (az == 0f)
                    {
                        return false;
                    }

                    s1 = ((X87)qzv - (X87)bz * s2) / az;
                }
            }
            else if (fay > faz)
            {
                if (nyp == 0f)
                {
                    return false;
                }

                float qzv = ((X87)pcz - v0z).F, qxv = ((X87)pcx - v0x).F;
                s2 = ((X87)qzv * ax - (X87)qxv * az) / nyp;
                if (s2.Value < 0d || !(s2.Value <= 1d))
                {
                    return false;
                }

                if (MathF.Abs(ax) > MathF.Abs(az))
                {
                    s1 = ((X87)qxv - (X87)bx * s2) / ax;
                }
                else
                {
                    if (az == 0f)
                    {
                        return false;
                    }

                    s1 = ((X87)qzv - (X87)bz * s2) / az;
                }
            }
            else
            {
                if (nz == 0f)
                {
                    return false;
                }

                float qyv = ((X87)pcy - v0y).F, qxv = ((X87)pcx - v0x).F;
                s2 = ((X87)qyv * ax - (X87)qxv * ay) / nz;
                if (s2.Value < 0d || !(s2.Value <= 1d))
                {
                    return false;
                }

                if (MathF.Abs(ax) > MathF.Abs(ay))
                {
                    s1 = ((X87)qxv - (X87)bx * s2) / ax;
                }
                else
                {
                    if (ay == 0f)
                    {
                        return false;
                    }

                    s1 = ((X87)qyv - (X87)by * s2) / ay;
                }
            }

            if (s1.Value < 0d || !((s1 + s2).Value <= 1d))
            {
                return false;
            }

            tfOut = tf;
            point = [pcx, pcy, pcz];
            return true;
        }
    }
}
