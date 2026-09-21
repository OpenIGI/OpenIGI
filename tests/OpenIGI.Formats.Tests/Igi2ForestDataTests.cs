using System.Buffers.Binary;
using System.Text;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// <see cref="Igi2ForestData"/> against hand-built files, so the decode and every refusal are covered on
/// a checkout with no game data.
/// </summary>
public sealed class Igi2ForestDataTests
{
    /// <summary>A tree decodes its position, angles, scale and light.</summary>
    [Fact]
    public void A_tree_decodes_its_placement()
    {
        byte[] file = Build(
            [1f, 2f, 3f, 0f, 0f, 1.5f, 0.9f, 0.9f, 0.9f],
            [0x11, 0x22, 0x33, 0x00]);

        Igi2ForestData forest = Igi2ForestData.Read(file);

        Assert.Equal(Igi2ForestData.CurrentHeader, forest.Header);
        Assert.Equal(1, forest.Count);

        Igi2ForestTree tree = forest.GetTree(0);
        Assert.Equal(new Vector3f(1f, 2f, 3f), tree.Position);
        Assert.Equal(new Vector3f(0f, 0f, 1.5f), tree.Angles);
        Assert.Equal(new Vector3f(0.9f, 0.9f, 0.9f), tree.Scale);
        Assert.Equal(0x11, tree.Light.B);
        Assert.Equal(0x22, tree.Light.G);
        Assert.Equal(0x33, tree.Light.R);
    }

    /// <summary>Trees come back in file order, and the count is the file's own.</summary>
    [Fact]
    public void Trees_are_enumerated_in_file_order()
    {
        Igi2ForestData forest = Igi2ForestData.Read(BuildMany(3));

        Assert.Equal(3, forest.Count);
        Assert.Equal<float[]>([0f, 1f, 2f], [.. forest.Trees().Select(static t => t.Position.X)]);
    }

    /// <summary>A header the reader does not know is refused rather than guessed at.</summary>
    /// <remarks>
    /// The original picks its record stride by matching the header, so an unrecognised one is not a
    /// cosmetic difference: read at the wrong stride the whole array decodes to noise.
    /// </remarks>
    [Fact]
    public void An_unknown_header_is_refused()
    {
        byte[] file = Build([0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f], [0, 0, 0, 0], header: "\nVer. 2.1\n");

        PackFormatException failure = Assert.Throws<PackFormatException>(() => Igi2ForestData.Read(file));
        Assert.Contains("Ver. 2.1", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A header length that runs past the end of the file is refused.</summary>
    [Fact]
    public void A_header_longer_than_the_file_is_refused()
    {
        byte[] file = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(file, 99);

        Assert.Throws<PackFormatException>(() => Igi2ForestData.Read(file));
    }

    /// <summary>A partial trailing record is refused rather than silently dropped.</summary>
    /// <remarks>
    /// Unlike a terrain map, where a long file is the original's own tolerated case, a forest file is
    /// nothing but whole records — a remainder means the stride is wrong.
    /// </remarks>
    [Fact]
    public void A_partial_record_is_refused()
    {
        byte[] file = [.. BuildMany(2), .. new byte[7]];

        Assert.Throws<PackFormatException>(() => Igi2ForestData.Read(file));
    }

    /// <summary>A file with a header and no records is a forest with no trees, not an error.</summary>
    /// <remarks>The count comes from the task, so an empty file simply clamps it to zero.</remarks>
    [Fact]
    public void An_empty_placement_is_valid()
    {
        Igi2ForestData forest = Igi2ForestData.Read(BuildMany(0));

        Assert.Equal(0, forest.Count);
        Assert.Empty(forest.Trees());
    }

    /// <summary>Asking for a tree the file does not hold throws.</summary>
    [Fact]
    public void Indexing_past_the_end_throws()
    {
        Igi2ForestData forest = Igi2ForestData.Read(BuildMany(2));

        Assert.Throws<ArgumentOutOfRangeException>(() => forest.GetTree(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => forest.GetTree(-1));
    }

    private static byte[] Build(float[] values, byte[] light, string header = Igi2ForestData.CurrentHeader)
    {
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        byte[] file = new byte[sizeof(int) + headerBytes.Length + Igi2ForestData.TreeStride];

        BinaryPrimitives.WriteInt32LittleEndian(file, headerBytes.Length);
        headerBytes.CopyTo(file, sizeof(int));

        int at = sizeof(int) + headerBytes.Length;
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(at + (i * 4)), values[i]);
        }

        light.CopyTo(file, at + 36);
        return file;
    }

    private static byte[] BuildMany(int trees)
    {
        byte[] headerBytes = Encoding.ASCII.GetBytes(Igi2ForestData.CurrentHeader);
        byte[] file = new byte[sizeof(int) + headerBytes.Length + (trees * Igi2ForestData.TreeStride)];

        BinaryPrimitives.WriteInt32LittleEndian(file, headerBytes.Length);
        headerBytes.CopyTo(file, sizeof(int));

        for (int i = 0; i < trees; i++)
        {
            int at = sizeof(int) + headerBytes.Length + (i * Igi2ForestData.TreeStride);
            BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(at), i);
        }

        return file;
    }
}
