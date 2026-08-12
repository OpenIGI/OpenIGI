using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>Conservative clip-frustum tests shared by camera and shadow raster passes.</summary>
public static class FrustumCulling
{
    /// <summary>Tests a local-space sphere against all six homogeneous clip planes.</summary>
    /// <param name="clipFromLocal">The complete local-to-clip transform.</param>
    /// <param name="sphere">The conservative local-space bound.</param>
    /// <returns><see langword="true"/> when the sphere intersects or lies inside the frustum.</returns>
    public static bool SphereVisible(in Matrix4f clipFromLocal, in BoundingSphere sphere)
    {
        // OpenGL clip volume: -w <= x,y,z <= w. A sphere touching a plane remains visible.
        return Inside(clipFromLocal.M41 + clipFromLocal.M11, clipFromLocal.M42 + clipFromLocal.M12, clipFromLocal.M43 + clipFromLocal.M13, clipFromLocal.M44 + clipFromLocal.M14, sphere)
            && Inside(clipFromLocal.M41 - clipFromLocal.M11, clipFromLocal.M42 - clipFromLocal.M12, clipFromLocal.M43 - clipFromLocal.M13, clipFromLocal.M44 - clipFromLocal.M14, sphere)
            && Inside(clipFromLocal.M41 + clipFromLocal.M21, clipFromLocal.M42 + clipFromLocal.M22, clipFromLocal.M43 + clipFromLocal.M23, clipFromLocal.M44 + clipFromLocal.M24, sphere)
            && Inside(clipFromLocal.M41 - clipFromLocal.M21, clipFromLocal.M42 - clipFromLocal.M22, clipFromLocal.M43 - clipFromLocal.M23, clipFromLocal.M44 - clipFromLocal.M24, sphere)
            && Inside(clipFromLocal.M41 + clipFromLocal.M31, clipFromLocal.M42 + clipFromLocal.M32, clipFromLocal.M43 + clipFromLocal.M33, clipFromLocal.M44 + clipFromLocal.M34, sphere)
            && Inside(clipFromLocal.M41 - clipFromLocal.M31, clipFromLocal.M42 - clipFromLocal.M32, clipFromLocal.M43 - clipFromLocal.M33, clipFromLocal.M44 - clipFromLocal.M34, sphere);
    }

    private static bool Inside(float x, float y, float z, float w, in BoundingSphere sphere)
    {
        float distance = (x * sphere.Centre.X) + (y * sphere.Centre.Y) + (z * sphere.Centre.Z) + w;
        float normalLength = MathF.Sqrt((x * x) + (y * y) + (z * z));
        return distance >= -(sphere.Radius * normalLength);
    }
}
