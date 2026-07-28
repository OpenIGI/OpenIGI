using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;

namespace OpenIGI.Formats.World;

/// <summary>
/// A baked object lightmap: one <c>.olm</c> entry of a level's <c>lightmaps/lightmaps.res</c> archive.
/// </summary>
/// <remarks>
/// <para>
/// Entries are named <c>&lt;objectName&gt;_&lt;%05d&gt;.olm</c> (format string at <c>0x54F09C</c>, used by
/// <c>0x523D40</c>), where the number is the index of the model's draw record the map belongs to. The
/// original only loads them for <see cref="MefMeshFormat.Lightmapped"/> models that have triangles.
/// </para>
/// <para>
/// An entry holds <em>one or more</em> images: a 60-byte prefix, then one 44-byte descriptor per image,
/// then the pixel blocks in the same order. 25320 of the 25333 retail entries hold a single image, which
/// is why a single 104-byte header appears to fit — it is the one-image case of
/// <c>60 + 44*count</c>. Twelve entries hold two images and one holds three.
/// </para>
/// </remarks>
public sealed class ObjectLightmap
{
    /// <summary>The size in bytes of the header prefix that precedes the image descriptors.</summary>
    public const int PrefixSize = 60;

    /// <summary>The size in bytes of one image descriptor.</summary>
    public const int DescriptorSize = 44;

    /// <summary>The offset of an image's dimensions within its descriptor.</summary>
    public const int DescriptorSizeOffset = 40;

    /// <summary>The file-name extension of a lightmap entry, including the dot.</summary>
    public const string Extension = ".olm";

    private ObjectLightmap(float version, IReadOnlyList<ObjectLightmapImage> images) =>
        (Version, Images) = (version, images);

    /// <summary>Gets the version from the header, <c>0.12f</c> across the retail corpus.</summary>
    public float Version { get; }

    /// <summary>Gets the images, in file order. The first is the one a draw record samples.</summary>
    public IReadOnlyList<ObjectLightmapImage> Images { get; }

    /// <summary>Parses a lightmap.</summary>
    /// <param name="data">The entry bytes.</param>
    /// <returns>The parsed lightmap.</returns>
    /// <exception cref="PackFormatException">The bytes do not match the documented layout.</exception>
    public static ObjectLightmap Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < PrefixSize + DescriptorSize)
        {
            throw new PackFormatException(
                $"An object lightmap needs at least {PrefixSize + DescriptorSize} bytes, got {span.Length}.");
        }

        float version = BinaryPrimitives.ReadSingleLittleEndian(span);
        uint rawCount = BinaryPrimitives.ReadUInt32LittleEndian(span[44..]);
        if (rawCount is 0 or > 64)
        {
            throw new PackFormatException($"An object lightmap declares {rawCount} images, which is not plausible.");
        }

        int count = (int)rawCount;
        int pixelStart = PrefixSize + (count * DescriptorSize);
        if (pixelStart > span.Length)
        {
            throw new PackFormatException(
                $"An object lightmap declares {count} images, whose descriptors need {pixelStart} bytes, but the entry is {span.Length}.");
        }

        ObjectLightmapImage[] images = new ObjectLightmapImage[count];
        int offset = pixelStart;
        for (int i = 0; i < count; i++)
        {
            int dims = PrefixSize + (i * DescriptorSize) + DescriptorSizeOffset;
            int width = BinaryPrimitives.ReadUInt16LittleEndian(span[dims..]);
            int height = BinaryPrimitives.ReadUInt16LittleEndian(span[(dims + 2)..]);

            long bytes = 4L * width * height;
            if (bytes > span.Length - offset)
            {
                throw new PackFormatException(
                    $"Object lightmap image {i} is {width}x{height}, needing {bytes} bytes, but only {span.Length - offset} remain.");
            }

            images[i] = new ObjectLightmapImage(width, height, data.Slice(offset, (int)bytes));
            offset += (int)bytes;
        }

        // The arithmetic closes exactly for all 25333 retail entries; a leftover means a misread header.
        if (offset != span.Length)
        {
            throw new PackFormatException(
                $"An object lightmap's {count} images account for {offset} of {span.Length} bytes.");
        }

        return new ObjectLightmap(version, new ReadOnlyCollection<ObjectLightmapImage>(images));
    }

    /// <summary>Splits a lightmap entry name into the object name and the draw-record index.</summary>
    /// <param name="name">The entry name, with or without a directory prefix and with or without the extension.</param>
    /// <param name="objectName">Receives the object name, for example <c>obj00014</c>.</param>
    /// <param name="recordIndex">Receives the draw-record index the lightmap belongs to.</param>
    /// <returns><see langword="true"/> when the name has the expected shape.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static bool TryParseName(string name, out string objectName, out int recordIndex)
    {
        ArgumentNullException.ThrowIfNull(name);

        ReadOnlySpan<char> span = name.AsSpan();
        int slash = span.LastIndexOfAny('/', '\\');
        if (slash >= 0)
        {
            span = span[(slash + 1)..];
        }

        if (span.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            span = span[..^Extension.Length];
        }

        int separator = span.LastIndexOf('_');
        if (separator > 0
            && int.TryParse(span[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out recordIndex))
        {
            objectName = new string(span[..separator]);
            return true;
        }

        objectName = string.Empty;
        recordIndex = 0;
        return false;
    }

    /// <inheritdoc/>
    public override string ToString() => $"object lightmap, {Images.Count} image(s), first {Images[0]}";
}
