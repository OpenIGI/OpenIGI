using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Collision;

/// <summary>
/// What I.G.I. 2's line handlers record of a struck face beyond the engine's shared search (<see cref="MeshRaySearch{TFpu}"/>):
/// its unit normal, how lit it is, and the row an absent lightmap, portal or material is stored as.
/// </summary>
internal static class Igi2MeshSearch
{
    /// <summary>The value an absent lightmap, portal or material row is stored as.</summary>
    public const int None = 0xFFFF;

    /// <summary>
    /// How lit a struck surface is: zero on a lightmapped building face, else the level's ambient plus the sun on the
    /// face, the three channels averaged.
    /// </summary>
    /// <param name="lightmappedFace">Whether the face is a building face with a lightmap the object carries.</param>
    /// <param name="normal">The face's unit normal in the world.</param>
    /// <param name="lighting">The level's light, or <see langword="null"/> for none.</param>
    /// <returns>The light, <c>vLight</c>.</returns>
    public static float Light(bool lightmappedFace, Vector3f normal, IWorldSceneLighting? lighting)
    {
        if (lightmappedFace)
        {
            return 0f;
        }

        Vector3f ambient = lighting?.Ambient ?? default;
        float lx = ambient.X, ly = ambient.Y;
        Fpu lz = ambient.Z;
        if (lighting is not null && lighting.TryGetSun(out Vector3f direction, out Vector3f colour))
        {
            Fpu dot = ((Fpu)direction.Z * normal.Z) + ((Fpu)direction.Y * normal.Y) + ((Fpu)normal.X * direction.X);
            if (dot.Value > 0.0)
            {
                lx = ((dot * colour.X) + lx).F;
                ly = ((dot * colour.Y) + ly).F;
                lz = (dot * colour.Z) + lz;
            }
        }

        return ((lz + ly + lx) * 0.33333334f).F;
    }

    /// <summary>A face's normal from its edges, normalised: the handlers' record of the struck face.</summary>
    /// <param name="a2x">The third vertex less the first, x, as the FPU holds it.</param>
    /// <param name="a2y">Its y.</param>
    /// <param name="a2z">Its z.</param>
    /// <param name="b1x">The second vertex less the first, x, stored.</param>
    /// <param name="b1y">Its y, stored.</param>
    /// <param name="b1z">Its z, as the FPU holds it.</param>
    /// <returns>The unit normal; a zero normal stays zero.</returns>
    public static Vector3f UnitNormal(Fpu a2x, Fpu a2y, Fpu a2z, float b1x, float b1y, Fpu b1z)
    {
        float nx = ((b1z * a2y) - ((Fpu)b1y * a2z)).F;
        float ny = (-((b1z * a2x) - ((Fpu)b1x * a2z))).F;
        float nz = (((Fpu)b1y * a2x) - ((Fpu)b1x * a2y)).F;
        if (nx != 0f || ny != 0f || nz != 0f)
        {
            Fpu length = Fpu.Sqrt(((Fpu)nx * nx) + ((Fpu)ny * ny) + ((Fpu)nz * nz));
            Fpu inverse = (Fpu)1.0 / length;
            nx = ((Fpu)nx * inverse).F;
            ny = ((Fpu)ny * inverse).F;
            nz = ((Fpu)nz * inverse).F;
        }

        return new Vector3f(nx, ny, nz);
    }
}
