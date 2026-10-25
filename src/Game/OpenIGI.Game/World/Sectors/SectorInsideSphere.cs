using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World.Sectors;

/// <summary>
/// A sector object's optimal inside sphere: <c>SectorObject_GetOptimalInsideSphere</c>, the same function in both engines
/// (<c>IGI.exe 0x4DF500</c>; <c>igi2 e/3/sectorobject.c</c>).
/// </summary>
/// <remarks>
/// <para>
/// One sphere that encloses every room of a building and everything the rooms carry. Each room's mesh sphere
/// (<see cref="MefModel.BoundingSphereCentre"/>) is collected, then each of its attach objects' in turn, all <b>in their own
/// model space</b> -- the original never moves them into the building's frame -- and the two nearest are merged until one is
/// left. A building with no room is given its own mesh's sphere.
/// </para>
/// <para>
/// Project I.G.I. reads it while drawing: an object farther than its radius from its centre is outside the building the camera
/// stands in (<c>QCube_CallHandlers</c>, <c>IGI.exe 0x4D12C0</c>). I.G.I. 2 still computes and copies it but no longer reads it.
/// </para>
/// </remarks>
public static class SectorInsideSphere
{
    /// <summary>Merges spheres into one, as the original does.</summary>
    /// <param name="spheres">The spheres, in the order they were collected.</param>
    /// <returns>The single sphere left, or <see langword="null"/> when there were none.</returns>
    /// <remarks>
    /// <para>
    /// Each round finds the closest pair by centre, the first strictly nearer in collection order winning a tie. Two spheres
    /// with one centre keep the larger radius; a sphere inside the other is dropped; otherwise the first becomes the sphere
    /// spanning both -- centred halfway between their far edges along the line joining them -- and the second is dropped.
    /// </para>
    /// <para>
    /// Dropping moves the last sphere into the dropped one's place, which is how the original's pool removes an element, and is
    /// what decides which pair is met first in the next round.
    /// </para>
    /// </remarks>
    public static (Vector3f Centre, float Radius)? Merge(IEnumerable<(Vector3f Centre, float Radius)> spheres)
    {
        ArgumentNullException.ThrowIfNull(spheres);
        List<(Vector3f Centre, float Radius)> pool = [.. spheres];
        if (pool.Count == 0)
        {
            return null;
        }

        while (pool.Count > 1)
        {
            int first = -1;
            int second = -1;
            double nearest = float.MaxValue;
            for (int i = 0; i < pool.Count; i++)
            {
                for (int j = 0; j < pool.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    // In double, as the original's x87 measures the float centres.
                    Vector3f ci = pool[i].Centre, cj = pool[j].Centre;
                    double distance = Vector3d.Distance(new Vector3d(ci.X, ci.Y, ci.Z), new Vector3d(cj.X, cj.Y, cj.Z));
                    if (distance < nearest)
                    {
                        nearest = distance;
                        first = i;
                        second = j;
                    }
                }
            }

            (Vector3f a, float ra) = pool[first];
            (Vector3f b, float rb) = pool[second];
            if (nearest == 0.0)
            {
                pool[first] = (a, MathF.Max(ra, rb));
                Drop(pool, second);
            }
            else if (nearest + rb < ra)
            {
                Drop(pool, second);
            }
            else if (nearest + ra < rb)
            {
                Drop(pool, first);
            }
            else
            {
                double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
                double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                double near = ra / length;
                double far = rb / length;
                double x0 = a.X - (dx * near), y0 = a.Y - (dy * near), z0 = a.Z - (dz * near);
                double x1 = b.X + (dx * far), y1 = b.Y + (dy * far), z1 = b.Z + (dz * far);
                float cx = (float)(((x1 - x0) * 0.5) + x0);
                float cy = (float)(((y1 - y0) * 0.5) + y0);
                float cz = (float)(((z1 - z0) * 0.5) + z0);
                float radius = (float)Math.Sqrt(((x1 - cx) * (x1 - cx)) + ((y1 - cy) * (y1 - cy)) + ((z1 - cz) * (z1 - cz)));
                pool[first] = (new Vector3f(cx, cy, cz), radius);
                Drop(pool, second);
            }
        }

        return pool[0];
    }

    private static void Drop(List<(Vector3f Centre, float Radius)> pool, int index)
    {
        int last = pool.Count - 1;
        pool[index] = pool[last];
        pool.RemoveAt(last);
    }
}
