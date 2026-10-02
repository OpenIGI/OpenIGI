using System.Buffers.Binary;
using System.Collections.Concurrent;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;

namespace OpenIGI.Tools.ResExtract;

/// <summary>
/// Converts an installed overrides folder from PNG pack pictures to block-compressed ones, in place of
/// building it again from the upscaled PNG tree.
/// </summary>
/// <remarks>
/// <para>
/// A pack built before block compression existed holds every world texture as a PNG, which the game
/// decodes and mips on every load and keeps uncompressed on the GPU. This reads each one's top level back
/// out, runs the same bake <see cref="PackCompression"/> runs for a fresh build, and writes the stored
/// chain in its place. Everything else -- sprites, menus, tile grids, sounds, anything already compressed
/// -- is copied as it is.
/// </para>
/// <para>
/// What counts as a world texture is where it lives: under a <c>textures</c> directory, which is where the
/// game keeps the pictures models are drawn with (a level's <c>textures/levelN.res</c>, the common
/// <c>textures/textures.res</c>). Interface art is elsewhere and stays a PNG.
/// </para>
/// </remarks>
internal static class PackRecompress
{
    /// <summary>What one run did.</summary>
    /// <param name="Compressed">How many pictures were converted.</param>
    /// <param name="Copied">How many files or entries were copied unchanged.</param>
    /// <param name="BytesBefore">The input folder's size.</param>
    /// <param name="BytesAfter">The output folder's size.</param>
    /// <param name="Failures">What could not be converted, and was copied instead.</param>
    internal readonly record struct Result(int Compressed, int Copied, long BytesBefore, long BytesAfter, List<string> Failures);

    /// <summary>Converts a folder.</summary>
    /// <param name="source">The installed overrides folder.</param>
    /// <param name="destination">Where to write the converted folder; must not be <paramref name="source"/>.</param>
    /// <param name="quality">How hard the block encoder searches.</param>
    /// <returns>What was done.</returns>
    internal static Result Run(string source, string destination, BlockQuality quality)
    {
        ConcurrentBag<string> failures = [];
        int compressed = 0;
        int copied = 0;
        long before = 0;
        long after = 0;

        foreach (string path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, path);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            byte[] input = File.ReadAllBytes(path);
            before += input.Length;
            bool worldTextures = IsUnderTextures(relative);
            byte[] output;

            if (relative.EndsWith(".res", StringComparison.OrdinalIgnoreCase) && worldTextures)
            {
                output = ConvertArchive(relative, input, quality, failures, ref compressed, ref copied);
            }
            else if (worldTextures && TryConvert(input, quality, out byte[]? converted, relative, failures))
            {
                output = converted!;
                compressed++;
            }
            else
            {
                output = input;
                copied++;
            }

            File.WriteAllBytes(target, output);
            after += output.Length;
            Console.WriteLine($"  {relative}: {input.Length / 1048576.0:F1} -> {output.Length / 1048576.0:F1} MiB");
        }

        return new Result(compressed, copied, before, after, [.. failures]);
    }

    private static byte[] ConvertArchive(
        string relative,
        byte[] archive,
        BlockQuality quality,
        ConcurrentBag<string> failures,
        ref int compressed,
        ref int copied)
    {
        IReadOnlyList<IresIndex.Located> located;
        using (MemoryStream stream = new(archive, writable: false))
        {
            located = IresIndex.Read(stream);
        }

        // Each entry is independent; a block encoder is most of a second a texture, so they go side by side.
        IresWriter.Entry[] entries = new IresWriter.Entry[located.Count];
        int converted = 0;
        Parallel.For(0, located.Count, i =>
        {
            IresIndex.Located entry = located[i];
            ReadOnlyMemory<byte> bytes = archive.AsMemory((int)entry.Offset, entry.Length);

            if (entry.Kind == IresEntryKind.Body
                && TryConvert(bytes.ToArray(), quality, out byte[]? blocks, $"{relative}:{entry.Name}", failures))
            {
                entries[i] = new IresWriter.Entry(entry.Name, entry.Kind, blocks);
                Interlocked.Increment(ref converted);
                return;
            }

            entries[i] = new IresWriter.Entry(entry.Name, entry.Kind, bytes);
        });

        compressed += converted;
        copied += located.Count - converted;
        return IresWriter.Write(entries);
    }

    /// <summary>Converts one PNG world texture; anything else is left alone.</summary>
    private static bool TryConvert(byte[] input, BlockQuality quality, out byte[]? output, string what, ConcurrentBag<string> failures)
    {
        output = null;
        ReadOnlySpan<byte> span = input;

        if (!PackPicture.Is(span))
        {
            return false;
        }

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
        int frames = BinaryPrimitives.ReadInt32LittleEndian(span[8..]);
        int columns = BinaryPrimitives.ReadInt32LittleEndian(span[24..]);

        // One PNG picture, not a grid, not already compressed.
        if ((flags & PackPicture.PngFlag) == 0 || frames != 1 || columns != 0)
        {
            return false;
        }

        try
        {
            PackPicture.Frame frame = PackPicture.ReadFirstFrame(span, buildMips: false);
            if (!PackCompression.CanCompress(frame.Image))
            {
                return false;
            }

            int logicalWidth = BinaryPrimitives.ReadInt32LittleEndian(span[16..]);
            int logicalHeight = BinaryPrimitives.ReadInt32LittleEndian(span[20..]);

            output = PackPicture.Write(new PackPicture.Content(
                [PackCompression.Compress(frame, quality)], logicalWidth, logicalHeight, 0, 0, default));
            return true;
        }
        catch (Exception error) when (error is TextureFormatException or ArgumentException or InvalidDataException)
        {
            failures.Add($"{what}: {error.Message}");
            return false;
        }
    }

    private static bool IsUnderTextures(string relative) =>
        relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals("textures", StringComparison.OrdinalIgnoreCase));
}
