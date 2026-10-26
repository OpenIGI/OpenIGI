using System.Buffers.Binary;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.World;

/// <summary>
/// An I.G.I. 2 bone mesh's morph targets, chunk <c>HPRM</c> (<c>Mesh3D_Morph_t</c>): sixteen channels, each a run of vertices and
/// the offset that channel moves each one by at full weight. Only a face carries them, its mouth on the first channel.
/// </summary>
public sealed class MefMorph
{
    /// <summary>The number of channels, <c>BoneDynCubeObj_s.avMorphWeights</c>'s length.</summary>
    public const int Channels = 16;

    // Each channel's vertex count, then each moved vertex as its index and its offset (Mesh3D_MorphVertex_t, sixteen bytes).
    private const int HeaderSize = Channels * 4;
    private const int VertexSize = 16;

    private readonly int[] _counts;
    private readonly int[] _indices;
    private readonly Vector3f[] _offsets;

    private MefMorph(int[] counts, int[] indices, Vector3f[] offsets)
    {
        _counts = counts;
        _indices = indices;
        _offsets = offsets;
    }

    /// <summary>Gets how many vertices each channel moves.</summary>
    public ReadOnlySpan<int> ChannelVertexCounts => _counts;

    /// <summary>Gets the moved vertices' indices, channel after channel.</summary>
    public ReadOnlySpan<int> Indices => _indices;

    /// <summary>Gets each moved vertex's offset at full weight, in the model's units, in the order of <see cref="Indices"/>.</summary>
    public ReadOnlySpan<Vector3f> Offsets => _offsets;

    /// <summary>Reads a chunk's payload.</summary>
    /// <param name="payload">The <c>HPRM</c> chunk's payload.</param>
    /// <returns>The morphs, or <see langword="null"/> when the payload is too short for what its counts declare.</returns>
    public static MefMorph? Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HeaderSize)
        {
            return null;
        }

        int[] counts = new int[Channels];
        long total = 0;
        for (int channel = 0; channel < Channels; channel++)
        {
            int count = BinaryPrimitives.ReadInt32LittleEndian(payload[(channel * 4)..]);
            if (count < 0)
            {
                return null;
            }

            counts[channel] = count;
            total += count;
        }

        if (HeaderSize + (total * VertexSize) > payload.Length)
        {
            return null;
        }

        int[] indices = new int[total];
        Vector3f[] offsets = new Vector3f[total];
        for (int i = 0; i < total; i++)
        {
            ReadOnlySpan<byte> vertex = payload.Slice(HeaderSize + (i * VertexSize), VertexSize);
            indices[i] = BinaryPrimitives.ReadInt32LittleEndian(vertex);
            offsets[i] = new Vector3f(
                BinaryPrimitives.ReadSingleLittleEndian(vertex[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(vertex[8..]),
                BinaryPrimitives.ReadSingleLittleEndian(vertex[12..]));
        }

        return new MefMorph(counts, indices, offsets);
    }

    /// <summary>
    /// Moves positions by the channels' weights, <c>Direct3DRender_MorphVerticesBone</c>: every vertex a
    /// weighted channel names is moved by its offset times the weight, in float, before the skin poses it.
    /// </summary>
    /// <param name="weights">The channels' weights; a channel past its end, or at nought, moves nothing.</param>
    /// <param name="positions">The positions, moved in place.</param>
    /// <returns>Whether any vertex was moved.</returns>
    public bool Apply(ReadOnlySpan<float> weights, Span<Vector3f> positions)
    {
        bool moved = false;
        int at = 0;
        for (int channel = 0; channel < Channels; channel++)
        {
            int count = _counts[channel];
            float weight = channel < weights.Length ? weights[channel] : 0f;
            if (weight != 0f)
            {
                for (int i = at; i < at + count; i++)
                {
                    int index = _indices[i];
                    if ((uint)index >= (uint)positions.Length)
                    {
                        continue;
                    }

                    Vector3f p = positions[index];
                    Vector3f d = _offsets[i];
                    positions[index] = new Vector3f(
                        (float)((d.X * (double)weight) + p.X),
                        (float)((d.Y * (double)weight) + p.Y),
                        (float)((d.Z * (double)weight) + p.Z));
                    moved = true;
                }
            }

            at += count;
        }

        return moved;
    }
}
