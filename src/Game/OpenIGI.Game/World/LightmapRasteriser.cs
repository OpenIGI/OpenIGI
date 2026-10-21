using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// Turns a placed object's lightmapped draw records into texel maps: each triangle rasterised into its
/// lightmap page's UV space, every covered texel given the world point and normal it stands for.
/// </summary>
/// <remarks>
/// The geometry half of <see cref="LightmapBaker"/>'s phase 1, and the placement transforms the triangle
/// build shares with it, so a texel and the triangle a gather ray hits agree on where the surface is.
/// </remarks>
internal static class LightmapRasteriser
{
    // Walks a record's triangle list and rasterises each one into the target's lightmap-UV space, filling
    // every texel a triangle covers with the world position and normal barycentrically interpolated there.
    internal static void RasteriseRecord(
        WorldPlacement placement, ReadOnlySpan<MefVertex> vertices, MefDrawRecord record, LightmapTarget target)
    {
        ReadOnlySpan<ushort> indices = record.Indices.Span;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            MefVertex a = ResolveVertex(vertices, record, indices[i]);
            MefVertex b = ResolveVertex(vertices, record, indices[i + 1]);
            MefVertex c = ResolveVertex(vertices, record, indices[i + 2]);

            RasteriseTriangle(placement, a, b, c, target);
        }
    }

    // A record's indices are relative to its BaseVertex on the lightmapped format and absolute otherwise;
    // ResolveVertex hides that so the caller always gets the right vertex.
    internal static MefVertex ResolveVertex(ReadOnlySpan<MefVertex> vertices, MefDrawRecord record, ushort index)
    {
        int vertexIndex = record.IndicesAreRelative ? record.BaseVertex + index : index;
        return vertices[vertexIndex];
    }

    // One triangle into UV space. The lightmap UV maps [0,1] onto the page, so a texel centre at (x+0.5,
    // y+0.5) sits at UV ((x+0.5)/width, (y+0.5)/height); a texel is covered when that point is inside the
    // triangle's UV projection, and its surface point is the same barycentric blend of the three vertices'
    // world positions and normals. Only the UV bounding box is scanned, so the cost is the triangle's area
    // in texels rather than the whole page.
    private static void RasteriseTriangle(
        WorldPlacement placement, MefVertex a, MefVertex b, MefVertex c, LightmapTarget target)
    {
        // The three UV corners in texel coordinates.
        float uvAx = a.LightmapU * target.Width, uvAy = a.LightmapV * target.Height;
        float uvBx = b.LightmapU * target.Width, uvBy = b.LightmapV * target.Height;
        float uvCx = c.LightmapU * target.Width, uvCy = c.LightmapV * target.Height;

        // A degenerate UV triangle covers no area; its edge cross product is the doubled signed area, and
        // dividing by it below would be a divide by zero.
        float area = ((uvBx - uvAx) * (uvCy - uvAy)) - ((uvBy - uvAy) * (uvCx - uvAx));

        if (MathF.Abs(area) < 1e-8f)
        {
            return;
        }

        float inverseArea = 1f / area;

        int minX = Math.Clamp((int)MathF.Floor(MathF.Min(uvAx, MathF.Min(uvBx, uvCx))), 0, target.Width - 1);
        int maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(uvAx, MathF.Max(uvBx, uvCx))), 0, target.Width - 1);
        int minY = Math.Clamp((int)MathF.Floor(MathF.Min(uvAy, MathF.Min(uvBy, uvCy))), 0, target.Height - 1);
        int maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(uvAy, MathF.Max(uvBy, uvCy))), 0, target.Height - 1);

        // The three world positions and normals the barycentric blend runs over.
        Vector3d worldA = ToWorldMetres(placement, a.Position);
        Vector3d worldB = ToWorldMetres(placement, b.Position);
        Vector3d worldC = ToWorldMetres(placement, c.Position);
        Vector3f normalA = ToWorldNormal(placement, a.Normal);
        Vector3f normalB = ToWorldNormal(placement, b.Normal);
        Vector3f normalC = ToWorldNormal(placement, c.Normal);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;

                // Barycentric weights of the texel centre against the UV triangle.
                float wA = (((uvBx - px) * (uvCy - py)) - ((uvBy - py) * (uvCx - px))) * inverseArea;
                float wB = (((uvCx - px) * (uvAy - py)) - ((uvCy - py) * (uvAx - px))) * inverseArea;
                float wC = 1f - wA - wB;

                // A small negative tolerance keeps the shared edge between two triangles from leaving a
                // one-texel gap, which reads as a dark seam once the page is lit.
                const float edge = -0.001f;
                if (wA < edge || wB < edge || wC < edge)
                {
                    continue;
                }

                Vector3d world = new(
                    (worldA.X * wA) + (worldB.X * wB) + (worldC.X * wC),
                    (worldA.Y * wA) + (worldB.Y * wB) + (worldC.Y * wC),
                    (worldA.Z * wA) + (worldB.Z * wB) + (worldC.Z * wC));

                Vector3f normal = (new Vector3f(
                    (normalA.X * wA) + (normalB.X * wB) + (normalC.X * wC),
                    (normalA.Y * wA) + (normalB.Y * wB) + (normalC.Y * wC),
                    (normalA.Z * wA) + (normalB.Z * wB) + (normalC.Z * wC))).NormalizedOr(Vector3f.UnitZ, 1e-8f);

                target.Texels[(y * target.Width) + x] = new LightmapTexel(world, normal, true);
            }
        }
    }

    // A raw MEF vertex is in engine units in the model's own frame; the world point is the placement's
    // rotation applied and its position added, matching the transform ObjectCollisionIndex builds its
    // triangles with. The rotation is in units, so it is divided to metres before the metre position is
    // added.
    internal static Vector3d ToWorldMetres(WorldPlacement placement, Vector3f localUnits)
    {
        Vector3f rotated = placement.Orientation.Transform(localUnits);
        return new Vector3d(
            (rotated.X / WorldScene.UnitsPerMetre) + placement.Position.X,
            (rotated.Y / WorldScene.UnitsPerMetre) + placement.Position.Y,
            (rotated.Z / WorldScene.UnitsPerMetre) + placement.Position.Z);
    }

    // A normal rotates with the object but does not translate or scale; the placement's orientation is a
    // pure rotation, so transforming and renormalising is all it takes.
    private static Vector3f ToWorldNormal(WorldPlacement placement, Vector3f localNormal) =>
        (placement.Orientation.Transform(localNormal)).NormalizedOr(Vector3f.UnitZ, 1e-8f);
}
