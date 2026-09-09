using System.Buffers.Binary;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.World;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// <see cref="Igi2TerrainMap"/> against hand-built headers, so the decode and every refusal are covered
/// on a checkout with no game data.
/// </summary>
public sealed class Igi2TerrainMapTests
{
    /// <summary>A height map decodes its extent, its identity pair and its samples.</summary>
    [Fact]
    public void A_height_map_decodes_its_samples()
    {
        float[] heights = [1f, 2f, 3f, 4f, 5f, 6f];
        byte[] payload = new byte[heights.Length * 4];
        for (int i = 0; i < heights.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(i * 4), heights[i]);
        }

        Igi2TerrainMap map = Igi2TerrainMap.Read(Build(Igi2TerrainMapKind.Height, 3, 2, payload));

        Assert.Equal(Igi2TerrainMapKind.Height, map.Kind);
        Assert.Equal(3, map.Width);
        Assert.Equal(2, map.Height);
        Assert.Equal(6, map.SampleCount);
        Assert.Equal(0f, map.Offset);
        Assert.Equal(1f, map.Scale);
        Assert.True(map.Trailing.IsEmpty);

        // Row-major: the second row starts at the fourth sample.
        Assert.Equal(1f, map.GetHeight(0, 0));
        Assert.Equal(3f, map.GetHeight(2, 0));
        Assert.Equal(4f, map.GetHeight(0, 1));
        Assert.Equal(6f, map.GetHeight(2, 1));
    }

    /// <summary>A lightmap texel comes back in the order the writer stored it.</summary>
    [Fact]
    public void A_lightmap_decodes_blue_green_red_alpha()
    {
        byte[] payload = [0x11, 0x22, 0x33, 0xFF];

        Igi2TerrainMap map = Igi2TerrainMap.Read(Build(Igi2TerrainMapKind.Light, 1, 1, payload));
        Bgra32 texel = map.GetLight(0, 0);

        Assert.Equal(0x11, texel.B);
        Assert.Equal(0x22, texel.G);
        Assert.Equal(0x33, texel.R);
        Assert.Equal(0xFF, texel.A);
    }

    /// <summary>A material map is one byte per sample.</summary>
    [Fact]
    public void A_material_map_decodes_one_byte_per_sample()
    {
        Igi2TerrainMap map = Igi2TerrainMap.Read(Build(Igi2TerrainMapKind.Material, 2, 2, [0, 1, 2, 3]));

        Assert.Equal(4, map.Samples.Length);
        Assert.Equal(2, map.GetMaterial(0, 1));
        Assert.Equal(3, map.GetMaterial(1, 1));
    }

    /// <summary>Bytes past the declared extent are kept out of the grid rather than dropped silently.</summary>
    [Fact]
    public void Bytes_past_the_extent_become_the_trailing_block()
    {
        byte[] payload = new byte[(2 * 2 * 4) + 7];

        Igi2TerrainMap map = Igi2TerrainMap.Read(Build(Igi2TerrainMapKind.Height, 2, 2, payload));

        Assert.Equal(16, map.Samples.Length);
        Assert.Equal(7, map.Trailing.Length);
    }

    /// <summary>A file short of its declared extent is refused; the original would read past its end.</summary>
    [Fact]
    public void A_short_payload_is_refused()
    {
        byte[] data = Build(Igi2TerrainMapKind.Height, 2, 2, new byte[15]);

        PackFormatException failure = Assert.Throws<PackFormatException>(() => Igi2TerrainMap.Read(data));
        Assert.Contains("needs 16", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Only the one version the three loaders accept is accepted.</summary>
    [Fact]
    public void A_foreign_version_is_refused()
    {
        byte[] data = Build(Igi2TerrainMapKind.Material, 1, 1, [0]);
        BinaryPrimitives.WriteUInt32LittleEndian(data, Igi2TerrainMap.VersionWord + 1);

        Assert.Throws<PackFormatException>(() => Igi2TerrainMap.Read(data));
    }

    /// <summary>A format word outside the three kinds is refused rather than guessed at.</summary>
    [Fact]
    public void An_unknown_format_is_refused()
    {
        byte[] data = Build(Igi2TerrainMapKind.Material, 1, 1, [0]);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(32), 9);

        Assert.Throws<PackFormatException>(() => Igi2TerrainMap.Read(data));
    }

    /// <summary>Asking for one kind and being handed another is an error, not a reinterpretation.</summary>
    [Fact]
    public void The_checked_overload_refuses_a_different_kind()
    {
        byte[] data = Build(Igi2TerrainMapKind.Material, 1, 1, [0]);

        Assert.Throws<PackFormatException>(
            () => Igi2TerrainMap.Read(data, Igi2TerrainMapKind.Light));
    }

    /// <summary>An empty or negative extent is refused.</summary>
    /// <param name="width">The width to declare.</param>
    /// <param name="height">The height to declare.</param>
    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-1, 4)]
    public void A_non_positive_extent_is_refused(int width, int height)
    {
        byte[] data = Build(Igi2TerrainMapKind.Material, width, height, []);

        Assert.Throws<PackFormatException>(() => Igi2TerrainMap.Read(data));
    }

    /// <summary>Sampling outside the grid throws rather than reading a neighbouring row.</summary>
    [Fact]
    public void Sampling_outside_the_grid_throws()
    {
        Igi2TerrainMap map = Igi2TerrainMap.Read(Build(Igi2TerrainMapKind.Material, 2, 2, [0, 1, 2, 3]));

        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetMaterial(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetMaterial(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetMaterial(-1, 0));
    }

    /// <summary>The header sizes and strides are the ones the engine reads the payload at.</summary>
    [Fact]
    public void The_header_sizes_and_strides_match_the_engine()
    {
        Assert.Equal(52, Igi2TerrainMap.HeaderSizeOf(Igi2TerrainMapKind.Height));
        Assert.Equal(44, Igi2TerrainMap.HeaderSizeOf(Igi2TerrainMapKind.Light));
        Assert.Equal(44, Igi2TerrainMap.HeaderSizeOf(Igi2TerrainMapKind.Material));

        Assert.Equal(4, Igi2TerrainMap.SampleSizeOf(Igi2TerrainMapKind.Height));
        Assert.Equal(4, Igi2TerrainMap.SampleSizeOf(Igi2TerrainMapKind.Light));
        Assert.Equal(1, Igi2TerrainMap.SampleSizeOf(Igi2TerrainMapKind.Material));
    }

    /// <summary>Builds a terrain map file the way <c>TerrainMap_SaveHandler</c> does.</summary>
    private static byte[] Build(Igi2TerrainMapKind kind, int width, int height, ReadOnlySpan<byte> payload)
    {
        int headerSize = Igi2TerrainMap.HeaderSizeOf(kind);
        byte[] data = new byte[headerSize + payload.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(data, Igi2TerrainMap.VersionWord);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(32), (int)kind);

        int extents = headerSize - 8;
        if (kind == Igi2TerrainMapKind.Height)
        {
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(36), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(40), 1f);
        }

        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(extents), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(extents + 4), height);
        payload.CopyTo(data.AsSpan(headerSize));
        return data;
    }
}
