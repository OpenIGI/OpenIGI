using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using OpenIGI.Formats;

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

    /// <summary>The size in bytes of one image descriptor in Project I.G.I.</summary>
    /// <remarks>
    /// I.G.I. 2's is 52. The size is what a caller supplies through
    /// <see cref="AssetFormatProfile.ObjectLightmapDescriptorStride"/>; this constant is kept so the
    /// I.G.I. 1 number has a name and so the minimum-length check has something to quote.
    /// </remarks>
    public const int DescriptorSize = 44;

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
    /// <param name="profile">The title's format policy, which supplies the descriptor stride.</param>
    /// <returns>The parsed lightmap.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">The bytes do not match the documented layout.</exception>
    /// <remarks>
    /// <para>
    /// The two games differ by the descriptor only. A wrong stride is not a degraded read: the pixel
    /// blocks start at the wrong offset and every image after the first is noise, which is why the
    /// closing arithmetic below is an assertion rather than a courtesy.
    /// </para>
    /// <para>
    /// <b>Required, with no default.</b> It used to default to the first game, and the shared world
    /// renderer took that default — so every I.G.I. 2 level drew unlit while its archive was there and
    /// readable, reporting one refused entry per object. A caller that does not know which game it is
    /// reading cannot pick a stride, and asking it to say so is the only thing that makes that visible
    /// at the call site rather than at the first frame.
    /// </para>
    /// </remarks>
    public static ObjectLightmap Read(ReadOnlyMemory<byte> data, AssetFormatProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        int descriptorStride = profile.ObjectLightmapDescriptorStride;

        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < PrefixSize + descriptorStride)
        {
            throw new PackFormatException(
                $"An object lightmap needs at least {PrefixSize + descriptorStride} bytes, got {span.Length}.");
        }

        float version = BinaryPrimitives.ReadSingleLittleEndian(span);
        uint rawCount = BinaryPrimitives.ReadUInt32LittleEndian(span[44..]);
        if (rawCount is 0 or > 64)
        {
            throw new PackFormatException($"An object lightmap declares {rawCount} images, which is not plausible.");
        }

        int count = (int)rawCount;
        int pixelStart = PrefixSize + (count * descriptorStride);
        if (pixelStart > span.Length)
        {
            throw new PackFormatException(
                $"An object lightmap declares {count} images, whose descriptors need {pixelStart} bytes, but the entry is {span.Length}.");
        }

        ObjectLightmapImage[] images = new ObjectLightmapImage[count];
        int offset = pixelStart;
        for (int i = 0; i < count; i++)
        {
            // The dimensions are the descriptor's last dword in both games, so they move with the
            // stride rather than needing an offset of their own.
            int dims = PrefixSize + (i * descriptorStride) + descriptorStride - 4;
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

        // The arithmetic closes exactly for all 25,333 I.G.I. 1 entries and all 31,542 I.G.I. 2 ones;
        // a leftover means a misread header.
        if (offset != span.Length)
        {
            throw new PackFormatException(
                $"An object lightmap's {count} images account for {offset} of {span.Length} bytes.");
        }

        return new ObjectLightmap(version, new ReadOnlyCollection<ObjectLightmapImage>(images));
    }

    /// <summary>The letters an I.G.I. 2 entry name uses to separate one task's lightmap groups.</summary>
    /// <remarks>
    /// <c>acIndexLetters</c> (<c>igi2ed 0x6CB554</c>) is <c>"!abcdefghijklmnopqrstuvwxyz"</c>. Index zero
    /// is the <c>!</c>, and it is never emitted: the writer takes the lettered format string only when
    /// the index is non-zero, so <c>a</c> is one, <c>b</c> is two, and a name with no letter is zero.
    /// The retail corpus uses <c>a</c>, <c>b</c>, <c>c</c> and <c>e</c>.
    /// </remarks>
    public const string VariantLetters = "!abcdefghijklmnopqrstuvwxyz";

    /// <summary>Splits an I.G.I. 2 lightmap entry name into its four parts.</summary>
    /// <param name="name">The entry name, with or without a directory prefix and with or without the extension.</param>
    /// <param name="taskId">Receives the level id of the task the lightmapped object belongs to.</param>
    /// <param name="variant">Receives the group letter as a number, or zero when the name carries none.</param>
    /// <param name="modelIndex">Receives the index of the model within that task's enumeration.</param>
    /// <param name="lod">Receives the level of detail, counted from one.</param>
    /// <returns><see langword="true"/> when the name has the expected shape.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The sequel names lightmaps differently enough to need its own parser rather than a parameter.
    /// I.G.I. 1 writes <c>&lt;objectName&gt;_&lt;%05d&gt;.olm</c>, where the number indexes the model's
    /// draw records; I.G.I. 2 writes <c>%04d_%04d_%1d.olm</c> or <c>%04d%c_%04d_%1d.olm</c>
    /// (<c>LightmapInfo_AddLightmapVirModelToEnumeration</c>, <c>igi2ed 0x625A20</c>), where the parts are
    /// a task id, an optional group letter, a model index and a level of detail.
    /// </para>
    /// <para>
    /// Reading the sequel's names with the first game's parser is the trap this exists to close: splitting
    /// on the last underscore succeeds, and hands back the <em>level of detail</em> where a draw-record
    /// index was expected.
    /// </para>
    /// </remarks>
    public static bool TryParseIgi2Name(string name, out int taskId, out int variant, out int modelIndex, out int lod)
    {
        ArgumentNullException.ThrowIfNull(name);

        taskId = 0;
        variant = 0;
        modelIndex = 0;
        lod = 0;

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

        int second = span.LastIndexOf('_');
        if (second <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> head = span[..second];
        int first = head.LastIndexOf('_');
        if (first <= 0)
        {
            return false;
        }

        if (!TryParseNumber(span[(second + 1)..], out lod) || !TryParseNumber(head[(first + 1)..], out modelIndex))
        {
            return false;
        }

        ReadOnlySpan<char> id = head[..first];
        if (id.Length > 0 && char.IsAsciiLetter(id[^1]))
        {
            int letter = VariantLetters.IndexOf(char.ToLowerInvariant(id[^1]), StringComparison.Ordinal);
            if (letter <= 0)
            {
                return false;
            }

            variant = letter;
            id = id[..^1];
        }

        return TryParseNumber(id, out taskId);
    }

    private static bool TryParseNumber(ReadOnlySpan<char> text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

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
