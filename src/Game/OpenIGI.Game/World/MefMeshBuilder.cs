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

        // Every model is wound by its authored order, never by its normals. The original culls the stored order counter-
        // clockwise for every model it draws (the model states 0x49D480 and 0x49D810, drawn by 0x49DF60 and 0x49C850), and
        // the normals are no guide to it: whole models carry theirs pointing in -- every ladder, the doors, the watchtower,
        // the helicopters (issue #50), and mission 14's yard and halls among the rooms -- and many more have a share of
        // faces whose normals were smoothed across an edge. Wound to the normals, those faces turned inside out and the
        // preset that culls drew their insides. The stored order is mirrored whole into this renderer's facing.
        // A model with no normals (IGI 2's lightmapped geometry) is drawn two-sided and keeps the order it was stored in.
        if (HasNormals(vertices))
        {
            MirrorWinding(indices);
        }

        // Wound as stored, a sheet's two faces face apart, and the faces that coincide can be drawn culled.
        MeshSubset[] partitioned = CoincidentSkins.Partition(vertices, indices, subsets, SkinPlaneToleranceUnits * scale);
        return new MeshGeometry(vertices, indices, partitioned);
    }

    /// <summary>Turns every triangle over: the authored draw order faces the other way to this renderer's front.</summary>
    /// <param name="indices">The triangle-list indices, rewound in place.</param>
    /// <remarks>
    /// <para>
    /// The authored order faces each surface away from whoever is meant to see it, props and rooms alike, so turning all of
    /// them over gives the front the original culls to, without consulting a normal.
    /// </para>
    /// <para>
    /// Public because it is not only this builder's business: anything that assembles world geometry from a model's raw draw
    /// records -- a bent spline segment -- must turn it over the same way, or the preset that culls removes its front.
    /// </para>
    /// </remarks>
    public static void MirrorWinding(ushort[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
        }
    }

    /// <summary>Whether every vertex carries a usable normal.</summary>
    /// <param name="vertices">The mesh's vertices.</param>
    /// <returns><see langword="true"/> when none of the normals is zero or not finite.</returns>
    public static bool HasNormals(Vertex3D[] vertices) =>
        Array.TrueForAll(vertices, static v => float.IsFinite(v.Normal.LengthSquared) && v.Normal.LengthSquared > 0.000001f);
}
