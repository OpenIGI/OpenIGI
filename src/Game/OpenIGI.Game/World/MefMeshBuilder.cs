using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>Turns a loaded <c>.mef</c> model into buffers a graphics device can hold.</summary>
/// <remarks>
/// <para>
/// The vertex layouts already match — <see cref="Vertex3D"/> is deliberately the original's 40-byte
/// model vertex — so this is really about the index buffer. A model's draw records index its vertices two
/// different ways depending on the mesh format: a lightmapped model's indices are relative to the
/// record's base vertex, the other two formats index the whole array. Flattening resolves both to
/// absolute indices in one buffer, and each record becomes a <see cref="MeshSubset"/> naming its range
/// and its material slot.
/// </para>
/// <para>
/// The material slot is carried through rather than resolved here: a slot names a position in the
/// <em>instance's</em> list, so the same model drawn by two instances wears two different sets of
/// textures. Only the caller knows which instance it is drawing.
/// </para>
/// </remarks>
public static class MefMeshBuilder
{
    /// <summary>How far apart two faces' planes may be and still be one sheet's two faces, in model units: an eighth of a millimetre.</summary>
    private const float SkinPlaneToleranceUnits = 0.5f;

    /// <summary>Flattens a model's vertices and draw records into device-ready buffers.</summary>
    /// <param name="model">The model to convert.</param>
    /// <param name="scale">
    /// A factor applied to every position. Model vertices use the same units as world positions, so pass
    /// <c>1 / <see cref="WorldScene.UnitsPerMetre"/></c> to get metres.
    /// </param>
    /// <returns>The geometry, or <see langword="null"/> when the model has nothing to draw.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public static MeshGeometry? Build(MefModel model, float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.IsEmpty || model.DrawRecords.Count == 0)
        {
            return null;
        }

        ReadOnlySpan<MefVertex> source = model.Vertices;
        Vertex3D[] vertices = new Vertex3D[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            MefVertex v = source[i];
            vertices[i] = new Vertex3D(
                new Vector3f(v.Position.X * scale, v.Position.Y * scale, v.Position.Z * scale),
                v.Normal,
                v.DiffuseU,
                v.DiffuseV,
                v.LightmapU,
                v.LightmapV);
        }

        int total = 0;
        foreach (MefDrawRecord record in model.DrawRecords)
        {
            total += record.Indices.Length;
        }

        ushort[] indices = new ushort[total];
        List<MeshSubset> subsets = new(model.DrawRecords.Count);
        int cursor = 0;

        foreach (MefDrawRecord record in model.DrawRecords)
        {
            ReadOnlySpan<ushort> source2 = record.Indices.Span;
            int start = cursor;
            foreach (ushort index in source2)
            {
                indices[cursor++] = (ushort)record.ToVertexIndex(index);
            }

            SubsetBlend blend = record.IsAdditive ? SubsetBlend.Additive
                : record.IsTransparent ? SubsetBlend.Translucent
                : record.IsAlphaTested ? SubsetBlend.AlphaTested
                : SubsetBlend.Opaque;
            subsets.Add(new MeshSubset(start, source2.Length, record.MaterialSlot, record.LightmapPage, Blend: blend));
        }

        // A room is wound by its authored order, not its normals: some rooms carry their normals out of the room (mission 14's
        // yard and halls), and wound to them the whole interior became the back -- culled, a room seen into through a doorway
        // lost its floor and walls. The authored order faces every room's inside the same way, so it is mirrored whole into
        // this renderer's facing, which for a room with inward normals is exactly what the repair gave.
        if (model.IsSector && HasNormals(vertices))
        {
            MirrorWinding(indices);
        }
        else
        {
            RepairWinding(vertices, indices);
        }

        // Wound to their normals, a sheet's two faces now face apart, and the faces that coincide can be drawn culled.
        MeshSubset[] partitioned = CoincidentSkins.Partition(vertices, indices, subsets, SkinPlaneToleranceUnits * scale);
        return new MeshGeometry(vertices, indices, partitioned);
    }

    /// <summary>Turns every triangle over: the authored draw order faces the other way to this renderer's front.</summary>
    /// <param name="indices">The triangle-list indices, rewound in place.</param>
    /// <remarks>
    /// The authored order faces each surface away from whoever is meant to see it, props and rooms alike (most of a prop's
    /// triangles disagree with its outward normals before <see cref="RepairWinding"/>), so turning all of them over gives the
    /// front without consulting a normal.
    /// </remarks>
    public static void MirrorWinding(ushort[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
        }
    }

    private static bool HasNormals(Vertex3D[] vertices) =>
        Array.TrueForAll(vertices, static v => float.IsFinite(v.Normal.LengthSquared) && v.Normal.LengthSquared > 0.000001f);

    /// <summary>Makes every triangle wind to agree with its vertices' own normals, so a front face exists.</summary>
    /// <remarks>
    /// Public because it is not only this builder's business: anything that assembles world geometry from a
    /// model's raw draw records has the same unwound triangles and must be repaired the same way, or back
    /// face culling removes it. Spline segments are bent into shape rather than built here, and were the
    /// case that proved it.
    /// </remarks>
    /// <param name="vertices">The mesh's vertices, whose normals point out of the surface.</param>
    /// <param name="indices">The triangle-list indices, rewound in place.</param>
    /// <remarks>
    /// The flattened draw order does not wind uniformly -- some triangles face out and some in -- which is
    /// why the world pass could not cull: culling either facing dropped real walls. But every vertex carries
    /// a normal the lighting already trusts, and it points out of the surface. So a triangle's intended front
    /// is the side its vertex normals face; where its winding disagrees (its own cross product opposes the
    /// vertex normals) the two indices are swapped to bring it into line. With every triangle then wound the
    /// same way relative to its outward normal, back-face culling has a consistent facing to remove -- which
    /// is what stops a thin object's two coincident faces from fighting over the same pixel, and halves the
    /// indoor fill besides. A degenerate triangle, whose cross product is near zero, is left as it is.
    /// </remarks>
    public static void RepairWinding(Vertex3D[] vertices, ushort[] indices)
    {
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            int i0 = indices[t];
            int i1 = indices[t + 1];
            int i2 = indices[t + 2];

            Vector3f faceNormal = Vector3f.Cross(
                vertices[i1].Position - vertices[i0].Position,
                vertices[i2].Position - vertices[i0].Position);

            Vector3f vertexNormal = vertices[i0].Normal + vertices[i1].Normal + vertices[i2].Normal;

            if (Vector3f.Dot(faceNormal, vertexNormal) < 0f)
            {
                indices[t + 1] = (ushort)i2;
                indices[t + 2] = (ushort)i1;
            }
        }
    }
}
