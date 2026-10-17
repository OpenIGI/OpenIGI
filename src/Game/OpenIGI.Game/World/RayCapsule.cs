using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>Ray against a capsule — the analytic human body test used to make soldiers solid.</summary>
/// <remarks>
/// <para>
/// A capsule is the set of points within <c>radius</c> of the axis
/// <c>axisA..axisB</c>. This is the same 0.55 metre by 1.8 metre shape
/// <see cref="Player.HumanSeparation"/> uses for human-versus-human separation, so a soldier's
/// collision body matches the body the renderer already uses to keep two soldiers apart.
/// </para>
/// <para>
/// Like <see cref="RayTriangle"/>, this is geometry, not a transcription: the original never needed a
/// capsule because its human bodies were registered collision meshes. It exists so the player's existing
/// wall sweep can treat a living soldier as a wall, which is the behaviour retail got from that mesh
/// registration.
/// </para>
/// </remarks>
public static class RayCapsule
{
    /// <summary>Intersects a ray segment with a capsule.</summary>
    /// <param name="origin">The ray's start.</param>
    /// <param name="direction">The segment vector, <c>end - origin</c>; need not be unit length.</param>
    /// <param name="axisA">One end of the capsule's central axis.</param>
    /// <param name="axisB">The other end of the capsule's central axis.</param>
    /// <param name="radius">The capsule's radius.</param>
    /// <param name="distance">
    /// Receives the hit's distance as a fraction of <paramref name="direction"/>, matching
    /// <see cref="RayTriangle.Intersect"/>'s convention, so a value in <c>[0, 1)</c> lies within the
    /// segment.
    /// </param>
    /// <param name="normal">
    /// Receives the outward, ray-facing unit normal at the point of contact. On the cylindrical side
    /// this is horizontal, so <see cref="Player.HumanWallSweep"/> treats the soldier as a wall.
    /// </param>
    /// <returns><see langword="true"/> when the segment meets the capsule.</returns>
    public static bool Intersect(
        Vector3d origin,
        Vector3d direction,
        Vector3d axisA,
        Vector3d axisB,
        double radius,
        out double distance,
        out Vector3d normal)
    {
        distance = 0.0;
        normal = default;

        Vector3d axis = new(axisB.X - axisA.X, axisB.Y - axisA.Y, axisB.Z - axisA.Z);
        double length = Math.Sqrt((axis.X * axis.X) + (axis.Y * axis.Y) + (axis.Z * axis.Z));
        if (length <= 0.0 || radius <= 0.0)
        {
            return false;
        }

        // An orthonormal basis with z along the capsule axis.
        Vector3d z = new(axis.X / length, axis.Y / length, axis.Z / length);
        Vector3d hint = Math.Abs(z.Z) < 0.99 ? new Vector3d(0.0, 0.0, 1.0) : new Vector3d(1.0, 0.0, 0.0);
        Vector3d x = Vector3d.Cross(z, hint);
        double xLen = Math.Sqrt((x.X * x.X) + (x.Y * x.Y) + (x.Z * x.Z));
        if (xLen <= 0.0)
        {
            return false;
        }

        x = new Vector3d(x.X / xLen, x.Y / xLen, x.Z / xLen);
        Vector3d y = Vector3d.Cross(z, x);

        // The ray in that frame, measured from axisA.
        double ox = Dot(origin.X - axisA.X, origin.Y - axisA.Y, origin.Z - axisA.Z, x);
        double oy = Dot(origin.X - axisA.X, origin.Y - axisA.Y, origin.Z - axisA.Z, y);
        double oz = Dot(origin.X - axisA.X, origin.Y - axisA.Y, origin.Z - axisA.Z, z);
        double dx = Dot(direction.X, direction.Y, direction.Z, x);
        double dy = Dot(direction.X, direction.Y, direction.Z, y);
        double dz = Dot(direction.X, direction.Y, direction.Z, z);

        double best = double.MaxValue;
        Vector3d bestNormal = default;

        // The outward normal at a contact is (contact - nearest point on the axis) / radius, which
        // holds uniformly for the cylindrical side and both hemispherical caps. t is a fraction of the
        // segment, so a hit at or past the segment's end (t >= 1) is no obstacle — matching the ray
        // convention the mesh casts use.
        void Consider(double t, double px, double py, double pz)
        {
            if (t < 0.0 || t >= 1.0 || t >= best)
            {
                return;
            }

            double clamped = Math.Clamp(pz, 0.0, length);
            double nr = Math.Sqrt((px * px) + (py * py) + ((pz - clamped) * (pz - clamped)));
            if (nr <= 0.0)
            {
                return;
            }

            best = t;
            bestNormal = new Vector3d(
                ((px / nr) * x.X) + ((py / nr) * y.X) + (((pz - clamped) / nr) * z.X),
                ((px / nr) * x.Y) + ((py / nr) * y.Y) + (((pz - clamped) / nr) * z.Y),
                ((px / nr) * x.Z) + ((py / nr) * y.Z) + (((pz - clamped) / nr) * z.Z));
        }

        // Cylindrical side: px^2 + py^2 = radius^2, clipped to 0 <= pz <= length.
        double a = (dx * dx) + (dy * dy);
        if (a > 1e-12)
        {
            double b = 2.0 * ((ox * dx) + (oy * dy));
            double c = (ox * ox) + (oy * oy) - (radius * radius);
            double disc = (b * b) - (4.0 * a * c);
            if (disc >= 0.0)
            {
                double s = Math.Sqrt(disc);
                double t1 = (-b - s) / (2.0 * a);
                double t2 = (-b + s) / (2.0 * a);
                double pz1 = oz + (dz * t1);
                double pz2 = oz + (dz * t2);
                if (pz1 >= 0.0 && pz1 <= length)
                {
                    Consider(t1, ox + (dx * t1), oy + (dy * t1), pz1);
                }

                if (pz2 >= 0.0 && pz2 <= length)
                {
                    Consider(t2, ox + (dx * t2), oy + (dy * t2), pz2);
                }
            }
        }

        // Bottom cap: sphere centred at axisA (local 0,0,0), taken where pz <= 0.
        if (!SolveSphere(ox, oy, oz, dx, dy, dz, radius, out double bt, out double bp))
        {
            bt = double.MaxValue;
            bp = 0.0;
        }

        if (bt < best && (oz + (dz * bt)) <= 0.0)
        {
            Consider(bt, ox + (dx * bt), oy + (dy * bt), oz + (dz * bt));
        }

        // Top cap: sphere centred at axisB (local 0,0,length), taken where pz >= length.
        if (!SolveSphere(ox, oy, oz - length, dx, dy, dz, radius, out double tt, out double tp))
        {
            tt = double.MaxValue;
            tp = 0.0;
        }

        if (tt < best && (oz + (dz * tt)) >= length)
        {
            Consider(tt, ox + (dx * tt), oy + (dy * tt), oz + (dz * tt));
        }

        if (best < double.MaxValue)
        {
            distance = best;
            normal = bestNormal;
            return true;
        }

        return false;
    }

    private static bool SolveSphere(
        double ox,
        double oy,
        double oz,
        double dx,
        double dy,
        double dz,
        double radius,
        out double distance,
        out double branch)
    {
        distance = 0.0;
        branch = 0.0;
        double a = (dx * dx) + (dy * dy) + (dz * dz);
        if (a <= 1e-12)
        {
            return false;
        }

        double b = 2.0 * ((ox * dx) + (oy * dy) + (oz * dz));
        double c = (ox * ox) + (oy * oy) + (oz * oz) - (radius * radius);
        double disc = (b * b) - (4.0 * a * c);
        if (disc < 0.0)
        {
            return false;
        }

        double s = Math.Sqrt(disc);
        double t1 = (-b - s) / (2.0 * a);
        double t2 = (-b + s) / (2.0 * a);
        if (t1 >= 0.0)
        {
            distance = t1;
            branch = t2;
            return true;
        }

        if (t2 >= 0.0)
        {
            distance = t2;
            branch = t1;
            return true;
        }

        return false;
    }

    private static double Dot(double x, double y, double z, Vector3d axis) =>
        (x * axis.X) + (y * axis.Y) + (z * axis.Z);
}
