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

    /// <summary>A pass's six clip planes in view space, extracted and normalised once.</summary>
    /// <remarks>
    /// <see cref="SphereVisible(in Matrix4f, in BoundingSphere)"/> multiplies the projection into
    /// every object's transform and normalises six planes per test — per placement, per pass. When
    /// one projection tests hundreds of spheres, extracting its planes once and testing in view
    /// space is the same predicate at a fraction of the arithmetic. Valid for rigid model-view
    /// transforms, which is what <c>Camera3D.CreateModelView</c> produces.
    /// </remarks>
    public readonly struct Frustum
    {
        private readonly float _ax, _ay, _az, _aw;
        private readonly float _bx, _by, _bz, _bw;
        private readonly float _cx, _cy, _cz, _cw;
        private readonly float _dx, _dy, _dz, _dw;
        private readonly float _ex, _ey, _ez, _ew;
        private readonly float _fx, _fy, _fz, _fw;

        private Frustum(Span<float> planes)
        {
            _ax = planes[0]; _ay = planes[1]; _az = planes[2]; _aw = planes[3];
            _bx = planes[4]; _by = planes[5]; _bz = planes[6]; _bw = planes[7];
            _cx = planes[8]; _cy = planes[9]; _cz = planes[10]; _cw = planes[11];
            _dx = planes[12]; _dy = planes[13]; _dz = planes[14]; _dw = planes[15];
            _ex = planes[16]; _ey = planes[17]; _ez = planes[18]; _ew = planes[19];
            _fx = planes[20]; _fy = planes[21]; _fz = planes[22]; _fw = planes[23];
        }

        /// <summary>Extracts the six planes of a view-to-clip transform.</summary>
        /// <param name="projection">The pass's projection.</param>
        /// <returns>The frustum, in the projection's own view space.</returns>
        public static Frustum FromProjection(in Matrix4f projection)
        {
            Span<float> planes = stackalloc float[24];
            Write(planes[..4], projection.M41 + projection.M11, projection.M42 + projection.M12, projection.M43 + projection.M13, projection.M44 + projection.M14);
            Write(planes[4..8], projection.M41 - projection.M11, projection.M42 - projection.M12, projection.M43 - projection.M13, projection.M44 - projection.M14);
            Write(planes[8..12], projection.M41 + projection.M21, projection.M42 + projection.M22, projection.M43 + projection.M23, projection.M44 + projection.M24);
            Write(planes[12..16], projection.M41 - projection.M21, projection.M42 - projection.M22, projection.M43 - projection.M23, projection.M44 - projection.M24);
            Write(planes[16..20], projection.M41 + projection.M31, projection.M42 + projection.M32, projection.M43 + projection.M33, projection.M44 + projection.M34);
            Write(planes[20..24], projection.M41 - projection.M31, projection.M42 - projection.M32, projection.M43 - projection.M33, projection.M44 - projection.M34);
            return new Frustum(planes);
        }

        /// <summary>Tests a local sphere carried into view space by a rigid transform.</summary>
        /// <param name="viewFromLocal">The object's model-view; must not scale.</param>
        /// <param name="sphere">The conservative local-space bound.</param>
        /// <returns><see langword="true"/> when the sphere intersects or lies inside the frustum.</returns>
        public bool SphereVisible(in Matrix4f viewFromLocal, in BoundingSphere sphere)
        {
            float lx = sphere.Centre.X;
            float ly = sphere.Centre.Y;
            float lz = sphere.Centre.Z;
            float x = (viewFromLocal.M11 * lx) + (viewFromLocal.M12 * ly) + (viewFromLocal.M13 * lz) + viewFromLocal.M14;
            float y = (viewFromLocal.M21 * lx) + (viewFromLocal.M22 * ly) + (viewFromLocal.M23 * lz) + viewFromLocal.M24;
            float z = (viewFromLocal.M31 * lx) + (viewFromLocal.M32 * ly) + (viewFromLocal.M33 * lz) + viewFromLocal.M34;
            return SphereVisible(x, y, z, sphere.Radius);
        }

        /// <summary>Tests a sphere already in the frustum's own view space.</summary>
        /// <param name="x">The centre's view-space X.</param>
        /// <param name="y">The centre's view-space Y.</param>
        /// <param name="z">The centre's view-space Z.</param>
        /// <param name="radius">The sphere's radius.</param>
        /// <returns><see langword="true"/> when the sphere intersects or lies inside the frustum.</returns>
        public bool SphereVisible(float x, float y, float z, float radius)
        {
            float r = -radius;

            return (_ax * x) + (_ay * y) + (_az * z) + _aw >= r
                && (_bx * x) + (_by * y) + (_bz * z) + _bw >= r
                && (_cx * x) + (_cy * y) + (_cz * z) + _cw >= r
                && (_dx * x) + (_dy * y) + (_dz * z) + _dw >= r
                && (_ex * x) + (_ey * y) + (_ez * z) + _ew >= r
                && (_fx * x) + (_fy * y) + (_fz * z) + _fw >= r;
        }

        private static void Write(Span<float> plane, float x, float y, float z, float w)
        {
            float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
            float inverse = length > 0f ? 1f / length : 0f;
            plane[0] = x * inverse;
            plane[1] = y * inverse;
            plane[2] = z * inverse;
            plane[3] = w * inverse;
        }
    }
}
