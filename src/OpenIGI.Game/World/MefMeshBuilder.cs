using System.Collections.ObjectModel;
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
    /// <returns>The geometry, or <see langword="null"/> when the model has nothing to draw.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public static MeshGeometry? Build(MefModel model)
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
            vertices[i] = new Vertex3D(v.Position, v.Normal, v.DiffuseU, v.DiffuseV, v.LightmapU, v.LightmapV);
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

            subsets.Add(new MeshSubset(start, source2.Length, record.MaterialSlot));
        }

        return new MeshGeometry(vertices, indices, new ReadOnlyCollection<MeshSubset>(subsets));
    }
}
