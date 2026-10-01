using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace OpenIGI.Formats.World;

/// <summary>
/// A <c>terrain.lmp</c> file: a flat sequence of square, 8-bit baked lightmap pages.
/// </summary>
/// <remarks>
/// <para>
/// Each record is <c>{u32 size; u8 texels[size * size]}</c>. A size of zero is a hole and consumes only
/// its four bytes, which matters: level 14 is 171 holes out of 172 records. Retail sizes are 16, 32, 64,
/// 128 and 256, and the walk closes exactly for all fourteen levels.
/// </para>
/// <para>
/// Texels are indices, not intensities. The original expands them through a 256-entry ramp it builds in
/// place as <c>i &lt;&lt; 16</c>; the pages are kept 8-bit here so that expansion belongs to whoever
/// uploads them.
/// </para>
/// <para>
/// Which page a chunk samples, and the planar mapping it samples with, come from a run-time binding a
/// level script's tasks publish (<c>0xBA80B0</c>) rather than from this file.
/// </para>
/// </remarks>
public sealed class TerrainLightmaps
{
    private TerrainLightmaps(IReadOnlyList<TerrainLightmapPage> pages, int consumed, int length)
    {
        Pages = pages;
        BytesConsumed = consumed;
        Length = length;
    }

    /// <summary>Gets the pages, in file order; the index is the page id.</summary>
    public IReadOnlyList<TerrainLightmapPage> Pages { get; }

    /// <summary>Gets how many bytes the walk consumed.</summary>
    public int BytesConsumed { get; }

    /// <summary>Gets the file's size in bytes.</summary>
    public int Length { get; }

    /// <summary>Gets a value indicating whether the walk consumed the file exactly.</summary>
    public bool IsExact => BytesConsumed == Length;

    /// <summary>Gets how many pages carry texels.</summary>
    public int OccupiedCount
    {
        get
        {
            int n = 0;
            foreach (TerrainLightmapPage page in Pages)
            {
                if (!page.IsEmpty)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>Reads a <c>terrain.lmp</c> file.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The pages.</returns>
    /// <remarks>The walk stops at the first record that does not fit, leaving <see cref="IsExact"/> false.</remarks>
    public static TerrainLightmaps Read(ReadOnlyMemory<byte> data)
    {
        List<TerrainLightmapPage> pages = [];
        int cursor = 0;

        while (data.Length - cursor >= 4)
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[cursor..]);
            cursor += 4;

            if (size == 0)
            {
                pages.Add(default);
                continue;
            }

            if (size > ushort.MaxValue)
            {
                break;
            }

            long texels = (long)size * size;
            if (texels > data.Length - cursor)
            {
                cursor -= 4;
                break;
            }

            pages.Add(new TerrainLightmapPage((int)size, data.Slice(cursor, (int)texels)));
            cursor += (int)texels;
        }

        return new TerrainLightmaps(new ReadOnlyCollection<TerrainLightmapPage>(pages), cursor, data.Length);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"terrain lightmaps: {Pages.Count} page(s), {OccupiedCount} occupied, {BytesConsumed} of {Length} bytes";
}

/// <summary>One square 8-bit page of a <c>terrain.lmp</c>.</summary>
/// <param name="Size">The page's edge length in texels; zero for a hole.</param>
/// <param name="Texels">The palette indices, row-major.</param>
public readonly record struct TerrainLightmapPage(int Size, ReadOnlyMemory<byte> Texels)
{
    /// <summary>Gets a value indicating whether the record is a hole.</summary>
    public bool IsEmpty => Size == 0;
}
