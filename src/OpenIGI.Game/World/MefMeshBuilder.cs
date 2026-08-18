using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;

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

            subsets.Add(new MeshSubset(start, source2.Length, record.MaterialSlot, record.LightmapPage));
        }

        RepairWinding(vertices, indices);
        return new MeshGeometry(vertices, indices, [.. subsets]);
    }

    /// <summary>Makes every triangle wind to agree with its vertices' own normals, so a front face exists.</summary>
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
    private static void RepairWinding(Vertex3D[] vertices, ushort[] indices)
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
